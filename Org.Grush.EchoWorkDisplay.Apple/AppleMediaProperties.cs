using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;

internal sealed record AppleMediaProperties(
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