using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Orchard.Model;
using Orchard.Rendering;
using Orchard.Services;
using Orchard.Theme;

namespace Orchard.Controls;

/// <summary>
/// The dock: a glass shelf of icons along the bottom edge with pointer magnification, launch bounce,
/// running indicators, hover labels and a slide-away auto-hide. Entirely custom drawn so every frame of the
/// magnification wave is one cheap render pass.
/// </summary>
public sealed class DockControl : Control
{
    public double IconSize { get; set; } = 50;
    public double MaxScale { get; set; } = 1.75;
    public bool Magnify { get; set; } = true;

    public const double Padding = 6, Gap = 5, BottomMargin = 5, SeparatorWidth = 1;

    /// <summary>0 = fully shown, 1 = slid off the bottom of the screen.</summary>
    public double HiddenTarget { get; set; }
    public double Hidden { get; private set; }

    public event Action<AppEntry>? ItemClicked;
    public event Action<AppEntry, Rect>? ItemContextRequested;
    /// <summary>Raised when the painted shapes change, so the host window can match its hit-test / blur region.</summary>
    public event Action<IReadOnlyList<RoundedRect>>? ShapeChanged;

    private IReadOnlyList<AppEntry> _items = [];
    private double[] _scale = [];
    private readonly Dictionary<AppEntry, DateTime> _bounces = [];
    private double? _pointerX;
    private AppEntry? _hover, _pressed;
    private DateTime _hoverSince;
    private DateTime _lastFrame = DateTime.UtcNow;
    private bool _animating;
    private List<RoundedRect> _lastShape = [];

    /// <summary>Freezes animation time for screenshots.</summary>
    public DateTime? FixedNow { get; set; }
    private DateTime Now => FixedNow ?? DateTime.UtcNow;

    public IReadOnlyList<AppEntry> Items
    {
        get => _items;
        set
        {
            var old = _items.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => _scale.ElementAtOrDefault(x.i));
            _items = value;
            _scale = value.Select(a => old.TryGetValue(a, out var s) && s > 0 ? s : 1).ToArray();
            Kick();
        }
    }

    public bool IsEngaged => _pointerX != null || _pressed != null;

    public void Bounce(AppEntry app)
    {
        _bounces[app] = Now;
        Kick();
    }

    /// <summary>Puts the pointer at an x position (control coordinates) and snaps the wave there — for screenshots.</summary>
    public void SimulatePointer(double? x, AppEntry? hover = null)
    {
        _pointerX = x;
        _hover = hover;
        _hoverSince = Now - TimeSpan.FromSeconds(1);
        var centers = RestingCenters();
        for (int i = 0; i < _scale.Length; i++) _scale[i] = TargetScale(i, centers);
        InvalidateVisual();
    }

    public void SnapHidden() { Hidden = HiddenTarget; InvalidateVisual(); }

    // ---- geometry ------------------------------------------------------------------------------

    private HashSet<int> Separators()
    {
        var set = new HashSet<int>();
        int firstUnpinned = _items.ToList().FindIndex(a => !a.IsPinned && a.Kind == AppKind.Normal);
        if (firstUnpinned > 0) set.Add(firstUnpinned);
        int trash = _items.ToList().FindIndex(a => a.Kind == AppKind.Trash);
        if (trash > 0 && !set.Contains(trash) && (firstUnpinned < 0 || firstUnpinned < trash)) set.Add(trash);
        return set;
    }

    private (double[] Lefts, double Width) RestingRow() => DockMath.Row(_items.Select(_ => 1.0).ToList(), IconSize, Gap, Separators(), SeparatorWidth);

    private double ShelfHeight => IconSize + Padding * 2;
    private double SlideDistance => ShelfHeight + BottomMargin + 4;

    /// <summary>Resting icon centres in control coordinates (magnification is measured against these).</summary>
    private double[] RestingCenters()
    {
        var (lefts, width) = RestingRow();
        double x0 = (Bounds.Width - width) / 2;
        return lefts.Select(l => x0 + l + IconSize / 2).ToArray();
    }

    private double TargetScale(int i) => TargetScale(i, RestingCenters());

    private double TargetScale(int i, double[] restingCenters)
    {
        if (!Magnify || _pointerX is not { } px) return 1;
        return DockMath.Scale(px - restingCenters[i], IconSize * 3.2, MaxScale);
    }

    private record struct Layout(Rect Shelf, Rect[] Icons, double SlideY);

    private Layout ComputeLayout()
    {
        var (lefts, width) = DockMath.Row(_scale, IconSize, Gap, Separators(), SeparatorWidth);
        double slide = Hidden * SlideDistance;
        double shelfW = width + Padding * 2;
        var shelf = new Rect((Bounds.Width - shelfW) / 2, Bounds.Height - BottomMargin - ShelfHeight + slide, shelfW, ShelfHeight);
        var icons = new Rect[_items.Count];
        double baseline = shelf.Bottom - Padding;
        for (int i = 0; i < _items.Count; i++)
        {
            double size = IconSize * _scale[i];
            double lift = _bounces.TryGetValue(_items[i], out var started) ? DockMath.Bounce((Now - started).TotalSeconds) * IconSize : 0;
            icons[i] = new Rect(shelf.X + Padding + lefts[i], baseline - size - lift, size, size);
        }
        return new Layout(shelf, icons, slide);
    }

    /// <summary>Where an app's icon currently sits, in this control's coordinates (genie target).</summary>
    public Rect? IconRect(AppEntry app)
    {
        int i = _items.ToList().IndexOf(app);
        return i < 0 ? null : ComputeLayout().Icons[i];
    }

    public Rect ShelfRect => ComputeLayout().Shelf;

    /// <summary>Width of the shelf with no magnification — the dock's footprint at rest.</summary>
    public double RestingShelfWidth => RestingRow().Width + Padding * 2;
    public double ShelfHeightAtRest => ShelfHeight;

    /// <summary>True while any icon is still scaled up (including while the wave settles after the pointer leaves).</summary>
    public bool IsMagnified => _scale.Any(s => s > 1.001);

    /// <summary>Height the control needs: the tallest magnified icon plus room for the hover label.</summary>
    public double RequiredHeight => BottomMargin + Padding + IconSize * Math.Max(1, MaxScale) + IconSize * 0.55 + 44;

    // ---- input ---------------------------------------------------------------------------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var layout = ComputeLayout();
        // Magnify only while the pointer is over the shelf or the icons rising above it.
        bool over = layout.Shelf.Inflate(new Thickness(0, IconSize * (MaxScale - 1) + 6, 0, BottomMargin)).Contains(p);
        _pointerX = over && Hidden < 0.5 ? p.X : null;
        var hit = over ? HitTest(layout, p) : null;
        if (hit != _hover) { _hover = hit; _hoverSince = Now; }
        Kick();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _pointerX = null;
        _hover = null;
        _pressed = null;
        Kick();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var hit = HitTest(ComputeLayout(), e.GetPosition(this));
        if (hit == null) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            ItemContextRequested?.Invoke(hit, IconRect(hit) ?? default);
            e.Handled = true;
            return;
        }
        _pressed = hit;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var hit = HitTest(ComputeLayout(), e.GetPosition(this));
        if (_pressed != null && hit == _pressed)
        {
            if (!hit.IsRunning && hit.Kind == AppKind.Normal) Bounce(hit);
            ItemClicked?.Invoke(hit);
        }
        _pressed = null;
        InvalidateVisual();
    }

    private AppEntry? HitTest(Layout layout, Point p)
    {
        for (int i = 0; i < layout.Icons.Length; i++)
        {
            var r = layout.Icons[i];
            // Include the gap so there is no dead zone between icons.
            if (p.X >= r.X - Gap / 2 && p.X < r.Right + Gap / 2 && p.Y >= r.Y - 4 && p.Y <= layout.Shelf.Bottom + BottomMargin)
                return _items[i];
        }
        return null;
    }

    // ---- animation -----------------------------------------------------------------------------

    private void Kick()
    {
        if (_animating || TopLevel.GetTopLevel(this) is not { } top) { InvalidateVisual(); return; }
        _animating = true;
        _lastFrame = DateTime.UtcNow;
        top.RequestAnimationFrame(Tick);
    }

    private void Tick(TimeSpan _)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastFrame).TotalSeconds, 0, 0.05);
        _lastFrame = now;
        bool moving = Advance(dt);
        if (moving && TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Tick);
        else _animating = false;
    }

    /// <summary>Steps the magnification, slide and bounce animations by <paramref name="dt"/> seconds. Returns true while anything is still moving.</summary>
    public bool Advance(double dt)
    {
        bool moving = false;
        var centers = RestingCenters();
        for (int i = 0; i < _scale.Length; i++)
        {
            double t = TargetScale(i, centers);
            _scale[i] = DockMath.Approach(_scale[i], t, dt, 22);
            moving |= _scale[i] != t;
        }
        double hiddenBefore = Hidden;
        Hidden = DockMath.Approach(Hidden, HiddenTarget, dt, 14);
        moving |= Hidden != HiddenTarget || hiddenBefore != Hidden;

        foreach (var (app, started) in _bounces.ToList())
            if ((Now - started).TotalSeconds > 0.9) _bounces.Remove(app);
        moving |= _bounces.Count > 0;

        // Keep ticking briefly while hovering so the label's fade-in completes.
        moving |= _hover != null && (Now - _hoverSince).TotalSeconds < 0.4;

        InvalidateVisual();
        return moving;
    }

    /// <summary>Moves the (simulated) pointer without snapping, so <see cref="Advance"/> animates the wave — used by the promo renderer.</summary>
    public void SetPointer(Point? p)
    {
        if (p is not { } pt) { _pointerX = null; _hover = null; return; }
        var layout = ComputeLayout();
        bool over = layout.Shelf.Inflate(new Thickness(0, IconSize * (MaxScale - 1) + 6, 0, BottomMargin)).Contains(pt);
        _pointerX = over && Hidden < 0.5 ? pt.X : null;
        var hit = over ? HitTest(layout, pt) : null;
        if (hit != _hover) { _hover = hit; _hoverSince = Now; }
    }

    // ---- painting ------------------------------------------------------------------------------

    public override void Render(DrawingContext ctx)
    {
        if (_items.Count == 0) return;
        var layout = ComputeLayout();
        var shape = new List<RoundedRect>();

        var shelf = new RoundedRect(layout.Shelf, Math.Min(22, ShelfHeight * 0.36));
        Glass.Draw(ctx, this, shelf, chrome: true);
        shape.Add(shelf);

        // Translucent light glass over a dark wallpaper reads dark: switch the dots and dividers to white, like macOS.
        bool darkBehind = Palette.Dark || Glass.PerceivedLuminance(this, layout.Shelf, Glass.ChromeTint) is < 0.5;
        var separatorColor = darkBehind ? Color.FromArgb(0x40, 255, 255, 255) : Palette.Separator;
        var dotColor = darkBehind ? Color.FromArgb(0xE0, 255, 255, 255) : Color.FromArgb(0xC0, 0x1D, 0x1D, 0x1F);

        var separators = Separators();
        for (int i = 0; i < _items.Count; i++)
        {
            var app = _items[i];
            var r = layout.Icons[i];

            if (separators.Contains(i))
            {
                double x = r.X - Gap - SeparatorWidth / 2 - Gap / 2;
                ctx.DrawLine(new Pen(Palette.Brush(separatorColor), SeparatorWidth),
                    new Point(x, layout.Shelf.Y + Padding + 4), new Point(x, layout.Shelf.Bottom - Padding - 4));
            }

            DrawIcon(ctx, app.Icon, r);
            if (app == _pressed)
                ctx.DrawGeometry(Palette.Brush(Color.FromArgb(0x50, 0, 0, 0)), null, Shapes.Squircle(r.Deflate(r.Width * 0.06)));

            if (app.IsRunning)
            {
                var dot = new Point(r.Center.X, layout.Shelf.Bottom - 3.2);
                ctx.DrawEllipse(Palette.Brush(dotColor), null, dot, 2, 2);
            }

        }

        if (_hover != null && _pressed == null && Hidden < 0.1)
        {
            int i = _items.ToList().IndexOf(_hover);
            if (i >= 0)
            {
                double fade = Math.Clamp((Now - _hoverSince).TotalSeconds / 0.15, 0, 1);
                DrawLabel(ctx, _hover.Name, layout.Icons[i], fade);
            }
        }

        // The host window clips itself to these shapes. They must not change every animation frame — the window
        // region is applied a frame late, which would shave the tops off growing icons. So: while the dock is in
        // use the whole control is live; at rest only the shelf is; while sliding, the shelf plus some slack.
        shape.Clear();
        bool engaged = _pointerX != null || _hover != null || _pressed != null || _bounces.Count > 0 || IsMagnified;
        if (engaged && Hidden < 0.5)
            shape.Add(new RoundedRect(new Rect(Bounds.Size), 0));
        else if (Hidden != HiddenTarget)
            shape.Add(new RoundedRect(new Rect(layout.Shelf.X, layout.Shelf.Y - 14, layout.Shelf.Width, Bounds.Height - layout.Shelf.Y + 14), 0));
        else
            shape.Add(shelf);

        if (!SameShape(shape, _lastShape))
        {
            _lastShape = shape;
            ShapeChanged?.Invoke(shape);
        }
    }

    // ---- icon cache ----------------------------------------------------------------------------
    // Vector icons are many gradients and paths; drawing ~20 of them every animation frame is what made the
    // magnification wave stutter. Each icon is rendered once at its largest on-screen size and then just scaled.

    private readonly Dictionary<IconArt, RenderTargetBitmap> _iconCache = new(ReferenceEqualityComparer.Instance);
    private int _iconCachePx;
    private DateTime _iconCacheDay;

    private void DrawIcon(DrawingContext ctx, IconArt icon, Rect r)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int px = (int)Math.Ceiling(IconSize * Math.Max(1, MaxScale) * scaling);
        if (px != _iconCachePx || _iconCacheDay != DateTime.Today)
        {
            // Size or DPI changed, or a new day (the Calendar icon shows the date): start over.
            foreach (var old in _iconCache.Values) old.Dispose();
            _iconCache.Clear();
            _iconCachePx = px;
            _iconCacheDay = DateTime.Today;
        }
        if (!_iconCache.TryGetValue(icon, out var bitmap))
        {
            try
            {
                bitmap = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96, 96));
                using (var bctx = bitmap.CreateDrawingContext()) icon.Draw(bctx, new Rect(0, 0, px, px));
                _iconCache[icon] = bitmap;
            }
            catch (Exception)
            {
                icon.Draw(ctx, r); // no offscreen rendering available: draw directly
                return;
            }
        }
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            ctx.DrawImage(bitmap, new Rect(0, 0, px, px), r);
    }

    private RoundedRect DrawLabel(DrawingContext ctx, string text, Rect icon, double opacity)
    {
        var ft = Shapes.Text(text, 13, Palette.Text, FontWeight.Medium);
        var rect = new Rect(icon.Center.X - ft.Width / 2 - 11, icon.Y - ft.Height - 22, ft.Width + 22, ft.Height + 10);
        rect = rect.WithX(Math.Clamp(rect.X, 4, Math.Max(4, Bounds.Width - rect.Width - 4)));
        var rr = new RoundedRect(rect, rect.Height / 2);
        using (ctx.PushOpacity(opacity))
        {
            Glass.Draw(ctx, this, rr, strong: true, shadow: false);
            ctx.DrawText(ft, new Point(rect.X + 11, rect.Y + 5));
        }
        return rr;
    }

    private static bool SameShape(List<RoundedRect> a, List<RoundedRect> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i].Rect; var y = b[i].Rect;
            if (Math.Abs(x.X - y.X) > 0.5 || Math.Abs(x.Y - y.Y) > 0.5 || Math.Abs(x.Width - y.Width) > 0.5 || Math.Abs(x.Height - y.Height) > 0.5) return false;
        }
        return true;
    }
}
