using Orchard.Rendering;

namespace Orchard.Model;

public enum AppKind { Normal, Launchpad, Trash }

/// <summary>One icon in the dock: a pinned shortcut, a running app, or both.</summary>
public sealed class AppEntry
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required IconArt Icon { get; set; }
    public AppKind Kind { get; init; } = AppKind.Normal;

    /// <summary>Path, URI or shell target used to launch the app.</summary>
    public string? Target { get; set; }
    public string? Arguments { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>Top-level window handles that belong to this app (empty in preview mode).</summary>
    public List<nint> Windows { get; } = [];

    /// <summary>Set by the preview platform, which has no real windows.</summary>
    public bool PreviewRunning { get; set; }

    public bool IsRunning => Windows.Count > 0 || PreviewRunning;

    public override string ToString() => $"{Name} ({Id})";
}
