namespace Org.Grush.EchoWorkDisplay.Common;

public abstract class BaseSessionManagerBuilder
{
    public abstract Task<BaseMediaSessionManager> BuildManagerAsync();
}