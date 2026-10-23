using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

[ApiController]
public class WebhookController : ControllerBase
{
    private readonly MonitorStore _store;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(MonitorStore store, ILogger<WebhookController> logger)
    {
        _store = store;
        _logger = logger;
    }

    // Receives the callbacks of the webhook. It has no login: it's called by the Webhooks service.
    [HttpPost("/webhook")]
    public async Task<ActionResult> Receive()
    {
        using var reader = new StreamReader(Request.Body);
        var text = await reader.ReadToEndAsync();
        JsonNode body;
        try
        {
            body = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            body = JsonValue.Create(text); // Keep a payload that is not JSON viewable as it is
        }
        var headers = Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString());
        var webhookEvent = _store.AddEvent(Request.Method, Request.Path + Request.QueryString, headers, body);
        var payload = body as JsonObject; // Null when the body is not a JSON object
        _logger.LogInformation("Webhook #{Id}: {Event} {Name}", webhookEvent.Id, (string)payload?["hook"]?["event"], (string)payload?["payload"]?["name"]);
        return Ok();
    }

    // The received payloads, newest first (only those of one webhook with ?hook=): /change polls them to find out that a callback arrived
    [HttpGet("/api/events")]
    public ActionResult GetEvents([FromQuery] string hook) => Ok(_store.GetEvents(string.IsNullOrEmpty(hook) ? null : hook));
}
