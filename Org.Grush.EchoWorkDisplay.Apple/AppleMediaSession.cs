using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;

internal class AppleMediaSession : BaseMediaSession
{
    protected virtual string? InnerId { get; set; }

    protected virtual AppleMediaProperties? InnerProperties
    {
        get;
        set
        {
            field = value;
            MediaChanged.Invoke(this, this);
        }
    }

    public override string? Id => InnerId;
    public override IMediaProperties? MediaProperties => InnerProperties;
    protected override object? Equater => InnerProperties;

    public override event EventHandler<BaseMediaSession, BaseMediaSession> MediaChanged;
    protected override ValueTask DisposeAsyncCore()
    {
        InnerProperties = null;
        return default;
    }
}