using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;

internal class AppleMediaSessionManager : BaseMediaSessionManager
{
    private readonly Lock _lock = new();
    private readonly AppleConsoleSession _consoleSession = new();
    private readonly List<AppleMediaSession> _sessions;

    private readonly List<EventHandler<BaseMediaSessionManager, SessionsChangedEventArgs>> _changedHandlers = [];

    public AppleMediaSessionManager(ApplePlatformManager platformManager)
    {
        _sessions = [_consoleSession];
        
        platformManager.consoleDeclaredMediaProperties += ((sender, properties) =>
        {
            _consoleSession.SetMedia(properties);
        });
    }

    public override event EventHandler<BaseMediaSessionManager, SessionsChangedEventArgs> SessionsChanged
    {
        add
        {
            lock (_changedHandlers)
            {
                _changedHandlers.Add(value);
                if (_sessions.Count is not 0)
                    value(this, new([.._sessions]));
            }
        }
        remove
        {
            lock (_changedHandlers)
                _changedHandlers.Remove(value);
        }
    }

    public override IReadOnlyList<BaseMediaSession> Sessions
    {
        get
        {
            using var _ = _lock.EnterScope();
            return [.._sessions];
        }
    }
    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var session in _sessions)
            await session.DisposeAsync();
    }
}