namespace Org.Grush.EchoWorkDisplay.Common;

public interface IMediaProperties
{
    public string? Artist { get; }
    public string? AlbumTitle { get; }
    public string? Title { get; }

    public Task<Stream?> GetThumbnailStream(CancellationToken cancellationToken);
}