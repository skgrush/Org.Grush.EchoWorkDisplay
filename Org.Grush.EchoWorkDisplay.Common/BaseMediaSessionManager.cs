namespace Org.Grush.EchoWorkDisplay.Common;

public abstract class BaseMediaSessionManager : IAsyncDisposable
{
    protected bool IsDisposed { get; private set; }
    
    public abstract event EventHandler<BaseMediaSessionManager, SessionsChangedEventArgs> SessionsChanged;
    
    public abstract IReadOnlyList<BaseMediaSession> Sessions { get; }

    protected abstract ValueTask DisposeAsyncCore();
    
    public ValueTask DisposeAsync()
    {
        lock (this)
        {
            if (IsDisposed)
                return ValueTask.CompletedTask;
            IsDisposed = true;
        }

        return DisposeAsyncCore();
    }

    public record struct SessionsChangedEventArgs(
        IEnumerable<BaseMediaSession> Sessions
    );
}