namespace SysHealth;

internal sealed record ReportPresentation(string Title, string NextStep, string Coverage, string Service,
    string Events, string Checked, string Summary)
{
    public static ReportPresentation From(PrintReport report)
    {
        var service = report.Spooler.Status == "unavailable"
            ? "Print service: " + DescribeError(report.Spooler.ErrorCode)
            : "Print service: " + (report.Spooler.State switch
            {
                "StartPending" => "Starting",
                "StopPending" => "Stopping",
                "ContinuePending" => "Resuming",
                "PausePending" => "Pausing",
                _ => report.Spooler.State ?? "Unavailable"
            });
        var events = report.Events.Status switch
        {
            "unavailable" => "Print events: " + DescribeError(report.Events.ErrorCode),
            "noEvents" => "Print events: No matching events in the last 24 hours",
            _ => $"Print events: {report.Events.SampledEvents} sampled{(report.Events.Truncated ? " (sample limit reached)" : "")}"
        };
        var title = report switch
        {
            { Spooler.State: "Stopped" } => "The print service is stopped",
            { Spooler.State: "Running", Events.Status: "unavailable" } => "The print service is running. The log could not be read.",
            { Spooler.State: "Running" } => "The print service is running",
            { Status: "unavailable" } => "Both checks are unavailable",
            { Spooler.Status: "unavailable" } => "The print service could not be checked",
            _ => "Check the print service status"
        };
        var next = report switch
        {
            { Spooler.State: "Stopped" } => "Open Windows Services and check Print Spooler. A stopped service can prevent printing.",
            { Spooler.ErrorCode: "accessDenied" } or { Events.ErrorCode: "accessDenied" }
                => "Ask your IT administrator to check whether your account can read the unavailable information.",
            { Spooler.Status: "unavailable" } => "Open Windows Services and look for Print Spooler. Then try the check again.",
            { Spooler.State: not "Running" } => "Open Windows Services to check the current state of Print Spooler.",
            { Events.ErrorCode: "logDisabled" } => "Check the PrintService Admin channel in Windows Event Viewer if you need recent print errors.",
            { Events.Status: "unavailable" } => "Open Windows Event Viewer and check whether the PrintService Admin log is available.",
            _ => "Check the printer connection and look for a stuck job in the print queue if printing still fails."
        };
        var checks = report.Status switch { "complete" => 2, "partial" => 1, _ => 0 };
        var coverage = $"{checks} of 2 checks completed";
        var time = $"Checked: {report.CollectedAtUtc.ToLocalTime():g}";
        var summary = string.Join(Environment.NewLine, "SysHealth print diagnostics", title, "Next step: " + next,
            coverage, service, events, time);
        return new ReportPresentation(title, next, coverage, service, events, time, summary);
    }

    private static string DescribeError(string? code) => code switch
    {
        "accessDenied" => "Access denied",
        "logDisabled" => "Logging is turned off",
        "logUnavailable" => "Log not found",
        "serviceNotFound" => "Service not found",
        _ => "Could not read this source"
    };
}
