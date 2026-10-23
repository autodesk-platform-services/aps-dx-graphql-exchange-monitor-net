using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

// What an item of the tree can be used for
public enum ItemKind
{
    // The Revit file to watch: its new versions trigger the webhook
    Revit,
    // The exchange to query for the changes
    Exchange,
    // Anything else: not shown in the tree
    Other
}

public class BrowserEntry
{
    public string Id;
    public string Name;
    public bool IsFolder;
    // Items: extension type (e.g., "items:autodesk.bim360:FDX" for exchanges)
    public string Type;
    public ItemKind Kind => IsFolder ? ItemKind.Other : GetKind(Name, Type);

    public static ItemKind GetKind(string name, string type)
    {
        if (type?.EndsWith(":FDX", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ItemKind.Exchange;
        }
        // Revit files uploaded to ACC, and Revit cloud models (items:autodesk.bim360:C4RModel)
        if (type?.EndsWith(":C4RModel", StringComparison.OrdinalIgnoreCase) == true || name?.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ItemKind.Revit;
        }
        return ItemKind.Other;
    }
}

// Calls the Data Management API directly rather than through the Autodesk.DataManagement SDK,
// whose enums do not cover newer values (e.g., hubs in the "DEU" region)
public partial class APS
{
    private static async Task<List<JsonNode>> GetAllPages(string url, string accessToken)
    {
        var results = new List<JsonNode>();
        while (url != null)
        {
            var page = await GetAsync(url, accessToken);
            results.AddRange(page["data"].AsArray());
            url = (string)page["links"]?["next"]?["href"];
        }
        return results;
    }

    public async Task<IEnumerable<BrowserEntry>> GetHubs(Tokens tokens)
    {
        var hubs = await GetAllPages("/project/v1/hubs", tokens.InternalToken);
        return hubs.Select(hub => new BrowserEntry { Id = (string)hub["id"], Name = (string)hub["attributes"]["name"] });
    }

    public async Task<IEnumerable<BrowserEntry>> GetProjects(string hubId, Tokens tokens)
    {
        var projects = await GetAllPages($"/project/v1/hubs/{hubId}/projects", tokens.InternalToken);
        return projects.Select(project => new BrowserEntry { Id = (string)project["id"], Name = (string)project["attributes"]["name"] });
    }

    public async Task<IEnumerable<BrowserEntry>> GetTopFolders(string hubId, string projectId, Tokens tokens)
    {
        var folders = await GetAllPages($"/project/v1/hubs/{hubId}/projects/{projectId}/topFolders", tokens.InternalToken);
        return folders.Select(folder => new BrowserEntry { Id = (string)folder["id"], Name = (string)folder["attributes"]["displayName"], IsFolder = true });
    }

    // The latest version of an item
    public async Task<string> GetTipVersionId(string projectId, string itemId, Tokens tokens)
    {
        var tip = await GetAsync($"/data/v1/projects/{projectId}/items/{Encode(itemId)}/tip", tokens.InternalToken);
        return (string)tip["data"]["id"];
    }

    public async Task<IEnumerable<BrowserEntry>> GetFolderContents(string projectId, string folderId, Tokens tokens)
    {
        var contents = await GetAllPages($"/data/v1/projects/{projectId}/folders/{Encode(folderId)}/contents", tokens.InternalToken);
        return contents.Select(entry => new BrowserEntry
        {
            Id = (string)entry["id"],
            Name = (string)entry["attributes"]["displayName"],
            IsFolder = (string)entry["type"] == "folders",
            Type = (string)entry["attributes"]["extension"]?["type"]
        });
    }
}
