namespace Typrite.Shell.AppBar;

/// <summary>Z-order control for the bar window. Implemented over SetWindowPos; never activates.</summary>
public interface IBarSurface
{
    void SendToBottom();

    void BringToTop();
}
