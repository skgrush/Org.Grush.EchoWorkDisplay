using Windows.Media.Control;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Windows;

public class WindowsMediaSession : BaseMediaSession
{
    private readonly List<EventHandler<WindowsMediaSession, WindowsMediaSession>> _changedHandlers = [];
    
    private GlobalSystemMediaTransportControlsSession? ControlsSession { get; set; }
    private WindowsMediaProperties? WindowsMediaProperties { get; set; }
    

    public override string? Id => ControlsSession?.SourceAppUserModelId;
    public override IMediaProperties? MediaProperties => WindowsMediaProperties;
    protected override object? Equater => ControlsSession;
    public override event EventHandler<BaseMediaSession, BaseMediaSession> MediaChanged
    {
        add { lock (_changedHandlers) _changedHandlers.Add(value); }
        remove { lock (_changedHandlers) _changedHandlers.Remove(value); }
    }

    public WindowsMediaSession(GlobalSystemMediaTransportControlsSession controlsSession)
    {
        ControlsSession = controlsSession;

        controlsSession.MediaPropertiesChanged += ControlsSessionOnMediaPropertiesChanged;
        _ = UpdateMediaAsync();
    }

    public bool Equals(GlobalSystemMediaTransportControlsSession? sess)
        => sess is not null && ControlsSession == sess;
    
    protected override ValueTask DisposeAsyncCore()
    {
        ControlsSession?.MediaPropertiesChanged -= ControlsSessionOnMediaPropertiesChanged;

        lock (_changedHandlers)
        {
            _changedHandlers.Clear();
        }
        return default;
    }

    private async Task UpdateMediaAsync()
    {
        if (ControlsSession is null)
            return;
        
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = await ControlsSession.TryGetMediaPropertiesAsync();
        
        if (WindowsMediaProperties.Equals(WindowsMediaProperties, props))
            return;
        
        WindowsMediaProperties = props is null ? null : new(props);
        
        lock (_changedHandlers)
            _changedHandlers.ForEach(cb => cb(this, this));
    }

    private void ControlsSessionOnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = UpdateMediaAsync();
    }
}