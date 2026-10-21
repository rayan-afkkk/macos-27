using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Orchard.Rendering;
using Orchard.Theme;

namespace Orchard.Controls;

/// <summary>A container whose background is the glass material.</summary>
public class GlassHost : Decorator
{
    public double Radius { get; set; } = 16;
    public bool Strong { get; set; } = true;
    public bool Shadow { get; set; }

    public override void Render(DrawingContext ctx) =>
        Glass.Draw(ctx, this, new RoundedRect(new Rect(Bounds.Size), Radius), Strong, Shadow);
}

public static class Inputs
{
    /// <summary>A borderless, transparent TextBox that sits directly on glass (Spotlight, Launchpad search).</summary>
    public static TextBox Bare(double fontSize, string watermark)
    {
        var tb = new TextBox
        {
            FontSize = fontSize,
            FontFamily = Palette.Font,
            Watermark = watermark,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Palette.Brush(Palette.Text),
            CaretBrush = Palette.Brush(Palette.Accent),
            Padding = new Thickness(0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        foreach (var state in new[] { ":focus", ":pointerover" })
        {
            tb.Styles.Add(new Style(x => x.OfType<TextBox>().Class(state).Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, Brushes.Transparent),
                    new Setter(Border.BorderThicknessProperty, new Thickness(0)),
                },
            });
        }
        tb.Styles.Add(new Style(x => x.OfType<TextBox>().Template().OfType<TextBlock>().Name("PART_Watermark"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Palette.Brush(Palette.TextSecondary)) },
        });
        return tb;
    }
}
