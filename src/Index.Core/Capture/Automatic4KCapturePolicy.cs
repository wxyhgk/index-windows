using Index.Actions;

namespace Index.Capture;

/// <summary>Pure routing policy for upgrading ordinary capture actions to a dense window frame.</summary>
public static class Automatic4KCapturePolicy
{
    public static bool ShouldUpgrade(
        bool isEnabled,
        string actionId,
        nint targetWindowHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return isEnabled
            && targetWindowHandle != nint.Zero
            && actionId is CaptureActionIds.Complete or CaptureActionIds.Copy;
    }
}
