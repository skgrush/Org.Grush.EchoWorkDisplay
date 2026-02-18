using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;

internal class AppleSessionManagerBuilder(ApplePlatformManager platformManager) : BaseSessionManagerBuilder
{
    public override async Task<BaseMediaSessionManager> BuildManagerAsync()
    {
        return new AppleMediaSessionManager(platformManager);
    }
}
