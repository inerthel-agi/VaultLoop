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
        BackColor = Palette.Themed(Palette.Canvas, dark);
        ForeColor = Palette.Themed(Palette.Text, dark);
        _control = Palette.Themed(Palette.Control, dark);
        var surface = Palette.Themed(Palette.Surface, dark);
        _dark = dark;
        _muted = Palette.Themed(Palette.Muted, dark);
        var line = Palette.Themed(Palette.Line, dark);
        _readRelease = AddFeed(FeedKind.Release, "VaultLoop updates", "CHECK FOR UPDATES",
            new Rectangle(12, 34, 718, 78), surface, visit);
        _readCatalog = AddFeed(FeedKind.Compatibility, "Compatibility notices", "REFRESH NOTICES",
            new Rectangle(12, 150, 718, 116), surface, visit);
        Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            foreach (var card in _cards) MenuGlyphs.DrawCard(e.Graphics, card, surface, line);
        };
        var status = ControlFactory.MakeButton(
            elevated ? "Copy Rockstar PC status URL" : "View Rockstar PC service status",
            new Rectangle(12, 278, 230, 32), Typography.MenuCaption, _control, ForeColor);
        status.Verbatim = true;
        status.Click += (_, _) => visit("https://support.rockstargames.com/servicestatus");
        Controls.Add(status);
        _linkStatus = new Label { Text = elevated
            ? "Links copy their URL while VaultLoop runs as administrator."
            : "Links open in your browser.", AutoSize = true, Location = new Point(254, 286),
            Font = Typography.MenuDetail, ForeColor = _muted };
        Controls.Add(_linkStatus);
    }

    private readonly Label _linkStatus;
    private readonly Color _control;
    private readonly Color _muted;
    private readonly bool _dark;
    private readonly System.Collections.Generic.List<Rectangle> _cards = new();
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
        var label = new Label { Text = title, Font = Typography.MenuCaption, AutoSize = true,
            Location = new Point(area.Left, area.Top - 24), ForeColor = _muted };
        // The text sits inset in a card painted by the page.
        _cards.Add(area);
        var output = ControlFactory.WithThemedScrollBars(new RichTextBox
        {
            Name = kind + "Result", AccessibleName = title,
            Bounds = Rectangle.Inflate(area, -10, -8), ReadOnly = true, TabStop = true,
            BorderStyle = BorderStyle.None, BackColor = surface, ForeColor = ForeColor,
            Font = Typography.MenuBody, Text = "Reading saved information..."
        }, _dark);
        output.LinkClicked += (_, e) => visit(e.LinkText);
        var button = ControlFactory.MakeButton(command,
            new Rectangle(area.Right - 172, area.Top - 32, 172, 28), Typography.MenuCaption, _control, ForeColor);
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
