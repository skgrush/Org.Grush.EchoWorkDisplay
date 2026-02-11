// See https://aka.ms/new-console-template for more information

using System.Security.Cryptography;
using Org.Grush.EchoWorkDisplay;

// await using var mediaManager = await GlobalMediaReader.InitAsync();

HashAlgorithm hasher = SHA512.Create();

Config config = Config.Deserialize(new FileInfo("./config.json"))
    ?? new();

ScreenRenderer screenRenderer = new(config);

MicrosoftPresenceService microsoftPresenceService = new(config, hasher);

await using StatusCommWriter commWriter = new (Console.WriteLine, config);
await commWriter.WaitForPortRefreshAsync(CancellationToken.None);

await using UniversalMediaReader universal = new();

ScreenManagerService screenManagerService = new(
    config,
    screenRenderer,
    universal,
    commWriter,
    microsoftPresenceService
);

CancellationTokenSource loopCancellationTokenSource = new();

await screenManagerService.Initialize(loopCancellationTokenSource.Token);

// CancellationTokenSource perSessionCancellation = new();
// universal.SessionsChanged += async (sender, list) =>
// {
//     (var previousCancellation, perSessionCancellation) = (perSessionCancellation, new CancellationTokenSource());
//     var cancellationToken = perSessionCancellation.Token;
//     await previousCancellation.CancelAsync();
//     previousCancellation.Dispose();
//     
//     Console.WriteLine("\n\nSession change:");
//     foreach (var session in list)
//     {
//         if (session.MediaProperties is null)
//             Console.WriteLine("{0}: null", session.Id);
//         else
//             Console.WriteLine("{0}: {1}   by   {2}", session.Id, session.MediaProperties.Title, session.MediaProperties.Artist);
//     }
//     
//     // TODO: choose session
//     var chosenSession = list.ElementAtOrDefault(0);
//     
//     if (chosenSession?.MediaProperties is null)
//     {
//         await commWriter.WriteToPortAsync(new PiPicoMessages.NoMediaMessage().ToRawMessage(), cancellationToken);
//         return;
//     }
//
//     using var screenBitmap = await screenRenderer.RenderMediaScreen(chosenSession.MediaProperties, config.ScreenHardwareWidth, config.ScreenHardwareHeight, cancellationToken);
//     var bitmapMessage = new PiPicoMessages.DrawBitmap(screenBitmap, SKPointI.Empty);
//     await commWriter.WriteToPortAsync(bitmapMessage.ToRawMessage(), cancellationToken);
// };
//
// await universal.Go();

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
        Console.WriteLine(ex);
    }
}