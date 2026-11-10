using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace SysHealth;

public partial class MainWindow : Window
{
    private readonly ScanSession session;
    private PrintReport? report;
    private string summary = "";

    public MainWindow()
    {
        InitializeComponent();
        session = new ScanSession(() => Task.Run(PrintAudit.Collect), ShowScanState);
        ContentRendered += FirstRender;
    }

    private async void FirstRender(object? sender, EventArgs e)
    {
        ContentRendered -= FirstRender;
        await session.StartAsync();
    }

    private async void ScanClicked(object sender, RoutedEventArgs e)
    {
        var restoreFocus = ScanButton.IsKeyboardFocused;
        await session.CheckAgainAsync();

        var focused = Keyboard.FocusedElement;
        if (restoreFocus && IsActive && ScanButton.IsEnabled &&
            (focused is null || focused == this || focused == ContentScroll))
        {
            ScanButton.Focus();
        }
    }

    private void ShowScanState(ScanState state)
    {
        if (!IsVisible)
            return;

        var checking = state.Phase == ScanPhase.Checking;
        report = state.Report;
        summary = "";
        ScanButton.IsEnabled = !checking;
        CopyButton.IsEnabled = report is not null;
        Details.IsEnabled = report is not null;
        Findings.Visibility = report is null ? Visibility.Collapsed : Visibility.Visible;
        NextLabel.Visibility = checking ? Visibility.Collapsed : Visibility.Visible;
        Progress.IsIndeterminate = checking;
        Progress.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;

        if (checking)
        {
            Details.IsExpanded = false;
            EventList.ItemsSource = null;
            Feedback.Text = "";
            ExportFeedback.Text = "";
            NextStep.Text = "Reading the print service and the last 24 hours of print events.";
            Announce(Headline, "Checking this computer…");
        }
        else if (report is PrintReport result)
        {
            ShowReport(result);
        }
        else
        {
            NextStep.Text = "Select Check again to retry. If this continues, check Print Spooler in Windows Services.";
            Announce(Headline, "The check could not finish");
        }
    }

    private void ShowReport(PrintReport result)
    {
        var view = ReportPresentation.From(result);
        NextStep.Text = view.NextStep;
        CoverageText.Text = view.Coverage;
        ServiceText.Text = view.Service;
        EventText.Text = view.Events;
        CheckedText.Text = view.Checked;
        LogText.Text = $"Log: {result.Events.Log}\nService result: {result.Spooler.ErrorCode ?? result.Spooler.Status}\nEvent result: {result.Events.ErrorCode ?? result.Events.Status}";
        var groups = result.Events.Groups;
        EventList.Tag = groups.Count == 0 ? 1 : groups.Max(group => group.Count);
        EventList.ItemsSource = groups;
        GroupHeading.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        summary = view.Summary;
        Announce(Headline, view.Title);
    }

    private static void Announce(TextBlock target, string text)
    {
        target.Text = text;
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            UIElementAutomationPeer.CreatePeerForElement(target)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(summary);
            Announce(Feedback, "Summary copied.");
        }
        catch (ExternalException)
        {
            Announce(Feedback, "Windows could not copy the summary. Try again.");
        }
    }

    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (report is not PrintReport result)
            return;

        ExportFeedback.Text = "";
        var dialog = new SaveFileDialog
        {
            Filter = "JSON report (*.json)|*.json", FileName = "print-audit.json", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            ReportExport.Write(dialog.FileName, result);
            Announce(ExportFeedback, "Report saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Announce(ExportFeedback, "The report could not be saved. Choose a different location and try again.");
        }
    }
}
