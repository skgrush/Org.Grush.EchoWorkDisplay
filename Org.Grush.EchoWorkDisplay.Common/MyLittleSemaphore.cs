using System.Runtime.CompilerServices;

namespace Org.Grush.EchoWorkDisplay.Common;

public class MyLittleSemaphore(TimeSpan timeout) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ILockScope GetLockOrThrow()
    {
        if (!_lock.Wait(TimeSpan.Zero))
            throw new WaitFailedException();
        return new Scope(this, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ILockScope> WaitAsync(CancellationToken cancellationToken)
    {
        var lockTask = _lock.WaitAsync(cancellationToken);
        return new Scope(
            this,
            lockTask
        ).WaitAsync();
    }

    public interface ILockScope : IAsyncDisposable, IDisposable;

    public class WaitFailedException : Exception;

    private readonly record struct Scope(MyLittleSemaphore locker, Task? lockWaiter) : ILockScope
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILockScope> WaitAsync()
        {
            var scope = this;
            return new(
                lockWaiter.ContinueWith<ILockScope>(t =>
                {
                    t.GetAwaiter().GetResult();

                    return scope;
                })
            );
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask DisposeAsync()
        {
            locker._lock.Release();
            return ValueTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            locker._lock.Dispose();
        }
    }

    public readonly record struct FakeScope : ILockScope
    {
        public ValueTask DisposeAsync()
            => default;

        public void Dispose()
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}