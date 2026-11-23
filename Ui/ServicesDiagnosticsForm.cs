using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed class ServicesDiagnosticsForm : BrutalistDialog
{
    private readonly TextBox _preview;
    private readonly ListBox _history;
    private readonly Button _refresh;
    private readonly Button _copy;
    private readonly Button _copyEvents;
    private readonly Label _status;
    private readonly Action<string> _writeClipboard;
    private string _summary = "";

    internal ServicesDiagnosticsForm(bool darkMode, Action<string>? writeClipboard = null)
        : base("SERVICES && DIAGNOSTICS", new Size(652, 546),
            darkMode ? Palette.DarkCanvas : Palette.Cream)
    {
        Text = "Services & Diagnostics";
        _writeClipboard = writeClipboard ?? Clipboard.SetText;
        StartPosition = FormStartPosition.CenterScreen;
        var surface = darkMode ? Palette.DarkSurface : Palette.Paper;

        var tabs = new TabControl { Name = "DiagnosticTabs", Bounds = new Rectangle(5, 48, 642, 492) };
        var local = new TabPage("Local diagnostics") { BackColor = BackColor, ForeColor = ForeColor };
        var reportLabel = new Label { Text = "Support report preview", AutoSize = true,
            Location = new Point(12, 6), Font = Typography.WindowTitle };
        _preview = new TextBox
        {
            Name = "ReportPreview", AccessibleName = reportLabel.Text,
            Bounds = new Rectangle(12, 32, 608, 136), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = surface, ForeColor = ForeColor,
            Font = Typography.CompactMono, TabIndex = 0
        };
        var historyLabel = new Label { Text = "Session history (UTC)", AutoSize = true,
            Location = new Point(12, 184), Font = Typography.WindowTitle };
        var hint = new Label { Text = "Select events to include in the report. Use Ctrl or Shift to select several.",
            Location = new Point(12, 210), AutoSize = true };
        _history = new ListBox
        {
            Name = "SessionHistory", AccessibleName = historyLabel.Text,
            Bounds = new Rectangle(12, 236, 608, 130), IntegralHeight = false,
            SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true,
            Font = Typography.CompactMono, BackColor = surface, ForeColor = ForeColor, TabIndex = 1
        };
        _status = new Label { Bounds = new Rectangle(12, 376, 608, 24),
            AccessibleName = "Diagnostic status", Text = "Reading local observations..." };
        _refresh = BrutalistControls.CreateOutlinedButton("REFRESH", new Rectangle(12, 419, 96, 38),
            Typography.StatusDetail, surface, ForeColor);
        _refresh.TabIndex = 2;
        _refresh.Click += (_, _) => RefreshObservation();
        _copyEvents = BrutalistControls.CreateOutlinedButton("COPY EVENTS", new Rectangle(348, 419, 126, 38),
            Typography.StatusDetail, surface, ForeColor);
        _copyEvents.TabIndex = 4;
        _copyEvents.Name = "CopyEvents";
        _copyEvents.Enabled = false;
        _copyEvents.Click += (_, _) => CopyText(SelectedEvents());
        _history.SelectedIndexChanged += (_, _) => UpdatePreview();
        _copy = BrutalistControls.CreateOutlinedButton("COPY REPORT", new Rectangle(488, 419, 132, 38),
            Typography.StatusDetail, Palette.Yellow, Palette.Ink);
        _copy.TabIndex = 5;
        _copy.Name = "CopyReport";
        _copy.Enabled = false;
        _copy.Click += (_, _) => CopyText(_preview.Text);
        const string statusUrl = "https://support.rockstargames.com/servicestatus";
        var elevated = Program.IsRunningAsAdministrator();
        var rockstar = new LinkLabel { Text = elevated ? "Copy Rockstar status URL" : "Rockstar service status",
            AutoSize = true, Location = new Point(124, 430), LinkColor = darkMode ? Palette.Acid : Palette.Ink,
            TabIndex = 3 };
        void Visit(string url)
        {
            if (elevated)
            {
                CopyText(url);
                return;
            }
            try
            {
                using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                MessageBox.Show(this, "Windows could not open this link.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        rockstar.LinkClicked += (_, _) => Visit(statusUrl);
        local.Controls.AddRange([reportLabel, _preview, historyLabel, hint, _history, _status,
            _refresh, rockstar, _copyEvents, _copy]);
        tabs.TabPages.Add(local);
        Controls.Add(tabs);
        Shown += (_, _) => RefreshObservation();
    }

    private void RefreshObservation()
    {
        _refresh.Enabled = false;
        _status.Text = "Reading local observations...";
        var worker = new BackgroundWorker();
        worker.DoWork += (_, e) =>
        {
            var observation = LocalDiagnosticSnapshot.Read();
            string[]? history;
            try { history = DiagnosticHistory.Read(); }
            catch (Exception) { history = null; }
            e.Result = (observation, history);
        };
        worker.RunWorkerCompleted += (_, e) =>
        {
            worker.Dispose();
            if (IsDisposed || Disposing) return;
            _refresh.Enabled = true;
            if (e.Error is not null)
            {
                _status.Text = "Could not refresh local observations.";
                return;
            }
            var (observation, history) = ((LocalDiagnosticSnapshot, string[]?))e.Result!;
            Display(observation, history);
        };
        worker.RunWorkerAsync();
    }

    internal void Display(LocalDiagnosticSnapshot observation, string[]? history)
    {
        _summary = observation.FormatForSupport();
        _history.BeginUpdate();
        try
        {
            _history.Items.Clear();
            if (history is not null) _history.Items.AddRange(history);
        }
        finally { _history.EndUpdate(); }
        UpdatePreview();
        _copy.Enabled = true;
        _status.Text = history is null ? "Session history could not be read." :
            history.Length == 0 ? "No recorded events." : $"{history.Length} recorded events, newest first.";
    }

    private void UpdatePreview()
    {
        _copyEvents.Enabled = _history.SelectedItems.Count != 0;
        _preview.Text = _history.SelectedItems.Count == 0 ? _summary :
            _summary + Environment.NewLine + Environment.NewLine + "Selected events (UTC)" +
            Environment.NewLine + SelectedEvents();
    }

    private string SelectedEvents() =>
        string.Join(Environment.NewLine, _history.SelectedItems.Cast<string>());

    private void CopyText(string text)
    {
        try
        {
            _writeClipboard(text);
            _status.Text = "Copied to clipboard.";
        }
        catch (ExternalException)
        {
            _status.Text = "Clipboard unavailable. Try again.";
        }
    }
}
