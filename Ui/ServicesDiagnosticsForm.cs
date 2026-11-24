using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed class ServicesDiagnosticsForm : ThemedDialog
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
        : base("SERVICES && DIAGNOSTICS", new Size(760, 426), darkMode)
    {
        Text = "Services & Diagnostics";
        _writeClipboard = writeClipboard ?? Clipboard.SetText;
        StartPosition = FormStartPosition.CenterScreen;
        var surface = Role(Palette.Surface);
        var control = Role(Palette.Control);

        // The native tab strip cannot be themed. Its headers are collapsed to nothing and
        // the buttons built below stand in for them; the pages and Ctrl+Tab still work.
        var tabs = new TabControl
        {
            Name = "DiagnosticTabs",
            Bounds = new Rectangle(5, 78, 750, 340),
            Appearance = TabAppearance.FlatButtons,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(0, 1)
        };
        var muted = Role(Palette.Muted);
        var local = new TabPage("Local diagnostics") { BackColor = BackColor, ForeColor = ForeColor };
        // The two cards the report and the history sit on; the controls are inset in them.
        // 210 high: the history shows nine whole rows, with no row cut in half.
        var reportCard = new Rectangle(12, 26, 322, 210);
        var historyCard = new Rectangle(346, 26, 384, 210);
        local.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            MenuGlyphs.DrawCard(e.Graphics, reportCard, surface, Role(Palette.Line));
            MenuGlyphs.DrawCard(e.Graphics, historyCard, surface, Role(Palette.Line));
        };
        var reportLabel = new Label { Text = "Support report preview", AutoSize = true,
            Location = new Point(12, 4), Font = Typography.MenuCaption, ForeColor = muted };
        _preview = ControlFactory.WithThemedScrollBars(new TextBox
        {
            Name = "ReportPreview", AccessibleName = reportLabel.Text,
            Bounds = Rectangle.Inflate(reportCard, -9, -9), Multiline = true, ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical, BackColor = surface, ForeColor = ForeColor,
            Font = Typography.CompactMono, TabIndex = 0
        }, darkMode);
        var historyLabel = new Label { Text = "Session history (UTC)", AutoSize = true,
            Location = new Point(346, 4), Font = Typography.MenuCaption, ForeColor = muted };
        var hint = new Label { Text = "Select events to include in the report. Use Ctrl or Shift to select several.",
            Location = new Point(12, 242), AutoSize = true, Font = Typography.MenuDetail, ForeColor = muted };
        _history = ControlFactory.WithThemedScrollBars(new ListBox
        {
            Name = "SessionHistory", AccessibleName = historyLabel.Text,
            Bounds = Rectangle.Inflate(historyCard, -5, -6), IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            SelectionMode = SelectionMode.MultiExtended,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22,
            Font = Typography.CompactMono, BackColor = surface, ForeColor = ForeColor, TabIndex = 1
        }, darkMode);
        _history.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var selected = (e.State & DrawItemState.Selected) != 0;
            var back = selected ? Palette.Mix(surface, Palette.Blue, 0.30) : surface;
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            // An entry is "<UTC time>  <event>": the time is context, the event is the content.
            // The copied text keeps the full "yyyy-MM-dd HH:mm:ssZ"; the list drops the year
            // and the Z, which its heading already states.
            var entry = (string)_history.Items[e.Index];
            var split = entry.IndexOf("  ", StringComparison.Ordinal);
            var left = e.Bounds.X + 6;
            if (split > 0)
            {
                var time = split == 20 ? entry.Substring(5, 14) : entry.Substring(0, split);
                time += "  ";
                TextRenderer.DrawText(e.Graphics, time, e.Font, new Rectangle(
                    left, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height), muted, back, flags);
                // Measured without the ellipsis flag: with it and no bounds, the width
                // comes back as the first few characters only.
                left += TextRenderer.MeasureText(e.Graphics, time, e.Font,
                    new Size(int.MaxValue, e.Bounds.Height),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                    TextFormatFlags.SingleLine).Width;
            }
            TextRenderer.DrawText(e.Graphics, split > 0 ? entry.Substring(split + 2) : entry,
                e.Font, new Rectangle(
                    left, e.Bounds.Y, e.Bounds.Right - left - 4, e.Bounds.Height),
                ForeColor, back, flags);
        };
        _status = new Label { Bounds = new Rectangle(12, 264, 718, 20),
            Font = Typography.MenuDetail,
            AccessibleName = "Diagnostic status", Text = "Reading local observations..." };
        _refresh = ControlFactory.MakeButton("REFRESH", new Rectangle(12, 292, 96, 32),
            Typography.MenuCaption, control, ForeColor);
        _refresh.TabIndex = 2;
        _refresh.Click += (_, _) => RefreshObservation();
        _copyEvents = ControlFactory.MakeButton("COPY EVENTS", new Rectangle(480, 292, 122, 32),
            Typography.MenuCaption, control, ForeColor);
        _copyEvents.TabIndex = 4;
        _copyEvents.Name = "CopyEvents";
        _copyEvents.Enabled = false;
        _copyEvents.Click += (_, _) => CopyText(SelectedEvents());
        _history.SelectedIndexChanged += (_, _) => UpdatePreview();
        _copy = ControlFactory.MakeButton("COPY REPORT", new Rectangle(610, 292, 120, 32),
            Typography.MenuCaption, Palette.Blue, Palette.Ink);
        _copy.TabIndex = 5;
        _copy.Name = "CopyReport";
        _copy.Enabled = false;
        _copy.Click += (_, _) => CopyText(_preview.Text);
        const string statusUrl = "https://support.rockstargames.com/servicestatus";
        var elevated = Program.IsRunningAsAdministrator();
        var rockstar = ControlFactory.MakeButton(
            elevated ? "Copy Rockstar status URL" : "Rockstar service status",
            new Rectangle(116, 292, 180, 32), Typography.MenuCaption, control, ForeColor);
        rockstar.Verbatim = true;
        rockstar.TabIndex = 3;
        void Visit(string url)
        {
            if (url != statusUrl && !VaultLoopFeed.IsProjectLink(url)) return;
            if (elevated)
            {
                CopyText(url);
                if (tabs.SelectedTab is OnlineServicesPage page) page.ShowLinkStatus(_status.Text);
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
        rockstar.Click += (_, _) => Visit(statusUrl);
        local.Controls.AddRange([reportLabel, _preview, historyLabel, hint, _history, _status,
            _refresh, rockstar, _copyEvents, _copy]);
        var online = new OnlineServicesPage(darkMode, elevated, Visit);
        tabs.TabPages.AddRange([local, online]);
        var tabButtons = new SoftButton[tabs.TabPages.Count];
        for (var index = 0; index < tabButtons.Length; index++)
        {
            var page = index;
            var button = ControlFactory.MakeButton(
                tabs.TabPages[index].Text, new Rectangle(16 + index * 158, 48, 150, 26),
                Typography.MenuCaption, BackColor, ForeColor);
            button.Verbatim = true;
            button.Click += (_, _) => tabs.SelectedIndex = page;
            tabButtons[index] = button;
            Controls.Add(button);
        }
        void ShowSelectedTab()
        {
            // Before its handle exists the tab control reports no selection; it opens on the first.
            var current = Math.Max(tabs.SelectedIndex, 0);
            for (var index = 0; index < tabButtons.Length; index++)
            {
                var selected = index == current;
                tabButtons[index].BackColor = selected ? control : BackColor;
                tabButtons[index].ForeColor = selected ? ForeColor : Role(Palette.Muted);
            }
        }
        tabs.SelectedIndexChanged += (_, _) =>
        {
            ShowSelectedTab();
            if (tabs.SelectedTab == online) online.LoadSavedResults();
        };
        ShowSelectedTab();
        // The control paints a system-colored frame around its page; clip it to the page.
        void ClipToPage()
        {
            if (!tabs.IsHandleCreated) return;
            // The collapsed header still leaves its separator on the first rows of the page.
            var page = tabs.DisplayRectangle;
            tabs.Region = new Region(Rectangle.FromLTRB(page.Left, page.Top + 2, page.Right, page.Bottom));
        }
        tabs.HandleCreated += (_, _) => ClipToPage();
        tabs.SizeChanged += (_, _) => ClipToPage();
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
        // A text box keeps a dead scroll bar when everything fits; show it only when needed.
        var lines = _preview.GetLineFromCharIndex(_preview.TextLength) + 1;
        var scrollBars = lines * _preview.Font.Height > _preview.Height
            ? ScrollBars.Vertical : ScrollBars.None;
        if (_preview.ScrollBars != scrollBars) _preview.ScrollBars = scrollBars;
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
