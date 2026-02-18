using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Windows.PlatformGeneric;


public static class StartupExtensions
{
    extension(IServiceCollection serviceCollection)
    {
        [UsedImplicitly]
        public IServiceCollection AddPlatformServices()
            => serviceCollection
                .AddScoped<IPlatformManager, WindowsPlatformManager>()
                .AddTransient<BaseSessionManagerBuilder, WindowsSessionManagerBuilder>()
            ;
    }
}