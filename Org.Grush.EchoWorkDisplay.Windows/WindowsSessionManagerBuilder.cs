using Windows.Media.Control;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Windows;

public class WindowsSessionManagerBuilder : BaseSessionManagerBuilder
{
    public override async Task<BaseMediaSessionManager> BuildManagerAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        
        return new WindowsMediaSessionManager(manager);
    }
}