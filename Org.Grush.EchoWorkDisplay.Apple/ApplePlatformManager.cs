using System.Net.Http.Headers;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;

public sealed record AppleMediaProperties(
    string? Artist,
    string? AlbumTitle,
    string? Title,
    byte[]? Thumbnail
) : IMediaProperties
{
    public static readonly AppleMediaProperties Default = new(null, null, null, null);
    
    public async Task<Stream?> GetThumbnailStream(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Thumbnail is null)
            return null;

        return new MemoryStream(Thumbnail, writable: false);
    }
}

public sealed class ApplePlatformManager : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly HttpClient _client = new();
    
    public AppleSessionManagerBuilder SessionManagerBuilder { get; }

    public ApplePlatformManager()
    {
        SessionManagerBuilder = new AppleSessionManagerBuilder(this);
        
        ConsoleLoop();
    }

    private const string Prompt = 
        """
        Enter (1) change media, (2) change presence:
        """;

    internal event EventHandler<ApplePlatformManager, AppleMediaProperties?>? consoleDeclaredMediaProperties;
    internal event EventHandler<ApplePlatformManager, PresenceAvailability>? consoleDeclaredPresence;

    private async void ConsoleLoop()
    {
        await using var stdin = Console.OpenStandardInput();
        using var stdinReader = new StreamReader(stdin);

        while (true)
        {
            Console.Write(Prompt + " ");
            var line = await stdinReader.ReadLineAsync(_cancellationTokenSource.Token);
            if (line is null)
            {
                Console.WriteLine("?");
                continue;
            }

            switch (line.Trim().ToLowerInvariant())
            {
                case "1":
                case "change media":
                    await PromptChangeMedia(stdinReader);
                    break;
                case "2":
                case "change presence":
                    await PromptPresence(stdinReader);
                    break;
                default:
                    await Console.Error.WriteLineAsync("Unsupported answer...");
                    break;
            }
        }
    }

    private async Task PromptPresence(StreamReader streamReader)
    {
        PresenceAvailability presenceAvailability;
        try
        {
            while (true)
            {
                Console.WriteLine("Presence options: {0}", string.Join(", ", Enum.GetNames<PresenceAvailability>()));
                Console.Write("Enter presence [PresenceUnknown]: ");

                var str = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
                if (str is null)
                {
                    Console.WriteLine("?");
                    continue;
                }

                str = str.Trim();
                if (str is "")
                {
                    presenceAvailability = PresenceAvailability.PresenceUnknown;
                    break;
                }

                if (Enum.TryParse(str, true, out presenceAvailability))
                {
                    break;
                }
                else
                {
                    await Console.Error.WriteLineAsync("ERROR: Failed to parse presence option...\n");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"ERROR: {ex.GetType()}: {ex.Message}\n");
            return;
        }
        
        Console.WriteLine("Successfully chose presence: {0}", presenceAvailability);
        consoleDeclaredPresence?.Invoke(this, presenceAvailability);
    }

    private async Task PromptChangeMedia(StreamReader streamReader)
    {
        try
        {
            Console.Write("Enter Arist: ");
            var artist = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
            
            Console.WriteLine("Enter Album: ");
            var album = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
            
            Console.Write("Enter Title: ");
            var title = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
            
            Uri? thumbnailUri = null;
            byte[]? thumbnail = null;
            while (true)
            {
                Console.WriteLine("Enter Thumbnail URL: ");
                var url = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);

                if (url is null or "")
                {
                    break;
                }

                try
                {
                    thumbnailUri = new(url);
                    using HttpRequestMessage req = new(HttpMethod.Get, thumbnailUri);
                    req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("image/png,image/jpeg;q=0.8,image/*;q=0.5"));

                    using var response = await _client.GetAsync(thumbnailUri, HttpCompletionOption.ResponseHeadersRead);
                    
                    response.EnsureSuccessStatusCode();

                    var mediaType = response.Content.Headers.ContentType?.MediaType;

                    if (mediaType is not null && mediaType.StartsWith("image/"))
                    {
                        thumbnail = await response.Content.ReadAsByteArrayAsync(_cancellationTokenSource.Token);
                        new ReadOnlyMemory<byte>(thumbnail);
                        break;
                    }
                    else
                    {
                        Console.WriteLine("?");
                        continue;
                    }
                }
                catch (Exception thumbE)
                {
                    await Console.Error.WriteLineAsync($"THUMB ERROR: {thumbE.GetType()}: {thumbE.Message}");
                }
            }
            
            AppleMediaProperties? props = new(
                artist,
                album,
                title,
                thumbnail
            );
            if (props == AppleMediaProperties.Default)
                props = null;
            
            consoleDeclaredMediaProperties?.Invoke(this, props);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return new(_cancellationTokenSource.CancelAsync());
    }
}