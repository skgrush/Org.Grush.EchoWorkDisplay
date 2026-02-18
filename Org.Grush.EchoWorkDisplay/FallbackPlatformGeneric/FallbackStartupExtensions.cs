using Microsoft.Extensions.DependencyInjection;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.FallbackPlatformGeneric;

#if WINDOWS
#elif MACCATALYST || MACOS || OSX
#else
public static class FallbackStartupExtensions
{
    extension(IServiceCollection serviceCollection)
    {
        public IServiceCollection AddPlatformServices()
            => serviceCollection
                .AddSingleton<IPlatformManager>(_ => throw new PlatformNotSupportedException());
    }
}
#endif