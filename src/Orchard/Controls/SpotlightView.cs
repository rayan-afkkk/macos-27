using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Orchard.Platform;
using Orchard.Rendering;
using Orchard.Services;
using Orchard.Theme;

namespace Orchard.Controls;

public sealed record SearchResult(string Title, string Subtitle, IconArt? Icon, Glyph? Glyph, Action Run);

/// <summary>Spotlight: type to find apps, do quick maths, or hand the query to the web.</summary>
public sealed class SpotlightView : GlassHost
{
    public const double PanelWidth = 680, SearchHeight = 58, RowHeight = 44;

    private readonly IShellPlatform _platform;
    private readonly TextBox _box;
    private readonly ResultList _list;

    public event Action? Dismissed;

    public SpotlightView(IShellPlatform platform)
    {
        _platform = platform;
        Radius = 26;
        Width = PanelWidth;

        _box = Inputs.Bare(23, "Spotlight Search");
        _box.TextChanged += (_, _) => Refresh();
        _box.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        _list = new ResultList();
        _list.Picked += r => { Dismissed?.Invoke(); r.Run(); };

        var icon = new GlyphView(Glyph.Search) { Width = 22, Height = 22, Color = Palette.TextSecondary, Margin = new Thickness(20, 0, 12, 0) };
        var row = new DockPanel { Height = SearchHeight };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(_box);

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(_list);
        Child = stack;
        Refresh();
    }

    public string Query
    {
        get => _box.Text ?? "";
        set { _box.Text = value; _box.CaretIndex = value.Length; Refresh(); }
    }

    public void FocusInput()
    {
        _box.Focus();
        _box.SelectAll();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Dismissed?.Invoke(); break;
            case Key.Down: _list.Move(1); break;
            case Key.Up: _list.Move(-1); break;
            case Key.Enter when _list.Selected is { } r: Dismissed?.Invoke(); r.Run(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Refresh()
    {
        var q = Query.Trim();
        var results = new List<SearchResult>();
        if (q.Length > 0)
        {
            if (Calculator.LooksLikeMath(q) && Calculator.TryEvaluate(q) is { } v)
            {
                var text = Calculator.Format(v);
                results.Add(new SearchResult(text, $"{q} =", null, Glyph.Plus, () => _platform.Launch("calculator:")));
            }
            foreach (var app in AppSearch.Rank(_platform.InstalledApps, q, a => a.Name, 6))
                results.Add(new SearchResult(app.Name, "Application", _platform.IconFor(app), null, () => _platform.Launch(app.Target, app.Arguments)));
            results.Add(new SearchResult($"Search the web for “{q}”", "Web", null, Glyph.Search,
                () => _platform.Launch("https://www.bing.com/search?q=" + Uri.EscapeDataString(q))));
        }
        _list.Results = results;
        _list.Height = results.Count == 0 ? 0 : results.Count * RowHeight + 16;
        Height = SearchHeight + _list.Height;
    }

    private sealed class ResultList : Control
    {
        private IReadOnlyList<SearchResult> _results = [];
        private int _selected;

        public event Action<SearchResult>? Picked;
        public SearchResult? Selected => _selected < _results.Count ? _results[_selected] : null;

        public IReadOnlyList<SearchResult> Results
        {
            get => _results;
            set { _results = value; _selected = 0; InvalidateVisual(); }
        }

        public void Move(int d)
        {
            if (_results.Count == 0) return;
            _selected = (_selected + d + _results.Count) % _results.Count;
            InvalidateVisual();
        }

        private int RowAt(Point p) => (int)((p.Y - 8) / RowHeight);

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            int r = RowAt(e.GetPosition(this));
            if (r >= 0 && r < _results.Count && r != _selected) { _selected = r; InvalidateVisual(); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            int r = RowAt(e.GetPosition(this));
            if (r >= 0 && r < _results.Count) Picked?.Invoke(_results[r]);
        }

        public override void Render(DrawingContext ctx)
        {
            if (_results.Count == 0) return;
            ctx.DrawLine(new Pen(Palette.Brush(Palette.Separator), 1), new Point(0, 0.5), new Point(Bounds.Width, 0.5));
            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                var row = new Rect(8, 8 + i * RowHeight, Bounds.Width - 16, RowHeight);
                if (i == _selected) ctx.DrawRectangle(Palette.Brush(Palette.Accent), null, new RoundedRect(row, 10));
                var ink = i == _selected ? Colors.White : Palette.Text;
                var sub = i == _selected ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF) : Palette.TextSecondary;

                var iconRect = new Rect(row.X + 10, row.Y + 6, 32, 32);
                if (r.Icon != null) r.Icon.Draw(ctx, iconRect);
                else if (r.Glyph is { } g)
                {
                    ctx.DrawRectangle(Palette.Brush(i == _selected ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0)), null,
                        new RoundedRect(iconRect.Deflate(2), 8));
                    Glyphs.Draw(ctx, g, iconRect.Deflate(9), ink, 2.2);
                }
                var title = Shapes.Text(r.Title, 15, ink, FontWeight.Medium);
                ctx.DrawText(title, new Point(iconRect.Right + 12, row.Center.Y - title.Height / 2));
                var subtitle = Shapes.Text(r.Subtitle, 12, sub);
                ctx.DrawText(subtitle, new Point(row.Right - subtitle.Width - 14, row.Center.Y - subtitle.Height / 2));
            }
        }
    }
}

/// <summary>Draws a single glyph; used where a symbol sits inside normal layout.</summary>
public sealed class GlyphView(Glyph glyph) : Control
{
    public Color Color { get; set; } = Palette.Text;
    public double Weight { get; set; } = 2;

    public override void Render(DrawingContext ctx) => Glyphs.Draw(ctx, glyph, new Rect(Bounds.Size), Color, Weight);
}
