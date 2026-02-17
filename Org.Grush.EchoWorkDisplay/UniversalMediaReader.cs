// using Windows.Media.Control;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay;

public sealed class UniversalMediaReader(BaseSessionManagerBuilder managerBuilder) : IAsyncDisposable
{
    private BaseMediaSessionManager _sessionManager;

    public event EventHandler<object,  BaseMediaSessionManager.SessionsChangedEventArgs> SessionsChanged
    {
        add => _sessionManager.SessionsChanged += value;
        remove => _sessionManager.SessionsChanged -= value;
    }
    
    public int SessionCount => _sessionManager.Sessions.Count;

    public async Task Go()
    {
        if (_sessionManager is not null)
            throw new InvalidOperationException("Already going...");

        _sessionManager = await managerBuilder.BuildManagerAsync();
        if (_sessionManager is null)
            throw new InvalidOperationException("Not found...");
    }

    public ValueTask DisposeAsync()
        => _sessionManager.DisposeAsync();
}