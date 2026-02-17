namespace Org.Grush.EchoWorkDisplay.Apple;

internal class AppleConsoleSession : AppleMediaSession
{
    protected override string InnerId => "ConsoleSession";
    protected override AppleMediaProperties? InnerProperties { get; set; }

    public void SetMedia(AppleMediaProperties? properties)
    {
        InnerProperties = properties;
    }
}