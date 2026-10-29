namespace Typrite.Shell.Fullscreen;

public static class FullscreenClassifier
{
    /// <summary>Unknown fails closed to yielding. Overlay needs allowlist, borderless compatibility and a passed physical validation.</summary>
    public static DisplayMode Decide(DisplayObservation observation, FullscreenPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(policy);

        if (observation.IsProtectedOrSecureContext)
        {
            return DisplayMode.ProtectedSurface;
        }

        if (observation.FullscreenCandidate is null)
        {
            return DisplayMode.NormalReserved;
        }

        var overlayAllowed = policy.IsAllowlisted(observation.FullscreenCandidate.App)
            && observation.IsBorderlessCompatible
            && observation.OverlayValidationPassed;

        return overlayAllowed ? DisplayMode.CompatibleOverlay : DisplayMode.YieldedFullscreen;
    }
}
