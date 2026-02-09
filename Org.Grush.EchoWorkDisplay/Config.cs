using System.Text.Json;

namespace Org.Grush.EchoWorkDisplay;

public record Config(
    int BaudRate = 115200,
    int ComPortSearchDelayMilliseconds = 1_000,
    int MaxThumbnailWidth = 120,
    int MaxThumbnailHeight = 120,
    int ScreenHardwareWidth = 320,
    int ScreenHardwareHeight = 240,
    int MarginSize = 5,
    float FontSize = 24,
    string FontFamilies = "Arial, Arial Regular, Courier New, Courier New Regular"
)
{
    public bool _FeasibleToDrawThumbnail =>
        this is { MaxThumbnailHeight: > 20, MaxThumbnailWidth: > 20 };

    public bool _FeasibleToDrawText =>
        this is { FontSize: > 5 };
    
    public static Config Deserialize(Stream stream)
        => JsonSerializer.Deserialize(stream, LocalJsonSerializerContext.Default.Config)!;

    public static Config? Deserialize(FileInfo file)
    {
        if (!file.Exists)
            return null;

        using var stream = file.OpenRead();
        
        return Deserialize(stream);
    }

    private void Validate()
    {
        if (BaudRate is <= 100 or > 1_000_000)
            throw new ArgumentException($"Invalid BaudRate: {BaudRate}");
        
        if (ComPortSearchDelayMilliseconds is <= 100 or > 10_000)
            throw new ArgumentException($"Invalid ComPortSearchDelayMilliseconds: {ComPortSearchDelayMilliseconds}");
        
        if (MaxThumbnailHeight < 0 || MaxThumbnailHeight > ScreenHardwareHeight)
            throw new ArgumentException($"Invalid MaxThumbnailHeight: {MaxThumbnailHeight}");
        if (MaxThumbnailWidth < 0 || MaxThumbnailWidth > ScreenHardwareWidth)
            throw new ArgumentException($"Invalid MaxThumbnailWidth: {MaxThumbnailWidth}");
        
        if (MarginSize is < 0 or > 30)
            throw new ArgumentException($"Invalid MarginSize: {MarginSize}");

        if (FontSize < 0 || FontSize > ScreenHardwareHeight)
            throw new ArgumentException($"Invalid FontSize: {FontSize}");
    }
}
