using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;
using Windows.Storage.Streams;
using JetBrains.Annotations;
using Org.Grush.EchoWorkDisplay.Common;
using WindowsBuffer = Windows.Storage.Streams.Buffer;

namespace Org.Grush.EchoWorkDisplay.Windows;

public class WindowsMediaProperties(
    GlobalSystemMediaTransportControlsSessionMediaProperties windowsAbiProperties
) : IMediaProperties
{
    
    public string? Artist => windowsAbiProperties.Artist;
    public string? AlbumTitle => windowsAbiProperties.AlbumTitle;
    public string? Title => windowsAbiProperties.Title;
    
    public bool Equals(GlobalSystemMediaTransportControlsSessionMediaProperties? other)
        => windowsAbiProperties.Equals(other);

    public static bool Equals(
        WindowsMediaProperties? left,
        GlobalSystemMediaTransportControlsSessionMediaProperties? right
    )
        => (left, right) switch
        {
            (null, null) => true,
            ({ } a, { } b) => a.Equals(b),
            _ => false
        };
    
    
    [MustDisposeResource]
    public async Task<Stream?> GetThumbnailStream(CancellationToken cancellationToken)
    {
        if (windowsAbiProperties.Thumbnail is not {} thumb)
            return null;

        using var reader = await thumb.OpenReadAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (reader.Size < 16 || !reader.CanRead)
            return null;

        WindowsBuffer buffer = new((uint)reader.Size);

        var asyncOperation = reader.ReadAsync(buffer, (uint)reader.Size, InputStreamOptions.None);
        cancellationToken.Register(asyncOperation.Cancel);
        await asyncOperation;

        return buffer.AsStream();
    }
}