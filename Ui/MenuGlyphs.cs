using System.Drawing;
using System.Drawing.Drawing2D;

namespace ReplayGlitchGTA;

internal enum MenuGlyph
{
    Open,
    Hide,
    Diagnostics,
    Hud,
    Startup,
    Exit
}

/// <summary>What the status cloud carries: saves go through, saves are cut, or nobody knows.</summary>
internal enum CloudMark
{
    Check,
    Slash,
    Unknown
}

/// <summary>
/// The line icons of the tray menu and the HUD, drawn as vectors so they stay sharp at any
/// scale and the release build keeps shipping as one file with no image resources.
/// </summary>
internal static class MenuGlyphs
{
    /// <summary>The cloud for a status title as <c>MainForm.RenderStatus</c> names it.</summary>
    internal static CloudMark MarkFor(string status) => status switch
    {
        "ACTIVE" => CloudMark.Slash,
        "INACTIVE" => CloudMark.Check,
        _ => CloudMark.Unknown
    };

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2F;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180F, 90F);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270F, 90F);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
        path.CloseFigure();
        return path;
    }

    /// <summary>A rounded surface with a one-pixel edge.</summary>
    internal static void DrawCard(Graphics graphics, Rectangle bounds, Color fill, Color edge)
    {
        using var shape = RoundedRectangle(
            new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width - 1, bounds.Height - 1),
            8F);
        using var fillBrush = new SolidBrush(fill);
        using var edgePen = new Pen(edge);
        graphics.FillPath(fillBrush, shape);
        graphics.DrawPath(edgePen, shape);
    }

    /// <summary>
    /// A switch filling <paramref name="track"/>. <paramref name="position"/> runs from 0
    /// (off: knob left, off color) to 1 (on: knob right, blue), so it can be animated.
    /// </summary>
    internal static void DrawSwitch(
        Graphics graphics, RectangleF track, float position, Color offColor)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = RoundedRectangle(track, track.Height / 2F))
        using (var fill = new SolidBrush(Palette.Mix(offColor, Palette.Blue, position)))
        {
            graphics.FillPath(fill, shape);
        }
        const float Inset = 2F;
        var knob = track.Height - 2F * Inset;
        var travel = track.Width - knob - 2F * Inset;
        graphics.FillEllipse(Brushes.White,
            track.X + Inset + travel * position, track.Y + Inset, knob, knob);
    }

    /// <summary>Draws an action icon in a 16-unit box whose top-left corner is <paramref name="origin"/>.</summary>
    internal static void Draw(
        Graphics graphics, MenuGlyph glyph, PointF origin, float scale, Color color)
    {
        var state = Enter(graphics, origin, scale);
        using var pen = CreatePen(color, 1.4F);
        switch (glyph)
        {
            case MenuGlyph.Open:
                using (var window = RoundedRectangle(new RectangleF(2.5F, 3F, 11F, 10F), 2F))
                {
                    graphics.DrawPath(pen, window);
                }
                graphics.DrawLine(pen, 2.5F, 6.2F, 13.5F, 6.2F);
                break;
            case MenuGlyph.Hide:
                graphics.DrawLine(pen, 8F, 2.8F, 8F, 9.2F);
                graphics.DrawLines(pen, (PointF[])[new(5.4F, 6.7F), new(8F, 9.3F), new(10.6F, 6.7F)]);
                graphics.DrawLine(pen, 3F, 12.8F, 13F, 12.8F);
                break;
            case MenuGlyph.Diagnostics:
                graphics.DrawLines(pen,
                (PointF[])[
                    new(1.8F, 8.2F), new(4.4F, 8.2F), new(6.1F, 3.8F),
                    new(9.1F, 12.2F), new(10.7F, 8.2F), new(14.2F, 8.2F)
                ]);
                break;
            case MenuGlyph.Hud:
                using (var screen = RoundedRectangle(new RectangleF(2F, 3F, 12F, 8.2F), 1.6F))
                {
                    graphics.DrawPath(pen, screen);
                }
                graphics.DrawLine(pen, 6F, 13.6F, 10F, 13.6F);
                graphics.DrawLine(pen, 9.4F, 5.6F, 11.8F, 5.6F);
                break;
            case MenuGlyph.Startup:
                graphics.DrawLine(pen, 8F, 2.4F, 8F, 7.6F);
                graphics.DrawArc(pen, 3.2F, 3.2F, 9.6F, 9.6F, 313.3F, 273.4F);
                break;
            case MenuGlyph.Exit:
                graphics.DrawLines(pen,
                (PointF[])[
                    new(6.6F, 3F), new(3.6F, 3F), new(3F, 3.6F),
                    new(3F, 12.4F), new(3.6F, 13F), new(6.6F, 13F)
                ]);
                graphics.DrawLines(pen, (PointF[])[new(10F, 5.4F), new(12.6F, 8F), new(10F, 10.6F)]);
                graphics.DrawLine(pen, 12.6F, 8F, 6.6F, 8F);
                break;
        }
        graphics.Restore(state);
    }

    /// <summary>Draws the status cloud in a 20-unit box whose top-left corner is <paramref name="origin"/>.</summary>
    internal static void DrawCloud(
        Graphics graphics, CloudMark mark, PointF origin, float scale, Color color)
    {
        var state = Enter(graphics, origin, scale);
        using var pen = CreatePen(color, 1.6F);
        if (mark == CloudMark.Unknown)
        {
            pen.DashCap = DashCap.Round;
            pen.DashPattern = [1.4F, 1.5F];
        }

        // Three arcs — right lobe, crown, left lobe — closed by the flat base.
        using var cloud = new GraphicsPath();
        cloud.AddArc(11.5F, 9F, 6F, 6F, 90F, -168.5F);
        cloud.AddArc(6.1F, 4.71F, 9F, 9F, -1.9F, -157.1F);
        cloud.AddArc(3.03F, 7.58F, 7.5F, 7.5F, 264.25F, -162.3F);
        cloud.CloseFigure();
        graphics.DrawPath(pen, cloud);

        if (mark == CloudMark.Check)
        {
            graphics.DrawLines(pen, (PointF[])[new(8.3F, 11.2F), new(9.8F, 12.7F), new(12.4F, 9.9F)]);
        }
        else if (mark == CloudMark.Slash)
        {
            pen.Width = 1.9F;
            graphics.DrawLine(pen, 4F, 3.6F, 16.2F, 16.6F);
        }
        graphics.Restore(state);
    }

    private static GraphicsState Enter(Graphics graphics, PointF origin, float scale)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(origin.X, origin.Y);
        graphics.ScaleTransform(scale, scale);
        return state;
    }

    private static Pen CreatePen(Color color, float width) =>
        new(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
}
