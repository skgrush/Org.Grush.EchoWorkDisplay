using SkiaSharp;

namespace Org.Grush.EchoWorkDisplay;

public class ScreenManagerService(
    Config config,
    ScreenRenderer screenRenderer,
    UniversalMediaReader universal,
    StatusCommWriter commWriter,
    MicrosoftPresenceService microsoftPresenceService
)
{
    private PiPicoMessages.IMediaMessage? CurrentMediaMessage { get; set; }
    private PiPicoMessages.DrawPresenceBitmap? CurrentPresenceMessage { get; set; }
    
    public async Task Initialize(CancellationToken externalCancellationToken)
    {
        CancellationTokenSource perSessionCancellation = new();
        universal.SessionsChanged += async (sender, list) =>
        {
            (var previousCancellation, perSessionCancellation) = (perSessionCancellation, CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken));
            var cancellationToken = perSessionCancellation.Token;
            cancellationToken.Register(() => CurrentMediaMessage?.Dispose());
            await previousCancellation.CancelAsync();
            previousCancellation.Dispose();

            if (!config.ShowPlayingMedia)
            {
                CurrentMediaMessage = new PiPicoMessages.NoMediaMessage();
            }
    
            Console.WriteLine("\n\nSession change:");
            foreach (var session in list)
            {
                if (session.MediaProperties is null)
                    Console.WriteLine("{0}: null", session.Id);
                else
                    Console.WriteLine("{0}: {1}   by   {2}", session.Id, session.MediaProperties.Title, session.MediaProperties.Artist);
            }
    
            // TODO: choose session
            var chosenSession = list.ElementAtOrDefault(0);

            // debounce
            await Task.Delay(100, cancellationToken);
    
            if (chosenSession?.MediaProperties is null)
            {
                var msg = new PiPicoMessages.NoMediaMessage();
                // await commWriter.WriteToPortAsync(msg.ToRawMessage(), cancellationToken);
                CurrentMediaMessage = msg;
                return;
            }

            var screenBitmap = await screenRenderer.RenderMediaScreen(chosenSession.MediaProperties, config.ScreenHardwareWidth, config.ScreenHardwareHeight, cancellationToken);
            var bitmapMessage = new PiPicoMessages.DrawMediaBitmap(screenBitmap, SKPointI.Empty, chosenSession.MediaProperties);
            CurrentMediaMessage = bitmapMessage;

            await WriteMessagesAsync(cancellationToken);
            // await commWriter.WriteToPortAsync(bitmapMessage.ToRawMessage(), cancellationToken);
        };

        // CancellationTokenSource perPresenceCancellation = new();
        microsoftPresenceService.PresenceChanged += async (sender, presenceDescription) =>
        {
            var screenBitmap = await screenRenderer.RenderPresenceScreen(presenceDescription, config.ScreenHardwareWidth, config.ScreenHardwareHeight, externalCancellationToken);
            
            CurrentPresenceMessage =
                screenBitmap is null
                    ? null
                    : new PiPicoMessages.DrawPresenceBitmap(screenBitmap, SKPointI.Empty, presenceDescription);
            
            await WriteMessagesAsync(externalCancellationToken);
        };

        microsoftPresenceService.LoopAsync(externalCancellationToken);
        await universal.Go();
    }

    private async Task WriteMessagesAsync(CancellationToken currentCancellationToken)
    {
        if (CurrentPresenceMessage is not (null or { Description.Error: {} }) && CurrentMediaMessage is (null or PiPicoMessages.NoMediaMessage))
        {
            await commWriter.WriteToPortAsync(CurrentPresenceMessage.ToRawMessage(), currentCancellationToken);
        }
        else if (CurrentMediaMessage is not (null or PiPicoMessages.NoMediaMessage) && CurrentPresenceMessage is (null or { Description.Error: {} }))
        {
            await commWriter.WriteToPortAsync(CurrentMediaMessage.ToRawMessage(), currentCancellationToken);
        }
        else if (CurrentMediaMessage is not null && CurrentPresenceMessage is not null)
        {
            // both are present, decide between them!
            var mediaMessage = CurrentMediaMessage as PiPicoMessages.DrawMediaBitmap;

            float mediaPriority = config.GetPriority(mediaMessage?.MediaProperties);
            float presencePriority = config.GetPriority(CurrentPresenceMessage.Description.Availability ?? MicrosoftPresenceService.Availability.PresenceUnknown);

            await commWriter.WriteToPortAsync(
                (
                    presencePriority >= mediaPriority
                        ? CurrentPresenceMessage
                        : CurrentMediaMessage
                ).ToRawMessage(),
                currentCancellationToken
            );
        }
        else
        {
            await commWriter.WriteToPortAsync(new PiPicoMessages.NoMediaMessage().ToRawMessage(), currentCancellationToken);
        }
    }
}