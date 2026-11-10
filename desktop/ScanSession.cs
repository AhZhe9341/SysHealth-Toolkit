namespace SysHealth;

internal enum ScanPhase { Checking, Completed, Failed }

internal sealed record ScanState(ScanPhase Phase, PrintReport? Report = null);

internal sealed class ScanSession(Func<Task<PrintReport>> collect, Action<ScanState> show)
{
    private bool started;
    private bool busy;

    public Task StartAsync()
    {
        if (started)
            return Task.CompletedTask;

        started = true;
        return CheckAgainAsync();
    }

    public async Task CheckAgainAsync()
    {
        if (busy)
            return;

        busy = true;
        try
        {
            show(new ScanState(ScanPhase.Checking));
            ScanState result;
            try
            {
                result = new ScanState(ScanPhase.Completed, await collect());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Print audit failed: {0}", ex.GetType().Name);
                result = new ScanState(ScanPhase.Failed);
            }
            show(result);
        }
        finally
        {
            busy = false;
        }
    }
}
