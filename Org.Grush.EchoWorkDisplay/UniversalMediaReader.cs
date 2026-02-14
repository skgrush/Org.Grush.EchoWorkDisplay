using Windows.Media.Control;

namespace Org.Grush.EchoWorkDisplay;

public sealed class UniversalMediaReader : IAsyncDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager _sessionManager;
    
    private List<Session> _sessions = [];

    private event EventHandler<UniversalMediaReader, IReadOnlyList<Session>>? _sessionsChanged;
    
    public event EventHandler<UniversalMediaReader, IReadOnlyList<Session>> SessionsChanged
    {
        add
        {
            _sessionsChanged += value;
            value.Invoke(null, _sessions);
        }
        remove
            => _sessionsChanged -= value;
    }
    
    public int SessionCount => _sessions.Count;

    public async Task Go()
    {
        if (_sessionManager is not null)
            throw new InvalidOperationException("Already going...");

        _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (_sessionManager is null)
            throw new InvalidOperationException("Not found...");
        
        SessionManagerOnSessionsChanged(_sessionManager, null);
        _sessionManager.SessionsChanged += SessionManagerOnSessionsChanged;
    }

    private void SessionManagerOnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs? args
    )
    {
        var sessions = sender.GetSessions();

        if (
            _sessions.Count == sessions.Count &&
            _sessions.Zip(sessions).All(pair => pair.First.Equals(pair.Second))
        )
            return;
        
        
        _sessions.ForEach(s => s.Dispose());
        _sessions.Clear();
        _sessions.AddRange(
            sessions.Select(sess =>
                new Session(sess, (changedSession => _sessionsChanged?.Invoke(null, _sessions)))
            )
        );
        
        _sessionsChanged?.Invoke(this, _sessions);

        // var deleted = Session
        //     .GetDeletedSessions(_sessions, sessions)
        //     .ToList();
        // var added = Session
        //     .GetAddedSessions(_sessions, sessions)
        //     .ToList();

        // if (_sessions.Count is 0)
        // {
        //     _sessions.AddRange(added.Select(a => Session.From(a.session)));
        // }
        // else
        // {
        //     int i = 0;
        //     int deletedCount = 0;
        //     while (deleted.Count > 0 || added.Count > 0)
        //     {
        //         int? nextAddedIdx = added.Count is 0 ? null : added.First().idx;
        //
        //         if (i < nextAddedIdx)
        //         {
        //             if (_sessions.ElementAtOrDefault(i) is { } sessionAtI && deleted.Remove(sessionAtI))
        //             {
        //                 sessionAtI.Dispose();
        //                 _sessions.RemoveAt(i);
        //                 ++deletedCount;
        //                 continue;
        //             }
        //             else
        //             {
        //                 ++i;
        //             }
        //         }
        //         else if (i == nextAddedIdx)
        //         {
        //             if (_sessions.ElementAtOrDefault(i) is { } sessionAtI && deleted.Remove(sessionAtI))
        //             {
        //                 sessionAtI.Dispose();
        //                 _sessions[i] = Session.From(added.First().session);
        //                 ++deletedCount;
        //             }
        //             else
        //             {
        //                 _sessions.Insert(i, Session.From(added.First().session));
        //             }
        //         }
        //     }
        // }
    }

    public sealed class Session : IDisposable
    {
        private GlobalSystemMediaTransportControlsSession? ControlsSession { get; set; }
        private Action<Session> ChangeCallback { get; }

        public GlobalSystemMediaTransportControlsSessionMediaProperties? MediaProperties { get; private set; }

        public string? Id => ControlsSession?.SourceAppUserModelId;

        public bool Equals(GlobalSystemMediaTransportControlsSession other)
            => other == ControlsSession;

        public Session(
            GlobalSystemMediaTransportControlsSession session,
            Action<Session> changeCallback
        )
        {
            ControlsSession = session;
            ChangeCallback = changeCallback;
            session.MediaPropertiesChanged += SessionOnMediaPropertiesChanged;
            _ = UpdateMediaAsync();
        }

        private void SessionOnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            _ = UpdateMediaAsync();
        }

        private async Task UpdateMediaAsync()
        {
            if (ControlsSession is null)
            {
                return;
            }

            var props = await ControlsSession.TryGetMediaPropertiesAsync();
            if (MediaProperties == props)
                return;
            
            MediaProperties = props;
            ChangeCallback(this);
        }

        public void Dispose()
        {
            MediaProperties = null;
            ControlsSession?.MediaPropertiesChanged -= SessionOnMediaPropertiesChanged;
            ControlsSession = null;
        }
        
    //     public static IEnumerable<Session> GetDeletedSessions(
    //         IEnumerable<Session> sessions,
    //         IEnumerable<GlobalSystemMediaTransportControlsSession> otherSessions
    //     ) =>
    //         sessions.ExceptBy(otherSessions, session => session.ControlsSession);
    //
    //     public static IEnumerable<(GlobalSystemMediaTransportControlsSession session, int idx)> GetAddedSessions(
    //         IEnumerable<Session> sessions,
    //         IEnumerable<GlobalSystemMediaTransportControlsSession> otherSessions
    //     )
    //     {
    //         var currentSessions = sessions.Select(s => s.ControlsSession).ToHashSet();
    //
    //         foreach ((var otherSession, int idx) in otherSessions.Select((v, i) => (v, i)))
    //         {
    //             if (!currentSessions.Contains(otherSession))
    //                 yield return (otherSession, idx);
    //         }
    //     }
    }

    public async ValueTask DisposeAsync()
    {
        _sessionManager.SessionsChanged -= SessionManagerOnSessionsChanged;

        _sessions.ForEach(s => s.Dispose());
        _sessions.Clear();
    }
}