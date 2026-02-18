using Microsoft.Extensions.DependencyInjection;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple.PlatformGeneric;


public static class StartupExtensions
{
    extension(IServiceCollection serviceCollection)
    {
        public IServiceCollection AddPlatformServices()
            => serviceCollection
                .AddScoped<IPlatformManager, ApplePlatformManager>()
                .AddTransient<BaseSessionManagerBuilder, AppleSessionManagerBuilder>()
        ;
    }
}