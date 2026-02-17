using System.Runtime.InteropServices;
using Windows.Media.Control;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Windows;

public class WindowsMediaSessionManager : BaseMediaSessionManager
{
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly Lock _lock = new();
    
    private readonly List<WindowsMediaSession> _sessions = [];
    
    private readonly List<EventHandler<BaseMediaSessionManager, SessionsChangedEventArgs>> _changedHandlers = [];

    public override IReadOnlyList<BaseMediaSession> Sessions
    {
        get
        {
            using var _ = _lock.EnterScope();
            return [.._sessions];
        }
    }

    public WindowsMediaSessionManager(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        manager.SessionsChanged += ManagerOnSessionsChanged;
        ManagerOnSessionsChanged(null, null);
    }

    private async void ManagerOnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager? sender, global::Windows.Media.Control.SessionsChangedEventArgs? args)
    {
        _lock.Enter();
        try
        {
            var sessions = _manager.GetSessions();

            if (
                _sessions.Count == sessions.Count &&
                _sessions.Zip(sessions).All(pair => pair.First.Equals(pair.Second))
            )
                return;

            foreach (var sess in _sessions)
                await sess.DisposeAsync();
            
            _sessions.Clear();
            _sessions.AddRange(
                sessions.Select(sess => new WindowsMediaSession(sess))
            );
            foreach (var sess in _sessions)
                sess.MediaChanged += SessOnMediaChanged;

            SessOnMediaChanged(null, null);
        }
        finally
        {
            _lock.Exit();
        }
    }

    private void SessOnMediaChanged(BaseMediaSession? sender, BaseMediaSession? e)
    {
        lock (_changedHandlers)
        {
            SessionsChangedEventArgs args = new([.._sessions]);
            foreach (var changedHandler in _changedHandlers)
                changedHandler(this, args);
        }
    }

    public override event EventHandler<BaseMediaSessionManager, SessionsChangedEventArgs> SessionsChanged
    {
        add {
            lock (_changedHandlers)
            {
                _changedHandlers.Add(value);
                if (_sessions.Count is not 0)
                    value(this, new([.._sessions]));
            }
        }
        remove { lock(_changedHandlers) _changedHandlers.Remove(value); }
    }
    
    

    protected override async ValueTask DisposeAsyncCore()
    {
        _manager.SessionsChanged -= ManagerOnSessionsChanged;
        
        foreach (var windowsMediaSession in _sessions)
            await windowsMediaSession.DisposeAsync();
    }
}