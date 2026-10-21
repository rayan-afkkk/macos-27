using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Orchard.Rendering;
using Orchard.Theme;

namespace Orchard.Controls;

public sealed record MenuItem(string Title, string? Shortcut = null, Action? Action = null, bool Enabled = true, bool Checked = false)
{
    public static readonly MenuItem Separator = new("-");
    public bool IsSeparator => Title == "-";
}

/// <summary>A drop-down menu: glass panel, rounded accent highlight that follows the pointer, right-aligned shortcuts.</summary>
public sealed class MenuPanel : Control
{
    public const double RowHeight = 22, SeparatorHeight = 9, Inset = 5, MinPanelWidth = 200;

    public IReadOnlyList<MenuItem> Items { get; }
    public event Action? Dismissed;

    private int _hover = -1;

    public MenuPanel(IReadOnlyList<MenuItem> items)
    {
        Items = items;
        Focusable = true;
        var (w, h) = Measure(items);
        Width = w;
        Height = h;
    }

    /// <summary>Set by screenshots to show the selection highlight on a given row.</summary>
    public int HighlightIndex { get => _hover; set { _hover = value; InvalidateVisual(); } }

    public static (double Width, double Height) Measure(IReadOnlyList<MenuItem> items)
    {
        double w = MinPanelWidth, h = Inset * 2;
        foreach (var item in items)
        {
            if (item.IsSeparator) { h += SeparatorHeight; continue; }
            h += RowHeight;
            double tw = Shapes.Text(item.Title, 13, Colors.Black).Width + 44;
            if (item.Shortcut != null) tw += Shapes.Text(item.Shortcut, 13, Colors.Black).Width + 28;
            w = Math.Max(w, tw);
        }
        return (Math.Ceiling(w), Math.Ceiling(h));
    }

    private IEnumerable<(int Index, Rect Row)> Rows()
    {
        double y = Inset;
        for (int i = 0; i < Items.Count; i++)
        {
            double h = Items[i].IsSeparator ? SeparatorHeight : RowHeight;
            yield return (i, new Rect(Inset, y, Bounds.Width - Inset * 2, h));
            y += h;
        }
    }

    private int RowAt(Point p) => Rows().FirstOrDefault(r => r.Row.Contains(p) && !Items[r.Index].IsSeparator && Items[r.Index].Enabled, (-1, default)).Index;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        int row = RowAt(e.GetPosition(this));
        if (row != _hover) { _hover = row; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = -1; InvalidateVisual(); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        int row = RowAt(e.GetPosition(this));
        if (row >= 0) Invoke(row);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Dismissed?.Invoke(); break;
            case Key.Down: Step(1); break;
            case Key.Up: Step(-1); break;
            case Key.Enter when _hover >= 0: Invoke(_hover); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Step(int dir)
    {
        for (int n = 0, i = _hover; n < Items.Count; n++)
        {
            i = (i + dir + Items.Count) % Items.Count;
            if (!Items[i].IsSeparator && Items[i].Enabled) { _hover = i; InvalidateVisual(); return; }
        }
    }

    private void Invoke(int row)
    {
        var item = Items[row];
        Dismissed?.Invoke();
        item.Action?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        var panel = new RoundedRect(new Rect(Bounds.Size), 10);
        Glass.Draw(ctx, this, panel, strong: true, shadow: false);

        foreach (var (i, row) in Rows())
        {
            var item = Items[i];
            if (item.IsSeparator)
            {
                double y = Math.Round(row.Center.Y) + 0.5;
                ctx.DrawLine(new Pen(Palette.Brush(Palette.Separator), 1), new Point(row.X + 9, y), new Point(row.Right - 9, y));
                continue;
            }
            bool hot = i == _hover;
            if (hot) ctx.DrawRectangle(Palette.Brush(Palette.Accent), null, new RoundedRect(row, 5));
            var color = !item.Enabled ? Palette.TextSecondary : hot ? Colors.White : Palette.Text;
            if (item.Checked)
                ctx.DrawText(Shapes.Text("✓", 12, color, FontWeight.SemiBold), new Point(row.X + 7, row.Y + 3));
            var title = Shapes.Text(item.Title, 13, color);
            ctx.DrawText(title, new Point(row.X + 22, row.Y + (row.Height - title.Height) / 2));
            if (item.Shortcut != null)
            {
                var sc = Shapes.Text(item.Shortcut, 13, hot ? Colors.White : Palette.TextSecondary);
                ctx.DrawText(sc, new Point(row.Right - sc.Width - 10, row.Y + (row.Height - sc.Height) / 2));
            }
        }
    }
}
