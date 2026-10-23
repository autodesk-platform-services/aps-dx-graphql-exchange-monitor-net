using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;

// A file or exchange picked in the tree
public class WatchedItem
{
    public string HubId { get; set; }
    public string ProjectId { get; set; }
    public string ItemId { get; set; }
    public string Name { get; set; }
    public string FolderId { get; set; }
    public string Region { get; set; }
}

// A webhook registered from the setup dialog, with the exchange to query when its callbacks arrive
public class Registration
{
    public WatchedItem Revit { get; set; }
    public WatchedItem Exchange { get; set; }
    // Data Exchange ID of the exchange, for the GraphQL queries
    public string ExchangeId { get; set; }
    public string CallbackUrl { get; set; }
    public string HookId { get; set; }
    public DateTime RegisteredAt { get; set; }
}

// A payload received on /webhook
public class WebhookEvent
{
    public int Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string Method { get; set; }
    public string Url { get; set; }
    public Dictionary<string, string> Headers { get; set; }
    // The parsed JSON, or the text as received when it is not JSON
    public JsonNode Body { get; set; }
    // The webhook that fired (hook.hookId of the payload), to tell the registrations apart
    public string HookId => (Body as JsonObject)?["hook"]?["hookId"]?.ToString();
}

// The registrations made in the setup dialog are saved to monitor.json, so that they survive a restart.
// The received payloads are only kept in memory (newest first).
public class MonitorStore
{
    private const int MaxEvents = 500;
    private readonly object _lock = new();
    private readonly string _path;
    private readonly List<WebhookEvent> _events = new();
    private readonly List<Registration> _registrations = new();
    private int _nextId = 1;

    public MonitorStore(IWebHostEnvironment env)
    {
        _path = Path.Combine(env.ContentRootPath, "monitor.json");
        if (File.Exists(_path))
        {
            try
            {
                _registrations.AddRange(JsonSerializer.Deserialize<List<Registration>>(File.ReadAllText(_path)));
            }
            catch (JsonException)
            {
                // Unreadable (or saved by an earlier version): start without registrations
            }
        }
    }

    public List<Registration> GetRegistrations()
    {
        lock (_lock)
        {
            return _registrations.ToList();
        }
    }

    public Registration GetRegistration(string hookId)
    {
        lock (_lock)
        {
            return _registrations.FirstOrDefault(registration => registration.HookId == hookId);
        }
    }

    // A webhook is registered on an exchange: registering the same exchange again replaces its registration
    public Registration FindByExchange(string exchangeItemId)
    {
        lock (_lock)
        {
            return _registrations.FirstOrDefault(registration => registration.Exchange.ItemId == exchangeItemId);
        }
    }

    public void Remove(string hookId)
    {
        lock (_lock)
        {
            _registrations.RemoveAll(registration => registration.HookId == hookId);
            Save();
        }
    }

    public void Add(Registration registration)
    {
        lock (_lock)
        {
            _registrations.RemoveAll(existing => existing.Exchange.ItemId == registration.Exchange.ItemId);
            _registrations.Add(registration);
            Save();
        }
    }

    private void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_registrations, new JsonSerializerOptions { WriteIndented = true }));
    }

    public WebhookEvent AddEvent(string method, string url, Dictionary<string, string> headers, JsonNode body)
    {
        lock (_lock)
        {
            var webhookEvent = new WebhookEvent { Id = _nextId++, ReceivedAt = DateTime.UtcNow, Method = method, Url = url, Headers = headers, Body = body };
            _events.Insert(0, webhookEvent);
            if (_events.Count > MaxEvents)
            {
                _events.RemoveAt(_events.Count - 1);
            }
            return webhookEvent;
        }
    }

    // Newest first; only the calls of one webhook when hookId is given
    public List<WebhookEvent> GetEvents(string hookId = null)
    {
        lock (_lock)
        {
            return _events.Where(webhookEvent => hookId == null || webhookEvent.HookId == hookId).ToList();
        }
    }
}
