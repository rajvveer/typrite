using Typrite.Shell.Displays;

namespace Typrite.Shell.Fullscreen;

public static class FullscreenObserver
{
    public const int DefaultTolerancePx = 2;

    /// <summary>
    /// Builds the observation for one display from a z-ordered window snapshot (top first).
    /// <paramref name="overlayProbe"/> is only consulted for allowlisted apps, since it may touch the shell.
    /// </summary>
    public static DisplayObservation Observe(
        DisplayInfo display,
        IReadOnlyList<WindowObservation> windowsTopToBottom,
        bool secureDesktop,
        FullscreenPolicy policy,
        Func<WindowObservation, bool> overlayProbe)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(windowsTopToBottom);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(overlayProbe);

        WindowObservation? candidate = null;
        foreach (var w in windowsTopToBottom)
        {
            if (FullscreenCandidateDetector.IsCandidate(w, display.Bounds, DefaultTolerancePx))
            {
                candidate = w;
                break;
            }
        }

        var probed = candidate is not null && policy.IsAllowlisted(candidate.App) && overlayProbe(candidate);

        return new DisplayObservation(
            IsProtectedOrSecureContext: secureDesktop,
            FullscreenCandidate: candidate,
            IsBorderlessCompatible: candidate?.IsBorderless ?? false,
            OverlayValidationPassed: probed);
    }
}
