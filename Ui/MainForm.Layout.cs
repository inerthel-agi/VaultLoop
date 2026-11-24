using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The window chrome: fixed geometry, the cards painted behind the controls, and the controls
/// themselves. Kept apart from the runtime behaviour so that changing what the window does and
/// changing what it looks like stay separate edits.
/// </summary>
/// <remarks>
/// Two columns under the title bar: the no-save control and its status on the left, every
/// setting on the right, and one strip for what the game and the elevation are doing.
/// Coordinates are absolute against a fixed <see cref="WindowSize"/> client area, because the
/// painted cards and the controls sitting on them have to line up exactly. Labels on a card
/// are transparent and buttons paint their own rounded fill, so the cards show through and
/// nothing has to repeat a card's color.
/// </remarks>
internal sealed partial class MainForm
{
    private static readonly Size WindowSize = new(780, 336);
    private static readonly Rectangle ControlCard = new(12, 52, 436, 216);
    private static readonly Rectangle StatusCard = new(28, 196, 404, 56);
    private static readonly Rectangle StatusOrb = new(40, 206, 36, 36);
    private static readonly Rectangle SettingsCard = new(460, 52, 308, 216);
    private static readonly Rectangle FooterCard = new(12, 280, 756, 44);
    private static readonly Rectangle GameStatusDot = new(28, 298, 8, 8);

    // Settings rows: one label on the left, one control ending at the right edge.
    private const int SettingsLeft = 476;
    private const int SettingsRight = 752;
    private const int SettingsTop = 94;
    private const int SettingsPitch = 34;

    /// <summary>
    /// Builds every control and returns the ones the window keeps a handle on. The form's own
    /// constructor owns those fields, so they are handed back rather than assigned here.
    /// </summary>
    private Chrome BuildLayout()
    {
        Text = "VaultLoop";
        ClientSize = WindowSize;
        BackColor = Palette.Canvas;
        ForeColor = Palette.Text;
        Font = Typography.MenuBody;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = Size;
        if (!_previewMode)
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        Controls.Add(BuildTitleBar());
        var chrome = new Chrome();
        BuildNoSaveSection(chrome);
        BuildSettings(chrome);
        BuildFooter(chrome);
        return chrome;
    }

    private Panel BuildTitleBar()
    {
        var titleBar = new Panel
        {
            Bounds = new Rectangle(0, 0, ClientSize.Width, 44),
            BackColor = Palette.Canvas
        };

        var logo = new PictureBox
        {
            Bounds = new Rectangle(12, 9, 26, 26),
            Image = _logoImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Palette.Ink,
            TabStop = false
        };
        // The title keeps its place; only its box is narrowed to what the word occupies, so the
        // version sits against it at any DPI instead of at a guessed offset.
        var titleWidth = TextRenderer.MeasureText("VaultLoop", Typography.MenuTitle).Width;
        var title = ControlFactory.MakeLabel(
            "VaultLoop", new Rectangle(46, 5, titleWidth, 34),
            Typography.MenuTitle, Palette.Canvas, Palette.Text);
        var version = ControlFactory.MakeLabel(
            VersionText, new Rectangle(46 + titleWidth + 4, 6, 90, 34),
            Typography.MenuDetail, Palette.Canvas, Palette.Muted);
        version.Name = "VersionLabel";
        version.AccessibleName = $"VaultLoop version {VersionText}";

        var guide = ControlFactory.MakeButton(
            "HOW TO USE", new Rectangle(572, 8, 108, 28),
            Typography.MenuCaption, Palette.Control, Palette.Text);
        guide.AccessibleName = "Open the no-save instruction guide";
        guide.Click += (_, _) =>
        {
            using var dialog = new GuideDialog(_darkMode);
            dialog.ShowDialog(this);
        };
        // Segoe MDL2 Assets: the minimize and close glyphs Windows draws on its own title bars.
        var minimize = ControlFactory.MakeButton(
            "", new Rectangle(688, 0, 46, 44),
            Typography.ChromeGlyph, Palette.Canvas, Palette.Text);
        minimize.Radius = 0;
        minimize.HoverBackColor = Palette.Blue;
        minimize.HoverForeColor = Palette.Ink;
        var close = ControlFactory.MakeButton(
            "", new Rectangle(734, 0, 46, 44),
            Typography.ChromeGlyph, Palette.Canvas, Palette.Text);
        close.Radius = 0;
        close.HoverBackColor = Palette.AlertRed;
        close.HoverForeColor = Palette.Surface;
        minimize.AccessibleName = "Minimize VaultLoop";
        close.AccessibleName = "Close VaultLoop";

        minimize.Click += (_, _) => MinimizeToTray();
        close.Click += (_, _) => RequestClose();
        WindowDrag.Attach(this, titleBar, logo, title, version);

        titleBar.Controls.AddRange([logo, title, version, guide, minimize, close]);
        return titleBar;
    }

    /// <summary>
    /// The displayed version, read from the assembly so the window cannot drift from the
    /// binary. The revision is dropped: the three released parts are what identifies a build.
    /// </summary>
    internal static string VersionText
    {
        get
        {
            var version = typeof(MainForm).Assembly.GetName().Version;
            return version is null
                ? ""
                : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private void BuildNoSaveSection(Chrome chrome)
    {
        Controls.Add(ControlFactory.MakeLabel(
            "No-save mode", new Rectangle(27, 60, 300, 26),
            Typography.CardHeading, Color.Transparent, Palette.Text));
        Controls.Add(ControlFactory.MakeLabel(
            "Toggle the Rockstar link without cutting the rest of your network.",
            new Rectangle(28, 86, 404, 20), Typography.MenuDetail, Color.Transparent,
            Palette.Muted));

        var toggle = new BooleanToggle
        {
            Location = new Point(28, 116),
            Size = new Size(404, 68),
            Enabled = ShouldEnableNoSaveToggle(
                _isAdministrator, applying: false, stateKnown: true,
                firewallState: FirewallRuleState.Inactive, hasVerifiedRunningGame: false),
            AccessibleName = "Toggle no-save mode",
            AccessibleDescription = "Active blocks the Rockstar link. Inactive restores it."
        };
        toggle.ToggleRequested += (_, _) => ToggleState();
        Controls.Add(toggle);
        chrome.Toggle = toggle;
        if (!_isAdministrator)
        {
            toggle.Visible = false;
            var adminRequired = ControlFactory.MakeButton(
                "ADMIN REQUIRED", toggle.Bounds,
                Typography.ToggleLabel, Palette.Control, Palette.Text);
            adminRequired.Radius = 12;
            adminRequired.Enabled = false;
            adminRequired.AccessibleName =
                "No-save unavailable until VaultLoop is launched as administrator";
            Controls.Add(adminRequired);
        }

        chrome.StateTitle = ControlFactory.MakeLabel(
            "", new Rectangle(88, 201, 336, 24),
            Typography.CardHeading, Color.Transparent, Palette.Text);
        chrome.StateDetail = ControlFactory.MakeLabel(
            "", new Rectangle(89, 225, 336, 20),
            Typography.MenuDetail, Color.Transparent, Palette.Muted);
        Controls.AddRange([chrome.StateTitle, chrome.StateDetail]);
    }

    /// <summary>
    /// Every setting in one place. They used to be spread over the title bar, the status
    /// card, the footer and the tray menu, and two of them could only be reached from the tray.
    /// </summary>
    private void BuildSettings(Chrome chrome)
    {
        Controls.Add(ControlFactory.MakeLabel(
            "Settings", new Rectangle(SettingsLeft - 1, 60, 200, 26),
            Typography.CardHeading, Color.Transparent, Palette.Text));

        AddSettingLabel(0, "Shortcut in GTA", 104);
        var shortcut = ControlFactory.MakeButton(
            ShortcutText, SettingBounds(0, 104, 28),
            Typography.MenuCaption, Palette.Control, Palette.Text);
        shortcut.Name = "ShortcutBadge";
        shortcut.AccessibleName = "Configure the GTA-only keyboard shortcut";
        shortcut.Click += (_, _) => ConfigureShortcut();
        chrome.ShortcutBadge = shortcut;

        AddSettingLabel(1, "In-game HUD", 40);
        var hud = MakeSwitch(1, _hudEnabled ? "HUD ON" : "HUD OFF", _hudEnabled);
        hud.Name = "HudVisibilityButton";
        hud.AccessibleName = _hudEnabled ? "Hide the no-save HUD" : "Show the no-save HUD";
        hud.Click += (_, _) => ToggleHudVisibility();
        chrome.HudVisibilityButton = hud;

        // Preview mode keeps the default, so the rendered window does not depend on the
        // registry of the machine that renders it.
        var startsWithWindows =
            !_previewMode && StartupRegistration.IsEnabled(Application.ExecutablePath);
        AddSettingLabel(2, "Start with Windows", 40);
        var startup = MakeSwitch(
            2, StartupSwitchText(startsWithWindows), startsWithWindows);
        startup.Name = "StartupSwitch";
        startup.AccessibleName = "Start VaultLoop with Windows";
        startup.Click += (_, _) => ToggleStartWithWindows();
        chrome.StartupSwitch = startup;

        AddSettingLabel(3, "Theme", 3 * 58 - 2);
        string[] themeNames = ["LIGHT", "DARK", "OLED"];
        chrome.ThemeButtons = new SoftButton[themeNames.Length];
        for (var index = 0; index < themeNames.Length; index++)
        {
            var theme = (AppTheme)index;
            var bounds = SettingBounds(3, 56, 28);
            bounds.X -= (themeNames.Length - 1 - index) * 58;
            var button = ControlFactory.MakeButton(
                themeNames[index], bounds, Typography.MenuCaption, Palette.Control, Palette.Text);
            button.Verbatim = index == (int)AppTheme.Oled;
            button.Name = "Theme" + theme;
            button.AccessibleName = $"Use the {theme} theme";
            button.Click += (_, _) => SetTheme(theme);
            chrome.ThemeButtons[index] = button;
        }

        AddSettingLabel(4, "Services & diagnostics", 72);
        var diagnostics = ControlFactory.MakeButton(
            "OPEN", SettingBounds(4, 72, 28),
            Typography.MenuCaption, Palette.Control, Palette.Text);
        diagnostics.AccessibleName = "Open services and diagnostics";
        diagnostics.Click += (_, _) => OpenDiagnostics();

        Controls.AddRange([shortcut, hud, startup, diagnostics]);
        Controls.AddRange(chrome.ThemeButtons);
    }

    /// <summary>
    /// The label of a settings row. It stops short of the row's control: a transparent label
    /// lying over a button paints the card across it and takes its clicks.
    /// </summary>
    private void AddSettingLabel(int row, string text, int controlWidth) =>
        Controls.Add(new Label
        {
            Text = text,
            Bounds = new Rectangle(
                SettingsLeft, SettingsTop + row * SettingsPitch,
                SettingsRight - SettingsLeft - controlWidth - 8, 28),
            Font = Typography.MenuBody,
            BackColor = Color.Transparent,
            ForeColor = Palette.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        });

    /// <summary>A control of the given size, right-aligned and centered in a settings row.</summary>
    private static Rectangle SettingBounds(int row, int width, int height) =>
        new(SettingsRight - width,
            SettingsTop + row * SettingsPitch + (28 - height) / 2, width, height);

    private static SoftButton MakeSwitch(int row, string text, bool on)
    {
        var button = ControlFactory.MakeButton(
            text, SettingBounds(row, 40, 22), Typography.MenuCaption, Palette.Track, Palette.Text);
        button.SwitchOn = on;
        return button;
    }

    private static string StartupSwitchText(bool enabled) =>
        enabled ? "START WITH WINDOWS ON" : "START WITH WINDOWS OFF";

    private void BuildFooter(Chrome chrome)
    {
        // The color of this status is carried by the dot painted beside it, so the text keeps
        // its contrast on a light footer as well as a dark one.
        chrome.GameStatusLabel = ControlFactory.MakeLabel(
            DisplayText.Sentence(_isAdministrator ? "WAITING FOR GTA" : "ADMIN REQUIRED"),
            new Rectangle(44, 285, 420, 34), Typography.MenuCaption, Color.Transparent,
            Palette.Text);
        Controls.Add(chrome.GameStatusLabel);

        var launchAsAdmin = ControlFactory.MakeButton(
            _isAdministrator ? "ADMIN READY" : "LAUNCH AS ADMIN",
            new Rectangle(600, 286, 160, 32), Typography.MenuCaption,
            _isAdministrator ? Palette.Control : Palette.Blue,
            _isAdministrator ? Palette.Text : Palette.Ink);
        launchAsAdmin.Name = "LaunchAsAdminButton";
        launchAsAdmin.Enabled = !_isAdministrator;
        launchAsAdmin.Cursor = _isAdministrator ? Cursors.Default : Cursors.Hand;
        launchAsAdmin.AccessibleName = _isAdministrator
            ? "VaultLoop is running as administrator"
            : "Launch VaultLoop as administrator";
        launchAsAdmin.Click += (_, _) => LaunchAsAdministrator();
        Controls.Add(launchAsAdmin);
    }

    private static Image LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ReplayGlitchLogo.png")
            ?? throw new InvalidOperationException("Embedded logo resource not found.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    /// <summary>
    /// The state color as an accent that still reads on the current theme: the lime and the
    /// yellow are fine on dark, and need darkening to show on a light surface.
    /// </summary>
    private Color ReadableAccent(Color accent) =>
        _darkMode ? accent : Palette.Mix(accent, Palette.Ink, 0.45);

    /// <summary>The state color on screen, on its way from the previous state's.</summary>
    private Color DisplayedStateColor =>
        Palette.Mix(_fromStateColor, _stateColor, _stateBlend.Value);

    /// <summary>The status card: a surface tinted by the state, with the state's cloud.</summary>
    private void DrawStatusCard(Graphics graphics, Color surface)
    {
        var stateColor = DisplayedStateColor;
        var fill = Palette.Mix(surface, stateColor, _darkMode ? 0.10 : 0.16);
        MenuGlyphs.DrawCard(graphics, StatusCard, fill,
            Palette.Mix(surface, stateColor, _darkMode ? 0.34 : 0.60));

        var accent = ReadableAccent(stateColor);
        using (var orbBrush = new SolidBrush(Palette.Mix(fill, accent, 0.18)))
        {
            graphics.FillEllipse(orbBrush, StatusOrb);
        }
        var inset = (StatusOrb.Width - 20F) / 2F;
        MenuGlyphs.DrawCloud(graphics, _stateMark,
            new PointF(StatusOrb.X + inset, StatusOrb.Y + inset), 1F, accent);
    }

    /// <summary>Marks the theme in use among the three theme buttons.</summary>
    private void ShowSelectedTheme()
    {
        for (var index = 0; index < _themeButtons.Length; index++)
        {
            var selected = (AppTheme)index == _theme;
            _themeButtons[index].BackColor =
                selected ? Palette.Blue : Palette.Themed(Palette.Control, _darkMode);
            _themeButtons[index].ForeColor =
                selected ? Palette.Ink : Palette.Themed(Palette.Text, _darkMode);
        }
    }

    /// <summary>The controls the window keeps addressing after construction.</summary>
    private sealed class Chrome
    {
        internal SoftButton ShortcutBadge { get; set; } = null!;
        internal SoftButton[] ThemeButtons { get; set; } = null!;
        internal BooleanToggle Toggle { get; set; } = null!;
        internal Label StateTitle { get; set; } = null!;
        internal Label StateDetail { get; set; } = null!;
        internal SoftButton HudVisibilityButton { get; set; } = null!;
        internal SoftButton StartupSwitch { get; set; } = null!;
        internal Label GameStatusLabel { get; set; } = null!;
    }
}
