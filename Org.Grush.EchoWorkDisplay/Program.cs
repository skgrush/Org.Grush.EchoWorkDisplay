// See https://aka.ms/new-console-template for more information

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Org.Grush.EchoWorkDisplay;

#if WINDOWS
using Org.Grush.EchoWorkDisplay.Windows.PlatformGeneric;
#elif MACCATALYST || MACOS || OSX
using Org.Grush.EchoWorkDisplay.Apple.PlatformGeneric;
#else
using Org.Grush.EchoWorkDisplay.FallbackPlatformGeneric;
#endif


// await using var mediaManager = await GlobalMediaReader.InitAsync();

var serviceCollection = new ServiceCollection()
    .AddLogging(c => c.SetMinimumLevel(LogLevel.Trace))
    .AddSingleton<ConfigProvider>()
    .AddTransient<HashAlgorithm>(_ => SHA512.Create())
    .AddPlatformServices()
    .AddScoped<ScreenRenderer>()
    .AddScoped<MicrosoftPresenceService>()
    .AddScoped<StatusCommWriter>()
    .AddScoped<UniversalMediaReader>()
    .AddScoped<ScreenManagerService>()
;

// HashAlgorithm hasher = SHA512.Create();
//
// Config config = Config.Deserialize(new FileInfo("./config.json"))
//     ?? new();

// IPlatformManager platformManager =
// #if WINDOWS
//     new Org.Grush.EchoWorkDisplay.Windows.WindowsPlatformManager();
// #elif MACCATALYST || MACOS || OSX
//     new Org.Grush.EchoWorkDisplay.Apple.ApplePlatformManager();
// #else
//     ((Func<IPlatformManager>)(() => throw new PlatformNotSupportedException()))();
// #endif
    

// ScreenRenderer screenRenderer = new(config);

// MicrosoftPresenceService microsoftPresenceService = new(config, hasher);

// await using StatusCommWriter commWriter = new (Console.WriteLine, config, platformManager);

await using var sp = serviceCollection.BuildServiceProvider();

var commWriter = sp.GetRequiredService<StatusCommWriter>();
var screenManagerService = sp.GetRequiredService<ScreenManagerService>();

await commWriter.WaitForPortRefreshAsync(CancellationToken.None);

// await using UniversalMediaReader universal = new(platformManager.SessionManagerBuilder);

// ScreenManagerService screenManagerService = new(
//     config,
//     screenRenderer,
//     universal,
//     commWriter,
//     microsoftPresenceService
// );

CancellationTokenSource loopCancellationTokenSource = new();

await screenManagerService.Initialize(loopCancellationTokenSource.Token);

var loopLogger = sp.GetRequiredService<ILogger<Program>>();
while (true)
{
    try
    {
        await Task.Delay(1000);
        Console.Write(".");
        await Console.Out.FlushAsync();
    }
    catch(Exception ex)
    {
        loopLogger.LogError(ex, "Error in main loop");
    }
}