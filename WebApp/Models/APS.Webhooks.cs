using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

// Webhooks API: the "dm.version.added" event of the Data Management system fires when a new version of a file is added
public partial class APS
{
    private const string VersionAddedHooksUrl = BaseUrl + "/webhooks/v1/systems/data/events/dm.version.added/hooks";

    // Registers a hook for the new versions of one item (the lineage, i.e., the Data Management item ID), here an exchange,
    // within the folder that holds it. Returns the ID of the hook.
    public async Task<string> CreateVersionAddedHook(string callbackUrl, string folderId, string lineageId, string note, Tokens tokens)
    {
        var body = new JsonObject
        {
            ["callbackUrl"] = callbackUrl,
            ["scope"] = new JsonObject { ["folder"] = folderId },
            ["filter"] = $"$[?(@.lineageUrn==\"{lineageId}\")]",
            ["hookAttribute"] = new JsonObject { ["note"] = note }
        };
        // The ID of the new hook is in the Location header, so this doesn't use the SendAsync helper, which only returns the body
        using var request = new HttpRequestMessage(HttpMethod.Post, VersionAddedHooksUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.InternalToken);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new APSException(response.StatusCode, $"POST {VersionAddedHooksUrl} failed ({(int)response.StatusCode}): {text}");
        }
        var location = response.Headers.Location?.ToString();
        if (!string.IsNullOrEmpty(location))
        {
            return location.TrimEnd('/')[(location.TrimEnd('/').LastIndexOf('/') + 1)..];
        }
        return string.IsNullOrWhiteSpace(text) ? null : (string)JsonNode.Parse(text)?["hookId"];
    }

    // Every webhook the token can see, in any system and for any event (GET /webhooks/v1/hooks), page after page
    public async Task<List<JsonNode>> GetHooks(Tokens tokens)
    {
        var hooks = new List<JsonNode>();
        var url = "/webhooks/v1/hooks";
        while (url != null)
        {
            var page = await GetAsync(url, tokens.InternalToken);
            if (page == null)
            {
                break; // 204: no webhooks
            }
            hooks.AddRange(page["data"]?.AsArray() ?? []);
            // "links.next" is relative to the Webhooks API: "/hooks?pageState=..."
            var next = (string)page["links"]?["next"];
            url = string.IsNullOrEmpty(next) ? null : "/webhooks/v1" + next;
        }
        return hooks;
    }

    // Deletes a webhook of any system and event, e.g., "data" and "dm.version.added". Already deleted is fine.
    public async Task DeleteHook(string system, string eventName, string hookId, Tokens tokens)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"{BaseUrl}/webhooks/v1/systems/{Encode(system)}/events/{Encode(eventName)}/hooks/{Encode(hookId)}", tokens.InternalToken);
        }
        catch (APSException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone
        }
    }

    public Task DeleteVersionAddedHook(string hookId, Tokens tokens) => DeleteHook("data", "dm.version.added", hookId, tokens);
}
