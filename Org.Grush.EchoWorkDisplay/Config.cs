using System.Text.Json;

namespace Org.Grush.EchoWorkDisplay;

public record Config(
    int BaudRate = 115200,
    int ComPortSearchDelayMilliseconds = 1_000
)
{
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
        
    }
}
