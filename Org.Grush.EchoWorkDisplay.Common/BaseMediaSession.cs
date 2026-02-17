namespace Org.Grush.EchoWorkDisplay.Common;

public abstract class BaseMediaSession : IEquatable<BaseMediaSession>, IAsyncDisposable
{
    public bool IsDisposed { get; private set; }
    
    public abstract string? Id { get; }
    public abstract IMediaProperties? MediaProperties { get; }
    
    protected abstract object? Equater { get; }

    public abstract event EventHandler<BaseMediaSession, BaseMediaSession> MediaChanged;
    
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

    public bool Equals(BaseMediaSession? other)
        => Equater is not null && other is not null && Equater.Equals(other.Equater);
}