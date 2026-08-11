namespace GameDraw.Core.Presentation;

/// <summary>
/// Width buckets shared by the WinUI shell and any future companion views.
/// Keeping the policy free of UI types makes the breakpoints deterministic and
/// testable without launching a desktop window.
/// </summary>
public enum ResponsiveLayoutMode
{
    Compact = 0,
    Medium = 1,
    Expanded = 2
}
public static class ResponsiveLayoutPolicy
{
    public const double CompactMaxWidth = 719d;

    public const double MediumMaxWidth = 1119d;

    public static ResponsiveLayoutMode FromWidth(double width)
    {
        if (!double.IsFinite(width) || width <= CompactMaxWidth)
        {
            return ResponsiveLayoutMode.Compact;
        }

        return width <= MediumMaxWidth
            ? ResponsiveLayoutMode.Medium
            : ResponsiveLayoutMode.Expanded;
    }

    public static bool IsTwoColumn(ResponsiveLayoutMode mode)
        => mode is ResponsiveLayoutMode.Medium or ResponsiveLayoutMode.Expanded;
}
