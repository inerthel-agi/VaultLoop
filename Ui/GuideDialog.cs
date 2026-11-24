using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ReplayGlitchGTA;

/// <summary>
/// The workflow guide: the steps on the left, the one being followed on the right, and the
/// limits and warnings under it. Everything is on screen at once; nothing scrolls.
/// </summary>
internal sealed class GuideDialog : ThemedDialog
{
    /// <summary>The workflow, in order. Step numbers, layout, and progress all derive from it.</summary>
    private static readonly (string Title, string Description)[] Steps =
    [
        ("START THE ACTIVITY",
            "Enter the activity you intend to test. This replay workflow has not been validated by this audit."),
        ("ENABLE NO-SAVE",
            "Enable no-save and check ACTIVE. This confirms the Windows rule, not that cloud saves fail."),
        ("COMPLETE THE ACTIVITY",
            "Observe the save messages and activity result in GTA. VaultLoop cannot verify rewards or replay availability."),
        ("RETURN TO STORY MODE",
            "After a successful completion, leave GTA Online and wait until Story Mode has fully loaded."),
        ("DISABLE NO-SAVE",
            "Disable no-save and verify that the application clearly displays INACTIVE before continuing."),
        ("JOIN A PRIVATE SESSION",
            "If continuing the test, use Invite Only or Friends Only. Changing sessions does not guarantee a reset."),
        ("CHECK THE SAVE RESULT",
            "With no-save INACTIVE, check GTA's save result. An outfit change or Alt+F4 does not guarantee a save."),
        ("CHECK BEFORE REPLAY",
            "Check your progress and activity availability before continuing. Rejoining does not guarantee replay.")
    ];

    /// <summary>
    /// The notes under the current step. The first line is the title; the line breaks after
    /// it were set for a wider column and are reflowed.
    /// </summary>
    private static readonly (string Text, Color Accent)[] Notes =
    [
        ("REPLAY — VALIDATION LIMITS\n" +
         "This audit does not validate a replay procedure for Cayo Perico, Kortz Center,\n" +
         "or any other activity. A session cycle does not guarantee retained progress.\n" +
         "An ACTIVE rule is not proof that a cloud save failed.\n" +
         "If progress changes, its cause is not established by this tool.\n" +
         "Check the in-game result before attempting another activity.", Palette.Yellow),
        ("TIP — COOLDOWN\n" +
         "Follow the availability shown in GTA. No fixed delay guarantees replay or retained progress.",
            Palette.Blue),
        ("WARNING — USE AT YOUR OWN RISK\n" +
         "Online exploits may cause progress loss, transaction rollback, suspension, or account sanctions. " +
         "VaultLoop cannot guarantee an outcome.", Palette.AlertRed)
    ];

    private const int DialogWidth = 760;
    private const int Edge = 20;
    private const int ContentTop = 114;
    private const int ListWidth = 290;
    private const int RowHeight = 34;
    private const int RowPitch = 37;
    private const int DetailLeft = Edge + ListWidth + 12;
    private const int DetailWidth = DialogWidth - Edge - DetailLeft;
    private const int CardPadding = 14;

    /// <summary>The number of steps stored progress is validated against.</summary>
    internal static int StepCount => Steps.Length;

    private readonly GuideStepPanel[] _steps = new GuideStepPanel[Steps.Length];
    private readonly List<(Rectangle Bounds, Color Accent)> _noteCards = new();
    // Tall enough for a description that wraps onto a third line.
    private readonly Rectangle _detailCard = new(DetailLeft, ContentTop, DetailWidth, 122);
    private readonly Label _currentStepLabel;
    private readonly Label _detailKicker;
    private readonly Label _detailTitle;
    private readonly Label _detailBody;
    private readonly SoftButton _previous;
    private readonly SoftButton _next;
    private int _currentStep = 1;

    internal GuideDialog(bool darkMode) :
        base("HOW TO USE NO-SAVE", new Size(DialogWidth, 520), darkMode)
    {
        var textColor = ForeColor;
        var muted = Role(Palette.Muted);
        Controls.Add(ControlFactory.MakeLabel(
            "VaultLoop workflow", new Rectangle(Edge - 2, 50, 380, 34),
            Typography.WindowHeading, BackColor, textColor));
        Controls.Add(ControlFactory.MakeLabel(
            "Click a step to mark your current position.", new Rectangle(Edge, 84, 400, 20),
            Typography.MenuDetail, BackColor, muted));
        _currentStepLabel = ControlFactory.MakeLabel(
            "", new Rectangle(DialogWidth - Edge - 150, 58, 150, 28),
            Typography.MenuCaption, Role(Palette.Control), textColor,
            ContentAlignment.MiddleCenter);
        Controls.Add(_currentStepLabel);

        for (var index = 0; index < Steps.Length; index++)
        {
            _steps[index] = BuildStep(index);
            Controls.Add(_steps[index]);
        }

        var navigationTop = ContentTop + Steps.Length * RowPitch + 5;
        var navigationWidth = (ListWidth - 8) / 2;
        _previous = ControlFactory.MakeButton(
            "PREVIOUS", new Rectangle(Edge, navigationTop, navigationWidth, 32),
            Typography.MenuCaption, Role(Palette.Control), textColor);
        _previous.AccessibleName = "Mark the previous step as current";
        _previous.Click += (_, _) => SetCurrentStep(_currentStep - 1, persist: true);
        _next = ControlFactory.MakeButton(
            "NEXT STEP",
            new Rectangle(Edge + navigationWidth + 8, navigationTop, navigationWidth, 32),
            Typography.MenuCaption, Role(Palette.Control), textColor);
        _next.AccessibleName = "Mark the next step as current";
        _next.Click += (_, _) => SetCurrentStep(_currentStep + 1, persist: true);
        Controls.AddRange([_previous, _next]);

        var innerLeft = DetailLeft + CardPadding;
        var innerWidth = DetailWidth - 2 * CardPadding;
        _detailKicker = ControlFactory.MakeLabel(
            "", new Rectangle(innerLeft, ContentTop + 10, innerWidth, 18),
            Typography.MenuCaption, Color.Transparent, muted);
        _detailTitle = ControlFactory.MakeLabel(
            "", new Rectangle(innerLeft - 1, ContentTop + 28, innerWidth, 26),
            Typography.CardHeading, Color.Transparent, textColor);
        _detailBody = new Label
        {
            Bounds = new Rectangle(innerLeft, ContentTop + 56, innerWidth, 56),
            Font = Typography.MenuBody,
            BackColor = Color.Transparent,
            ForeColor = textColor
        };
        Controls.AddRange([_detailKicker, _detailTitle, _detailBody]);

        var top = _detailCard.Bottom + 8;
        foreach (var (text, accent) in Notes)
        {
            top = AddNote(text, accent, top, innerLeft, innerWidth) + 8;
        }

        var bottom = Math.Max(top - 8, navigationTop + 32);
        var closeButton = ControlFactory.MakeButton(
            "GOT IT", new Rectangle(DialogWidth - Edge - 110, bottom + 14, 110, 34),
            Typography.MenuCaption, Palette.Blue, Palette.Ink);
        closeButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(closeButton);
        AcceptButton = closeButton;
        CancelButton = closeButton;
        ClientSize = new Size(DialogWidth, closeButton.Bottom + 16);

        SetCurrentStep(GuideProgressSettings.Load(), persist: false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var surface = Role(Palette.Surface);
        MenuGlyphs.DrawCard(e.Graphics, _detailCard, surface, Role(Palette.Line));
        foreach (var (bounds, accent) in _noteCards)
        {
            MenuGlyphs.DrawCard(e.Graphics, bounds,
                Palette.Mix(surface, accent, 0.16), Palette.Mix(surface, accent, 0.45));
        }
    }

    /// <summary>Adds one note card under the previous one and returns its bottom edge.</summary>
    private int AddNote(string text, Color accent, int top, int left, int width)
    {
        var lines = text.Split('\n');
        var body = string.Join(" ", lines, 1, lines.Length - 1);
        var bodyHeight = TextRenderer.MeasureText(
            body, Typography.MenuDetail, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak).Height + 2;

        Controls.Add(ControlFactory.MakeLabel(
            DisplayText.Sentence(lines[0]), new Rectangle(left, top + 9, width, 18),
            Typography.MenuCaption, Color.Transparent, ForeColor));
        Controls.Add(new Label
        {
            Text = body,
            Bounds = new Rectangle(left, top + 28, width, bodyHeight),
            Font = Typography.MenuDetail,
            BackColor = Color.Transparent,
            ForeColor = ForeColor
        });

        var card = new Rectangle(DetailLeft, top, DetailWidth, 28 + bodyHeight + 10);
        _noteCards.Add((card, accent));
        return card.Bottom;
    }

    private GuideStepPanel BuildStep(int index)
    {
        var stepNumber = index + 1;
        var (title, description) = Steps[index];
        var panel = new GuideStepPanel
        {
            Bounds = new Rectangle(Edge, ContentTop + index * RowPitch, ListWidth, RowHeight),
            BackColor = BackColor,
            Cursor = Cursors.Hand,
            TabStop = true,
            AccessibleRole = AccessibleRole.RadioButton,
            AccessibleName = $"Step {stepNumber}: {title}",
            AccessibleDescription = description,
            AccessibleDefaultActionDescription = "Mark as current step"
        };
        panel.Controls.Add(new Label
        {
            Text = stepNumber.ToString("00"),
            Bounds = new Rectangle(10, 0, 30, RowHeight),
            BackColor = Color.Transparent,
            ForeColor = Role(Palette.Muted),
            Font = Typography.MenuCaption,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        });
        panel.Controls.Add(new Label
        {
            Text = DisplayText.Sentence(title),
            Bounds = new Rectangle(44, 0, ListWidth - 52, RowHeight),
            BackColor = Color.Transparent,
            ForeColor = ForeColor,
            Font = Typography.MenuBody,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        });

        void SelectStep(object? sender, EventArgs eventArgs)
        {
            panel.Focus();
            SetCurrentStep(stepNumber, persist: true);
        }
        foreach (Control child in panel.Controls)
        {
            child.Click += SelectStep;
        }
        panel.Click += SelectStep;
        panel.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode is Keys.Enter or Keys.Space)
            {
                SetCurrentStep(stepNumber, persist: true);
                eventArgs.Handled = true;
            }
            else if (eventArgs.KeyCode is Keys.Down or Keys.Right && index < Steps.Length - 1)
            {
                _steps[index + 1].Focus();
                SetCurrentStep(stepNumber + 1, persist: true);
                eventArgs.Handled = true;
            }
            else if (eventArgs.KeyCode is Keys.Up or Keys.Left && index > 0)
            {
                _steps[index - 1].Focus();
                SetCurrentStep(stepNumber - 1, persist: true);
                eventArgs.Handled = true;
            }
        };
        return panel;
    }

    private void SetCurrentStep(int step, bool persist)
    {
        step = Math.Max(1, Math.Min(Steps.Length, step));
        _currentStep = step;
        _currentStepLabel.Text =
            DisplayText.Sentence($"CURRENT STEP  {step} / {Steps.Length}");
        var surface = Role(Palette.Surface);
        for (var index = 0; index < _steps.Length; index++)
        {
            var isCurrent = index == step - 1;
            _steps[index].IsCurrent = isCurrent;
            _steps[index].SetStepColors(
                isCurrent ? Palette.Mix(surface, Palette.Blue, 0.24) : surface,
                Role(Palette.Text));
        }

        var (title, description) = Steps[step - 1];
        _detailKicker.Text = DisplayText.Sentence($"STEP {step:00}");
        _detailTitle.Text = DisplayText.Sentence(title);
        _detailBody.Text = description;
        _previous.Enabled = step > 1;
        _next.Enabled = step < Steps.Length;

        if (persist)
        {
            try
            {
                GuideProgressSettings.Save(step);
            }
            catch
            {
                // Progress tracking is optional and must never block the guide.
            }
        }
    }
}
