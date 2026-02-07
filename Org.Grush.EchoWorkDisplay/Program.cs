// See https://aka.ms/new-console-template for more information

using Org.Grush.EchoWorkDisplay;

// await using var mediaManager = await GlobalMediaReader.InitAsync();

Config config = Config.Deserialize(new FileInfo("./config.json"))
    ?? new();

await using var commWriter = new StatusCommWriter(Console.WriteLine, config);
await commWriter.WaitForPortRefreshAsync(CancellationToken.None);

await using var universal = new UniversalMediaReader();

CancellationTokenSource cancellation = new();
universal.SessionsChanged += async (sender, list) =>
{
    (var previousCancellation, cancellation) = (cancellation, new CancellationTokenSource());
    var cancellationToken = cancellation.Token;
    await previousCancellation.CancelAsync();
    previousCancellation.Dispose();
    
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
    
    if (chosenSession?.MediaProperties is null)
    {
        await commWriter.WriteToPortAsync(new PiPicoMessages.NoMediaMessage().ToRawMessage(), cancellationToken);
        return;
    }
    
    var media = chosenSession.MediaProperties;
    var thumb = media.Thumbnail;
    
    var e = new PiPicoMessages.MediaMetadataChanged(media.Artist, media.Title, ResetMediaImage: true);
    await commWriter.WriteToPortAsync(e.ToRawMessage(), cancellationToken);

    using var t = await thumb.OpenReadAsync();
    if (t.Size < 16 || !t.CanRead)
        return;

    var image = SkiaSharp.SKImage.FromEncodedData(t.AsStreamForRead());
    cancellationToken.ThrowIfCancellationRequested();

    new SkiaSharp.SKBitmap()

};

await universal.Go();

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