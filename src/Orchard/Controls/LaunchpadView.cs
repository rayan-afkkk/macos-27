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

/// <summary>Launchpad: every installed app in a full-screen, paged grid over the blurred desktop.</summary>
public sealed class LaunchpadView : Panel
{
    private readonly IShellPlatform _platform;
    private readonly TextBox _search;
    private readonly AppGrid _grid;

    public event Action? Dismissed;

    public LaunchpadView(IShellPlatform platform)
    {
        _platform = platform;
        Focusable = true;

        var searchHost = new GlassHost { Radius = 11, Width = 260, Height = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 54, 0, 0) };
        _search = Inputs.Bare(14, "Search");
        _search.Margin = new Thickness(34, 0, 10, 0);
        _search.TextChanged += (_, _) => _grid!.Filter = _search.Text ?? "";
        var glyph = new GlyphView(Glyph.Search) { Width = 14, Height = 14, Color = Palette.TextSecondary, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0) };
        searchHost.Child = new Panel { Children = { glyph, _search } };

        _grid = new AppGrid(platform) { Margin = new Thickness(0, 120, 0, 40) };
        _grid.Launched += item => { Dismissed?.Invoke(); _platform.Launch(item.Target, item.Arguments); };
        _grid.EmptyAreaClicked += () => Dismissed?.Invoke();

        Children.Add(new Backdrop());
        Children.Add(_grid);
        Children.Add(searchHost);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { Dismissed?.Invoke(); e.Handled = true; }
            else if (e.Key is Key.Right or Key.PageDown) { _grid.Page++; e.Handled = true; }
            else if (e.Key is Key.Left or Key.PageUp) { _grid.Page--; e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    public void FocusSearch() => _search.Focus();

    public string Filter { set => _search.Text = value; }

    /// <summary>Full-screen glass with a dark wash so white labels stay readable.</summary>
    private sealed class Backdrop : Control
    {
        public override void Render(DrawingContext ctx)
        {
            Glass.Draw(ctx, this, new RoundedRect(new Rect(Bounds.Size), 0), strong: false, shadow: false);
            ctx.DrawRectangle(Palette.Brush(Color.FromArgb(0x38, 0, 0, 0)), null, new Rect(Bounds.Size));
        }
    }

    private sealed class AppGrid(IShellPlatform platform) : Control
    {
        public const int Columns = 7, Rows = 5;
        public const double Icon = 84;

        private int _page;
        private string _filter = "";
        private int _hover = -1;

        public event Action<LaunchItem>? Launched;
        public event Action? EmptyAreaClicked;

        public string Filter { set { _filter = value; _page = 0; InvalidateVisual(); } }

        public int Page
        {
            get => _page;
            set { _page = Math.Clamp(value, 0, Math.Max(0, PageCount - 1)); InvalidateVisual(); }
        }

        private IReadOnlyList<LaunchItem> Items =>
            string.IsNullOrWhiteSpace(_filter) ? platform.InstalledApps : AppSearch.Rank(platform.InstalledApps, _filter, a => a.Name, 200).ToList();

        private int PageCount => (int)Math.Ceiling(Items.Count / (double)(Columns * Rows));

        private (double CellW, double CellH, double X0) Cells()
        {
            double cellW = Math.Min(170, Bounds.Width * 0.8 / Columns);
            double cellH = Math.Min(150, (Bounds.Height - 40) / Rows);
            return (cellW, cellH, (Bounds.Width - cellW * Columns) / 2);
        }

        private int IndexAt(Point p)
        {
            var (cw, ch, x0) = Cells();
            int col = (int)Math.Floor((p.X - x0) / cw), row = (int)Math.Floor(p.Y / ch);
            if (col < 0 || col >= Columns || row < 0 || row >= Rows) return -1;
            int i = _page * Columns * Rows + row * Columns + col;
            return i < Items.Count ? i : -1;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            int i = IndexAt(e.GetPosition(this));
            if (i != _hover) { _hover = i; InvalidateVisual(); }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            Page += e.Delta.Y < 0 || e.Delta.X < 0 ? 1 : -1;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            int i = IndexAt(e.GetPosition(this));
            if (i >= 0) Launched?.Invoke(Items[i]);
            else EmptyAreaClicked?.Invoke();
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size)); // whole area hit-testable
            var items = Items;
            var (cw, ch, x0) = Cells();
            int start = _page * Columns * Rows;
            for (int k = 0; k < Columns * Rows && start + k < items.Count; k++)
            {
                var item = items[start + k];
                double cx = x0 + (k % Columns + 0.5) * cw, top = k / Columns * ch;
                var r = new Rect(cx - Icon / 2, top + 8, Icon, Icon);
                if (start + k == _hover) r = r.Inflate(3);
                platform.IconFor(item).Draw(ctx, r);
                var name = item.Name.Length > 22 ? item.Name[..21] + "…" : item.Name;
                var label = Shapes.Text(name, 12.5, Colors.White, FontWeight.Medium);
                var at = new Point(cx - Math.Min(label.Width, cw - 8) / 2, top + Icon + 16);
                ctx.DrawText(Shapes.Text(name, 12.5, Color.FromArgb(0x70, 0, 0, 0), FontWeight.Medium), at + new Point(0, 1));
                ctx.DrawText(label, at);
            }

            // Page dots.
            int pages = PageCount;
            if (pages > 1)
            {
                double dx = Bounds.Width / 2 - (pages - 1) * 9;
                for (int p = 0; p < pages; p++)
                    ctx.DrawEllipse(Palette.Brush(Color.FromArgb(p == _page ? (byte)0xF0 : (byte)0x70, 255, 255, 255)), null,
                        new Point(dx + p * 18, Bounds.Height - 6), 4, 4);
            }
        }
    }
}
