using GameDraw.Core.Models;

namespace GameDraw.Core.Targeting;

public sealed record TargetWindowSnapshot(
    long Handle,
    string ProcessName,
    string Title,
    int ClientWidth,
    int ClientHeight,
    uint Dpi,
    bool IsForeground);

public enum TargetVerificationSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2
}

public sealed record TargetVerificationIssue(
    TargetVerificationSeverity Severity,
    string Code,
    string Message);

public sealed record TargetVerificationResult(
    bool IsSafeToRun,
    IReadOnlyList<TargetVerificationIssue> Issues)
{
    public static TargetVerificationResult Safe()
        => new(true, Array.Empty<TargetVerificationIssue>());

    public static TargetVerificationResult Unsafe(string code, string message)
        => new(false, new[] { new TargetVerificationIssue(TargetVerificationSeverity.Error, code, message) });
}
