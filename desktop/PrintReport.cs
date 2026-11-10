using System.Text.Json.Serialization;

namespace SysHealth;

internal sealed record ServiceFinding(string Status, string Name, string? State, string? ErrorCode);
internal sealed record EventGroup(string Provider, int EventId, int Count, DateTimeOffset? LastSeenUtc)
{
    [JsonIgnore]
    public string LastSeenLocal => LastSeenUtc?.ToLocalTime().ToString("g") ?? "Unavailable";
}
internal sealed record EventFinding(string Status, string Log, int SampledEvents, int MaxEvents,
    bool Truncated, IReadOnlyList<EventGroup> Groups, string? ErrorCode);

internal sealed record PrintReport(int SchemaVersion, DateTimeOffset CollectedAtUtc, DateTimeOffset SinceUtc,
    string Check, string Status, ServiceFinding Spooler, EventFinding Events)
{
    public static PrintReport Create(DateTimeOffset now, ServiceFinding service, EventFinding events)
    {
        var status = (service.Status == "unavailable", events.Status == "unavailable") switch
        {
            (true, true) => "unavailable",
            (false, false) => "complete",
            _ => "partial"
        };
        return new PrintReport(1, now, now.AddHours(-24), "print", status, service, events);
    }
}
