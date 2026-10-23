using System.Threading.Tasks;

// What the monitor needs to know about a file or an exchange picked in the tree
public class ItemInfo
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; } // e.g., "items:autodesk.bim360:File", "items:autodesk.bim360:FDX"
    public ItemKind Kind { get; set; }
    // Data Management ID of the folder holding the item, e.g., "urn:adsk.wipprod:fs.folder:co.voPbEQk9R6i_enMyEDO1Dg"
    public string FolderId { get; set; }
    public string Region { get; set; }
}

public partial class APS
{
    public async Task<ItemInfo> GetItemInfo(string hubId, string projectId, string itemId, Tokens tokens)
    {
        var item = await GetAsync($"/data/v1/projects/{projectId}/items/{Encode(itemId)}", tokens.InternalToken);
        var hub = await GetAsync($"/project/v1/hubs/{hubId}", tokens.InternalToken);
        var attributes = item["data"]["attributes"];
        var name = (string)attributes["displayName"];
        var type = (string)attributes["extension"]?["type"];
        return new ItemInfo
        {
            Id = itemId,
            Name = name,
            Type = type,
            Kind = BrowserEntry.GetKind(name, type),
            FolderId = (string)item["data"]["relationships"]["parent"]["data"]["id"],
            Region = (string)hub["data"]["attributes"]["region"] ?? "US"
        };
    }
}
