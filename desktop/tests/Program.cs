using System.Text.Json;
using SysHealth;

var now = new DateTimeOffset(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);
var running = new ServiceFinding("ok", "Spooler", "Running", null);
var stopped = running with { State = "Stopped" };
var deniedService = new ServiceFinding("unavailable", "Spooler", null, "accessDenied");
var empty = new EventFinding("noEvents", "Microsoft-Windows-PrintService/Admin", 0, 200, false, [], null);
var deniedLog = empty with { Status = "unavailable", ErrorCode = "accessDenied" };
var report = PrintReport.Create(now, running, empty);

var tests = new (string Name, Func<Task> Run)[]
{
    ("Running and readable empty log are independent findings", () => Check(() =>
    {
        var view = ReportPresentation.From(report);
        Contains("running", view.Title);
        Contains("No matching events", view.Events);
        Contains("Check the printer connection", view.NextStep);
    })),
    ("Stopped service retains access-denied log and recovery guidance", () => Check(() =>
    {
        var result = PrintReport.Create(now, stopped, deniedLog);
        var view = ReportPresentation.From(result);
        Equal("partial", result.Status);
        Contains("stopped", view.Title);
        Contains("Access denied", view.Events);
        Contains("Windows Services", view.NextStep);
        Contains("Access denied", view.Summary);
    })),
    ("Readable source survives when the service cannot be read", () => Check(() =>
    {
        var view = ReportPresentation.From(PrintReport.Create(now, deniedService, empty));
        Contains("1 of 2 checks completed", view.Coverage);
        Contains("Access denied", view.Service);
        Contains("No matching events", view.Events);
    })),
    ("Neither source is available and retry guidance is actionable", () => Check(() =>
    {
        var result = PrintReport.Create(now, deniedService, deniedLog);
        var view = ReportPresentation.From(result);
        Equal("unavailable", result.Status);
        Contains("0 of 2 checks completed", view.Coverage);
        Contains("IT administrator", view.NextStep);
    })),
    ("Disabled channel and unexpected service state are explicit", () => Check(() =>
    {
        var result = PrintReport.Create(now, running with { State = "StartPending" },
            deniedLog with { ErrorCode = "logDisabled" });
        var view = ReportPresentation.From(result);
        Contains("Starting", view.Service);
        Contains("Logging is turned off", view.Events);
    })),
    ("Sample limit is visible in the result and copied summary", () => Check(() =>
    {
        var capped = empty with { Status = "ok", SampledEvents = 200, Truncated = true };
        var view = ReportPresentation.From(PrintReport.Create(now, running, capped));
        Contains("sample limit reached", view.Events);
        Contains("200", view.Summary);
        Contains("Next step", view.Summary);
    })),
    ("First startup starts once and prevents duplicate in-flight checks", async () =>
    {
        var pending = new TaskCompletionSource<PrintReport>();
        var calls = 0;
        var states = new List<ScanState>();
        var session = new ScanSession(() => { calls++; return pending.Task; }, states.Add);
        var first = session.StartAsync();
        await session.StartAsync();
        await session.CheckAgainAsync();
        Equal(1, calls);
        Equal(ScanPhase.Checking, states.Single().Phase);
        pending.SetResult(report);
        await first;
        await session.StartAsync();
        Equal(1, calls);
        Equal(ScanPhase.Completed, states.Last().Phase);
    }),
    ("Retry clears prior report on failure and can recover", async () =>
    {
        var states = new List<ScanState>();
        var attempts = 0;
        var session = new ScanSession(() => ++attempts == 2
            ? Task.FromException<PrintReport>(new InvalidOperationException())
            : Task.FromResult(report), states.Add);
        await session.StartAsync();
        await session.CheckAgainAsync();
        Equal(ScanPhase.Failed, states.Last().Phase);
        Equal<PrintReport?>(null, states.Last().Report);
        await session.CheckAgainAsync();
        Equal(3, attempts);
        Equal(report, states.Last().Report);
    }),
    ("Export uses the existing report schema and excludes display fields", () => Check(() =>
    {
        var logged = empty with { Status = "ok", SampledEvents = 2,
            Groups = [new EventGroup("Microsoft-Windows-PrintService", 372, 2, now.AddMinutes(-1))] };
        using var document = JsonDocument.Parse(ReportExport.Serialize(PrintReport.Create(now, running, logged)));
        var root = document.RootElement;
        Equal("check,collectedAtUtc,events,schemaVersion,sinceUtc,spooler,status",
            string.Join(',', root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Equal("print", root.GetProperty("check").GetString());
        Equal(now.AddHours(-24), root.GetProperty("sinceUtc").GetDateTimeOffset());
        var events = root.GetProperty("events");
        Equal("errorCode,groups,log,maxEvents,sampledEvents,status,truncated",
            string.Join(',', events.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        var group = events.GetProperty("groups")[0];
        Equal("count,eventId,lastSeenUtc,provider",
            string.Join(',', group.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
    })),
    ("Export replaces an approved target and cleans failed temporary writes", () => Check(() =>
    {
        var directory = Directory.CreateTempSubdirectory("syshealth-export-");
        try
        {
            var output = Path.Combine(directory.FullName, "audit.json");
            File.WriteAllText(output, "previous");
            ReportExport.Write(output, report);
            using var result = JsonDocument.Parse(File.ReadAllText(output));
            Equal(1, result.RootElement.GetProperty("schemaVersion").GetInt32());
            try
            {
                ReportExport.Write(directory.CreateSubdirectory("directory-target").FullName, report);
                throw new Exception("Expected export to fail for a directory target");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Equal(2, directory.GetFileSystemInfos().Length);
        }
        finally { directory.Delete(true); }
    }))
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static Task Check(Action test)
{
    test();
    return Task.CompletedTask;
}
static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new Exception($"Expected '{expected}' in '{actual}'");
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'");
}
