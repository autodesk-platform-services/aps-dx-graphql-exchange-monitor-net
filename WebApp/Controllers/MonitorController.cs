using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public class PickedItem
{
    public string HubId { get; set; }
    public string ProjectId { get; set; }
    public string ItemId { get; set; }
}

public class RegisterRequest
{
    public PickedItem Revit { get; set; }
    public PickedItem Exchange { get; set; }
    public string CallbackUrl { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class MonitorController : ControllerBase
{
    private readonly APS _aps;
    private readonly MonitorStore _store;

    public MonitorController(APS aps, MonitorStore store)
    {
        _aps = aps;
        _store = store;
    }

    // Every webhook of the account (not only the ones registered here), with how many calls each one received and,
    // for the ones registered here, the Revit file and the exchange
    [HttpGet()]
    public async Task<ActionResult> GetHooks()
    {
        var tokens = await AuthController.PrepareTokens(Request, Response, _aps);
        if (tokens == null)
        {
            return Unauthorized();
        }
        try
        {
            var events = _store.GetEvents();
            var registrations = _store.GetRegistrations();
            return Ok((await _aps.GetHooks(tokens)).Select(hook =>
            {
                var hookId = (string)hook["hookId"];
                var calls = events.Where(webhookEvent => webhookEvent.HookId == hookId).ToList();
                return new
                {
                    hookId,
                    system = (string)hook["system"],
                    @event = (string)hook["event"],
                    callbackUrl = (string)hook["callbackUrl"],
                    status = (string)hook["status"],
                    createdDate = (string)hook["createdDate"],
                    folder = (string)hook["scope"]?["folder"],
                    note = (string)hook["hookAttribute"]?["note"],
                    calls = calls.Count,
                    lastCallAt = calls.FirstOrDefault()?.ReceivedAt,
                    registration = registrations.FirstOrDefault(registration => registration.HookId == hookId)
                };
            }));
        }
        catch (APSException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    // The webhooks registered here (kept by this app, no call to APS): /change finds the exchange of a webhook in them
    [HttpGet("registrations")]
    public ActionResult GetRegistrations() => Ok(_store.GetRegistrations());

    // Registers the webhook on the exchange, and remembers it to query when the callback arrives. The exchange gets a new
    // version once it has synced with a new version of its Revit file, so the callback means the exchange can be queried.
    [HttpPost()]
    public async Task<ActionResult> Register([FromBody] RegisterRequest request)
    {
        var tokens = await AuthController.PrepareTokens(Request, Response, _aps);
        if (tokens == null)
        {
            return Unauthorized();
        }
        if (!IsComplete(request.Revit) || !IsComplete(request.Exchange))
        {
            return BadRequest("Pick a Revit file and an exchange.");
        }
        if (!Uri.TryCreate(request.CallbackUrl?.Trim(), UriKind.Absolute, out var callbackUrl) || callbackUrl.Scheme != Uri.UriSchemeHttps)
        {
            return BadRequest("The callback URL must be a public HTTPS URL, e.g., https://<your ngrok domain>/webhook.");
        }
        try
        {
            var revit = await _aps.GetItemInfo(request.Revit.HubId, request.Revit.ProjectId, request.Revit.ItemId, tokens);
            if (revit.Kind != ItemKind.Revit)
            {
                return BadRequest($"\"{revit.Name}\" is not a Revit file.");
            }
            var exchange = await _aps.GetItemInfo(request.Exchange.HubId, request.Exchange.ProjectId, request.Exchange.ItemId, tokens);
            if (exchange.Kind != ItemKind.Exchange)
            {
                return BadRequest($"\"{exchange.Name}\" is not an exchange.");
            }
            // The exchange is queried with its Data Exchange ID, found by name in its folder
            var folderId = await _aps.GetDataExchangeFolderId(request.Exchange.HubId, request.Exchange.ProjectId, exchange.FolderId, exchange.Region, tokens);
            var dataExchange = await _aps.FindExchangeInFolder(folderId, exchange.Name, exchange.Region, tokens)
                ?? throw new ApplicationException($"The exchange \"{exchange.Name}\" was not found in the Data Exchange API.");

            // One webhook per exchange: registering it again replaces the previous one
            var previous = _store.FindByExchange(request.Exchange.ItemId);
            if (previous?.HookId != null)
            {
                await _aps.DeleteVersionAddedHook(previous.HookId, tokens);
            }
            // The scope is the folder of the exchange, and the filter its lineage (the item ID). The note names the Revit file,
            // which the payload (about the exchange) doesn't mention.
            var note = $"The Revit file \"{revit.Name}\" changed: the exchange \"{exchange.Name}\" has a new version.";
            // Webhooks of the same event and folder need different callback URLs: two exchanges in one folder would otherwise
            // be refused as duplicates (409). The suffix is ignored by /webhook.
            var hookUrl = WithExchangeSuffix(callbackUrl.ToString(), exchange.Id);
            string hookId;
            try
            {
                hookId = await _aps.CreateVersionAddedHook(hookUrl, exchange.FolderId, exchange.Id, note, tokens);
            }
            catch (APSException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return Conflict($"A webhook for new versions in this folder with the callback URL {hookUrl} already exists (it was not registered by this app, or the registration was lost). Delete it on the Watchers page, then register again. {ex.Message}");
            }
            var config = new Registration
            {
                Revit = ToWatchedItem(request.Revit, revit),
                Exchange = ToWatchedItem(request.Exchange, exchange),
                ExchangeId = dataExchange.Id,
                CallbackUrl = hookUrl,
                HookId = hookId,
                RegisteredAt = DateTime.UtcNow
            };
            _store.Add(config);
            return Ok(config);
        }
        catch (Exception ex) when (ex is ApplicationException or APSException)
        {
            // Data Management, Webhooks and Data Exchange errors (e.g., an unreachable callback URL), reported as they are
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    // Deletes a webhook of the account, and forgets its registration if it was registered here
    [HttpDelete("{hookId}")]
    public async Task<ActionResult> Unregister(string hookId, [FromQuery] string system, [FromQuery] string @event)
    {
        var tokens = await AuthController.PrepareTokens(Request, Response, _aps);
        if (tokens == null)
        {
            return Unauthorized();
        }
        // A webhook is deleted by system and event too; the ones registered here are known to be "data" and "dm.version.added"
        var registered = _store.GetRegistration(hookId) != null;
        system = string.IsNullOrEmpty(system) && registered ? "data" : system;
        @event = string.IsNullOrEmpty(@event) && registered ? "dm.version.added" : @event;
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(@event))
        {
            return BadRequest("Missing system or event of the webhook.");
        }
        try
        {
            await _aps.DeleteHook(system, @event, hookId, tokens);
        }
        catch (APSException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
        _store.Remove(hookId);
        return NoContent();
    }

    // "https://host/webhook" -> "https://host/webhook?exchange=1a2b3c4d" (8 hex characters of the hash of the exchange's item ID)
    private static string WithExchangeSuffix(string url, string exchangeItemId)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(exchangeItemId)))[..8].ToLowerInvariant();
        return $"{url}{(url.Contains('?') ? '&' : '?')}exchange={hash}";
    }

    private static bool IsComplete(PickedItem item)
        => !string.IsNullOrEmpty(item?.HubId) && !string.IsNullOrEmpty(item.ProjectId) && !string.IsNullOrEmpty(item.ItemId);

    private static WatchedItem ToWatchedItem(PickedItem picked, ItemInfo info) => new()
    {
        HubId = picked.HubId,
        ProjectId = picked.ProjectId,
        ItemId = picked.ItemId,
        Name = info.Name,
        FolderId = info.FolderId,
        Region = info.Region
    };
}
