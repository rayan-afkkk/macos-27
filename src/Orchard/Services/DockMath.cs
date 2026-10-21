namespace Orchard.Services;

/// <summary>Pure layout math for the dock, kept separate so it can be unit tested.</summary>
public static class DockMath
{
    /// <summary>
    /// Magnification for an icon whose resting centre is <paramref name="distance"/> away from the pointer.
    /// A raised-cosine bell: full <paramref name="maxScale"/> under the pointer, easing smoothly to 1 at
    /// <paramref name="range"/>. This is what gives the "wave" its soft shoulders.
    /// </summary>
    public static double Scale(double distance, double range, double maxScale)
    {
        if (range <= 0 || maxScale <= 1) return 1;
        double d = Math.Abs(distance) / range;
        if (d >= 1) return 1;
        return 1 + (maxScale - 1) * (Math.Cos(d * Math.PI) + 1) / 2;
    }

    /// <summary>
    /// Lays icons out left to right. Returns each icon's left edge (relative to the row's left edge) and the row width.
    /// Separators are inserted before the indices in <paramref name="separatorsBefore"/>.
    /// </summary>
    public static (double[] Lefts, double Width) Row(IReadOnlyList<double> scales, double baseSize, double gap,
        IReadOnlySet<int>? separatorsBefore = null, double separatorWidth = 0)
    {
        var lefts = new double[scales.Count];
        double x = 0;
        for (int i = 0; i < scales.Count; i++)
        {
            if (i > 0) x += gap;
            if (separatorsBefore?.Contains(i) == true) x += separatorWidth + gap;
            lefts[i] = x;
            x += baseSize * scales[i];
        }
        return (lefts, x);
    }

    /// <summary>
    /// Height of a launch bounce at time <paramref name="t"/> seconds: two hops that decay, ~0.9 s total,
    /// expressed as a fraction of the icon size.
    /// </summary>
    public static double Bounce(double t)
    {
        const double hop = 0.45;
        if (t < 0) return 0;
        int n = (int)(t / hop);
        if (n >= 2) return 0;
        double p = (t - n * hop) / hop;
        double amplitude = n == 0 ? 0.5 : 0.28;
        return amplitude * Math.Sin(p * Math.PI);
    }

    /// <summary>Critically-damped approach used to animate scales and slide offsets frame to frame.</summary>
    public static double Approach(double current, double target, double dtSeconds, double speed = 18)
    {
        double k = 1 - Math.Exp(-speed * dtSeconds);
        double next = current + (target - current) * k;
        return Math.Abs(next - target) < 0.0005 ? target : next;
    }

    public static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
}
