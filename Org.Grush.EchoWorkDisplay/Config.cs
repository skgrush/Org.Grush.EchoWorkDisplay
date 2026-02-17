using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.Grush.EchoWorkDisplay.Common;
using SkiaSharp;

namespace Org.Grush.EchoWorkDisplay;

public record Config(
    int BaudRate = 115_200,
    int ComPortSearchDelayMilliseconds = 1_000,
    int MaxThumbnailWidth = 120,
    int MaxThumbnailHeight = 120,
    int ScreenHardwareWidth = 320,
    int ScreenHardwareHeight = 240,
    int MarginSize = 5,
    float FontSize = 24,
    string FontFamilies = "Arial, Arial Regular, Courier New, Courier New Regular",
    string BackgroundColor = "#000000",
    string FontColor = "#FFFFFF",
    bool ShowPlayingMedia = true,
    string AzTenantId = "common",
    string AzClientId = "", // TODO
    int AzRefreshPeriodMilliseconds = 10_000,
    ReadOnlyDictionary<string, float>? StatusPriority = null
)
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(1);
    
    internal bool _FeasibleToDrawThumbnail =>
        this is { MaxThumbnailHeight: > 20, MaxThumbnailWidth: > 20 };

    internal bool _FeasibleToDrawText =>
        this is { FontSize: > 5 };
    
    internal SKColor _BackgroundColor =>
        SKColor.TryParse(BackgroundColor, out var color) ? color : SKColors.Black;
    internal SKColor _TextColor =>
        SKColor.TryParse(FontColor, out var color) ? color : SKColors.White;

    internal float GetPriority(PresenceAvailability availability)
    {
        string availStr = availability.ToString();
        if (StatusPriority is not null && StatusPriority.TryGetValue(availStr, out var priority))
            return priority;

        if (DefaultStatusPriority.TryGetValue(availStr, out priority))
            return priority;
        
        return (float)PresenceAvailability.PresenceUnknown;
    }

    internal float GetPriority(IMediaProperties? mediaProperties)
    {
        if (mediaProperties is not null)
        {
            string mediaString = string.Join(", ", new[]
            {
                mediaProperties.Artist,
                mediaProperties.AlbumTitle,
                mediaProperties.Title
            });

            foreach (var (pattern, priority) in _EffectiveMusicPriorities)
            {
                if (pattern.IsMatch(mediaString))
                    return priority;
            }
        }
        
        return _EffectiveMusicPriorities.First(pair => pair.Pattern.ToString() is ".*" or "").Priority;
    }

    private readonly ImmutableArray<(Regex Pattern, float Priority)> _EffectiveMusicPriorities =
        [
            ..DefaultStatusPriority
                .Concat(StatusPriority ?? ReadOnlyDictionary<string, float>.Empty)
                .DistinctBy(pair => pair.Key)
                .SelectMany(pair =>
                {
                    (string key, float priority) = pair;
                    if (!key.StartsWith("music:", StringComparison.OrdinalIgnoreCase))
                        return Enumerable.Empty<(Regex, float)>();

                    string patternStr = key["music:".Length..];

                    return
                    [
                        (new Regex(patternStr, RegexOptions.IgnoreCase, RegexTimeout), priority)
                    ];
                })
        ];
    
    internal static readonly ImmutableDictionary<string, float> DefaultStatusPriority =
        Enum.GetValues<PresenceAvailability>()
            .Select(e => KeyValuePair.Create(e.ToString(), (float)e))
            .Append(KeyValuePair.Create("Music:.*", (float)PresenceAvailability.Busy - 0.5f))
            .ToImmutableDictionary();
    
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
