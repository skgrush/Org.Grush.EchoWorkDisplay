using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Org.Grush.EchoWorkDisplay;

public sealed class GlobalMediaReader : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly GlobalSystemMediaTransportControlsSessionManager _sessionManager;
    
    private GlobalSystemMediaTransportControlsSession? CurrentSession { get; set; }

    private MediaPropertiesProxy? CurrentMedia
    {
        get;
        set
        {
            if (field == value)
                return;
            
            field = value;
            MediaPropertiesChanged?.Invoke(this, value);
        }
    }

    public IMediaPropertiesProxy? CurrentMediaProperties => CurrentMedia;

    public event EventHandler<GlobalMediaReader, IMediaPropertiesProxy?>? MediaPropertiesChanged; 

    private GlobalMediaReader(GlobalSystemMediaTransportControlsSessionManager sessionManager)
    {
        _sessionManager = sessionManager;

        _sessionManager.CurrentSessionChanged += UpdateSession;
        UpdateSession(_sessionManager, null);
    }

    private void UpdateSession(GlobalSystemMediaTransportControlsSessionManager manager, CurrentSessionChangedEventArgs? e)
    {
        if (CurrentSession is not null)
        {
            CurrentMedia?.Dispose();
            CurrentMedia = null;
        }

        try
        {
            CurrentSession = _sessionManager.GetCurrentSession();
        }
        catch (Exception ex)
        {
            Console.WriteLine("{0} thrown getting media session: {1}", ex.GetType(), ex.Message);
            CurrentSession = null;
        }

        if (CurrentSession is not null)
        {
            CurrentSession.MediaPropertiesChanged += UpdateMediaProperties;
            UpdateMediaProperties(CurrentSession, null);
        }
    }

    private async void UpdateMediaProperties(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs? e)
    {
        if (CurrentMedia is not null)
        {
            CurrentMedia?.Dispose();
            CurrentMedia = null;
        }
        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (media is not null)
            {
                CurrentMedia = new(media);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("{0} thrown getting media properties: {1}", ex.GetType(), ex.Message);
            CurrentMedia = null;
        }

        // if (CurrentMedia is null)
        // {
        // }
    }

    public static async Task<GlobalMediaReader> InitAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        
        return new(manager);
    }

    public interface IMediaPropertiesProxy
    {
        string? Artist { get; }
        IRandomAccessStreamReference? Thumbnail { get; }
    }

    private sealed class MediaPropertiesProxy(
        GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties
    ) : IMediaPropertiesProxy, IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionMediaProperties? MediaProperties { get; set; } = mediaProperties;

        // public string? Artist
        // {
        //     get
        //     {
        //         if (MediaProperties is null)
        //             return null;
        //         if (
        //             MediaProperties.AlbumArtist is not (null or "") ||
        //             MediaProperties.AlbumArtist == MediaProperties.Artist
        //         )
        //             return MediaProperties.Artist;
        //         
        //         return $"{MediaProperties.Artist} ({MediaProperties.AlbumArtist})";
        //     }
        // }

        public string? Artist => MediaProperties?.Artist;
        
        public IRandomAccessStreamReference? Thumbnail => MediaProperties?.Thumbnail;

        public void Dispose()
        {
            MediaProperties = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _sessionManager.CurrentSessionChanged -= UpdateSession;
        CurrentSession?.MediaPropertiesChanged -= UpdateMediaProperties;
        
        CurrentMedia?.Dispose();
        CurrentMedia = null;
        CurrentSession = null;

        return ValueTask.CompletedTask;
    }
}