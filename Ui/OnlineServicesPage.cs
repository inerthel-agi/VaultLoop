using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

internal sealed class OnlineServicesPage : TabPage
{
    private readonly CancellationTokenSource _closing = new();
    private readonly Action<bool> _readRelease;
    private readonly Action<bool> _readCatalog;
    private bool _loaded;

    internal OnlineServicesPage(bool dark, bool elevated, Action<string> visit) : base("Online services")
    {
        Name = "OnlineServices";
        BackColor = dark ? Palette.DarkCanvas : Palette.Cream;
        ForeColor = dark ? Palette.Paper : Palette.Ink;
        var surface = dark ? Palette.DarkSurface : Palette.Paper;
        _readRelease = AddFeed(FeedKind.Release, "VaultLoop updates", "CHECK FOR UPDATES",
            new Rectangle(12, 52, 608, 78), surface, visit);
        _readCatalog = AddFeed(FeedKind.Compatibility, "Compatibility notices", "REFRESH NOTICES",
            new Rectangle(12, 189, 608, 174), surface, visit);
        var status = new LinkLabel { Text = elevated ? "Copy Rockstar PC status URL" : "View Rockstar PC service status",
            AutoSize = true, Location = new Point(12, 382), LinkColor = dark ? Palette.Acid : Palette.Ink };
        status.LinkClicked += (_, _) => visit("https://support.rockstargames.com/servicestatus");
        Controls.Add(status);
        _linkStatus = new Label { Text = elevated
            ? "Links copy their URL while VaultLoop runs as administrator."
            : "Links open in your browser.", AutoSize = true, Location = new Point(12, 417) };
        Controls.Add(_linkStatus);
    }

    private readonly Label _linkStatus;
    internal void ShowLinkStatus(string message) => _linkStatus.Text = message;

    internal void LoadSavedResults()
    {
        if (_loaded) return;
        _loaded = true;
        _readRelease(false);
        _readCatalog(false);
    }

    private Action<bool> AddFeed(FeedKind kind, string title, string command, Rectangle area,
        Color surface, Action<string> visit)
    {
        var feed = new VaultLoopFeed(kind);
        var installed = typeof(Program).Assembly.GetName().Version!;
        var label = new Label { Text = title, Font = Typography.WindowTitle, AutoSize = true,
            Location = new Point(area.Left, area.Top - 32) };
        var output = new RichTextBox { Name = kind + "Result", AccessibleName = title,
            Bounds = area, ReadOnly = true, TabStop = true, BackColor = surface, ForeColor = ForeColor,
            Font = Typography.Body, Text = "Reading saved information..." };
        output.LinkClicked += (_, e) => visit(e.LinkText);
        var button = BrutalistControls.CreateOutlinedButton(command,
            new Rectangle(area.Right - 172, area.Top - 40, 172, 32), Typography.StatusDetail, surface, ForeColor);
        button.Name = "Check" + kind;
        Controls.AddRange([label, button, output]);
        button.Click += (_, _) => Read(true);
        return Read;

        void Read(bool online)
        {
            if (!button.Enabled || IsDisposed) return;
            button.Enabled = false;
            if (online) button.Text = "CHECKING...";
            var token = _closing.Token;
            var worker = new BackgroundWorker();
            worker.DoWork += (_, e) => e.Result = feed.Read(online, installed, token);
            worker.RunWorkerCompleted += (_, e) =>
            {
                worker.Dispose();
                if (IsDisposed || Disposing || token.IsCancellationRequested) return;
                button.Enabled = true;
                button.Text = command;
                output.Text = e.Error is null ? (string)e.Result! : "Information unavailable.";
            };
            worker.RunWorkerAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing.IsCancellationRequested)
        {
            _closing.Cancel();
            _closing.Dispose();
        }
        base.Dispose(disposing);
    }
}
