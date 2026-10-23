using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ChangeController : ControllerBase
{
    private readonly APS _aps;
    private readonly MonitorStore _store;

    public ChangeController(APS aps, MonitorStore store)
    {
        _aps = aps;
        _store = store;
    }

    // What changed in the exchange of a registered webhook between its latest version and the previous one
    [HttpGet()]
    public async Task<ActionResult> GetChanges([FromQuery] string hook)
    {
        var tokens = await AuthController.PrepareTokens(Request, Response, _aps);
        if (tokens == null)
        {
            return Unauthorized();
        }
        var config = _store.GetRegistration(hook);
        if (config == null)
        {
            return NotFound("This webhook is not registered. Pick one on the Watchers page.");
        }
        try
        {
            var changes = await _aps.GetExchangeChanges(config.ExchangeId, config.Exchange.Region, tokens);
            return Ok(new { exchange = config.Exchange.Name, checkedAt = DateTime.UtcNow, changes });
        }
        catch (Exception ex) when (ex is ApplicationException or APSException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }
}
