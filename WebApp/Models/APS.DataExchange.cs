using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public class Exchange
{
    public string Id;
    public string Name;
}

// One element that differs between the latest version of an exchange and the previous one
public class ExchangeChange
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string ElementName { get; set; }
    public string Operation { get; set; } // e.g., "INSERT", "MODIFY", "DELETE"
}

public partial class APS
{
    private const string DataExchangeUrl = "https://developer.api.autodesk.com/dataexchange/2023-05/graphql";

    // operation names the call in error messages, since several calls can fail with the same generic error
    private static async Task<JsonNode> QueryDataExchange(string operation, string query, JsonObject variables, string region, Tokens tokens)
    {
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(region) && region != "US")
        {
            headers["Region"] = region;
        }
        var result = await PostAsync(DataExchangeUrl, tokens.InternalToken, new JsonObject { ["query"] = query, ["variables"] = variables }, headers: headers);
        var errors = result["errors"]?.AsArray();
        if (errors != null && errors.Count > 0)
        {
            // Include the path and the error code (extensions) when present, they tell more than the message
            throw new ApplicationException($"Data Exchange {operation} failed: " + string.Join("; ", errors.Select(error =>
            {
                var details = new[] { error["path"]?.ToJsonString(), error["extensions"]?.ToJsonString() }.Where(d => d != null);
                return $"{(string)error["message"]} {string.Join(" ", details)}".Trim();
            })));
        }
        return result["data"];
    }

    // Data Exchange IDs are base64-encoded strings such as "fold~<hub>~<project>~<folder>", built from the Data Management IDs.
    // For ACC IDs the encoding never needs padding or the characters where standard and URL-safe base64 differ, so any
    // base64 encoder gives the same ID
    private static string DataExchangeId(params string[] parts)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("~", parts)));
    }

    public async Task<string> GetDataExchangeFolderId(string hubId, string projectId, string folderId, string region, Tokens tokens)
    {
        const string query = "query GetFolder($folderId: ID!) { folder(folderId: $folderId) { id name } }";
        string id;
        try
        {
            var data = await QueryDataExchange("folder lookup", query, new JsonObject { ["folderId"] = DataExchangeId("fold", hubId, projectId, folderId) }, region, tokens);
            id = (string)data?["folder"]?["id"];
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"Could not find the folder in the Data Exchange API. {ex.Message}");
        }
        return id ?? throw new ApplicationException("Could not find the folder in the Data Exchange API.");
    }

    // The exchange item in ACC has the name of the exchange, which is how it's found in the folder
    public async Task<Exchange> FindExchangeInFolder(string dataExchangeFolderId, string exchangeName, string region, Tokens tokens)
    {
        const string query = "query GetFolderExchanges($folderId: ID!) { folder(folderId: $folderId) { exchanges { results { id name } } } }";
        var data = await QueryDataExchange("folder exchanges", query, new JsonObject { ["folderId"] = dataExchangeFolderId }, region, tokens);
        var match = data["folder"]["exchanges"]["results"].AsArray().FirstOrDefault(exchange => (string)exchange["name"] == exchangeName);
        return match == null ? null : new Exchange { Id = (string)match["id"], Name = (string)match["name"] };
    }

    // The elements that changed between the latest version of the exchange and the one before. An empty list means that
    // the latest change of the source file did not affect the elements of the exchange.
    public async Task<List<ExchangeChange>> GetExchangeChanges(string exchangeId, string region, Tokens tokens)
    {
        const string query = @"query GetExchangeByVersions($exchangeId: ID!, $filter: ExchangeFilterInput!) {
            exchangeByVersions(exchangeId: $exchangeId, filter: $filter) {
                elements {
                    results {
                        id
                        name
                        properties(filter: { names: [""Element Name"", ""Operation""] }) {
                            results { name value }
                        }
                    }
                }
            }
        }";
        var variables = new JsonObject
        {
            ["exchangeId"] = exchangeId,
            ["filter"] = new JsonObject { ["query"] = "version.fromId==latest-1 and version.toId==latest" }
        };
        var data = await QueryDataExchange("exchangeByVersions", query, variables, region, tokens);
        var results = data?["exchangeByVersions"]?["elements"]?["results"]?.AsArray() ?? [];
        return results.Select(element =>
        {
            var properties = (element["properties"]?["results"]?.AsArray() ?? [])
                .GroupBy(property => (string)property["name"])
                .ToDictionary(group => group.Key, group => group.First()["value"]?.ToString());
            return new ExchangeChange
            {
                Id = (string)element["id"],
                Name = (string)element["name"],
                ElementName = properties.GetValueOrDefault("Element Name"),
                Operation = properties.GetValueOrDefault("Operation")
            };
        }).ToList();
    }
}
