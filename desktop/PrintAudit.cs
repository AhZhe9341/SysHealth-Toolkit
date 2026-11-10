using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Security;
using System.ServiceProcess;

namespace SysHealth;

internal static class PrintAudit
{
    private const string LogName = "Microsoft-Windows-PrintService/Admin";
    private const int MaxEvents = 200;

    public static PrintReport Collect()
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddHours(-24);
        var service = ReadService();
        var events = ReadEvents(since, now);
        return PrintReport.Create(now, service, events);
    }

    private static ServiceFinding ReadService()
    {
        try
        {
            using var service = new ServiceController("Spooler");
            return new ServiceFinding("ok", "Spooler", service.Status.ToString(), null);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            return new ServiceFinding("unavailable", "Spooler", null, "serviceNotFound");
        }
        catch (Exception ex) when (IsSourceError(ex))
        {
            return new ServiceFinding("unavailable", "Spooler", null,
                IsAccessDenied(ex) ? "accessDenied" : "serviceQueryFailed");
        }
    }

    private static EventFinding ReadEvents(DateTimeOffset since, DateTimeOffset until)
    {
        try
        {
            using var configuration = new EventLogConfiguration(LogName);
            if (!configuration.IsEnabled)
                return Unavailable("logDisabled");

            var from = since.UtcDateTime.ToString("O");
            var to = until.UtcDateTime.ToString("O");
            var filter = $"*[System[TimeCreated[@SystemTime >= '{from}' and @SystemTime <= '{to}']]]";
            var query = new EventLogQuery(LogName, PathType.LogName, filter) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            var events = new List<(string Provider, int Id, DateTimeOffset? Time)>(MaxEvents);
            var truncated = false;

            while (true)
            {
                using var record = reader.ReadEvent(TimeSpan.FromSeconds(5));
                if (record is null)
                    break;
                if (events.Count == MaxEvents)
                {
                    truncated = true;
                    break;
                }

                DateTimeOffset? timestamp = record.TimeCreated is DateTime time
                    ? new DateTimeOffset(time).ToUniversalTime()
                    : null;
                events.Add((record.ProviderName ?? "Unknown provider", record.Id, timestamp));
            }

            var groups = events.GroupBy(record => (record.Provider, record.Id))
                .Select(group => new EventGroup(group.Key.Provider, group.Key.Id, group.Count(), group.Max(row => row.Time)))
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.EventId)
                .ToArray();
            return new EventFinding(groups.Length == 0 ? "noEvents" : "ok", LogName, events.Count,
                MaxEvents, truncated, groups, null);
        }
        catch (EventLogNotFoundException)
        {
            return Unavailable("logUnavailable");
        }
        catch (Exception ex) when (IsSourceError(ex) || ex is System.TimeoutException)
        {
            return Unavailable(IsAccessDenied(ex) ? "accessDenied" : "eventQueryFailed");
        }
    }

    private static EventFinding Unavailable(string code) =>
        new("unavailable", LogName, 0, MaxEvents, false, [], code);

    private static bool IsSourceError(Exception ex) =>
        ex is InvalidOperationException or EventLogException or Win32Exception or SecurityException or UnauthorizedAccessException;

    private static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or Win32Exception { NativeErrorCode: 5 } ||
        ex.InnerException is Win32Exception { NativeErrorCode: 5 };
}
