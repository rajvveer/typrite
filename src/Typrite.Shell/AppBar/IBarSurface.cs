namespace Typrite.Shell.AppBar;

/// <summary>Z-order control for the bar window. Implemented over SetWindowPos; never activates.</summary>
public interface IBarSurface
{
    void SendToBottom();

    void BringToTop();

    /// <summary>Make the bar topmost and non-activating for a tested, allowlisted borderless app.</summary>
    void EnterOverlay();

    void LeaveOverlay();
}
