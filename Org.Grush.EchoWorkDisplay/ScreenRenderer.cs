using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Media.Control;
using Windows.Storage.Streams;
using JetBrains.Annotations;
using SkiaSharp;
using Buffer = Windows.Storage.Streams.Buffer;

namespace Org.Grush.EchoWorkDisplay;

public class ScreenRenderer(Config config)
{
    public const double ItalicsAngle = 10;
    public const double ItalicsRadians = ItalicsAngle * Math.PI / 180;
    public static readonly double ItalicsTangent = Math.Tan(ItalicsRadians);
    
    private static readonly ConcurrentDictionary<string, SKTypeface?> KnownFontFamilies = [];
    private static readonly ConcurrentDictionary<string, string> ResolvedCommaSeparatedFontFamilies = [];

    private static SKTypeface FindFontFamily(string commaSeparatedFontFamilies)
    {
        if (ResolvedCommaSeparatedFontFamilies.TryGetValue(commaSeparatedFontFamilies, out var foundPreresolved))
        {
            return KnownFontFamilies.GetValueOrDefault(foundPreresolved) ?? SKTypeface.Default;
        }
        
        foreach (var fontFamName in commaSeparatedFontFamilies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (KnownFontFamilies.TryGetValue(fontFamName, out var fontFamily))
            {
                if (fontFamily is not null)
                {
                    ResolvedCommaSeparatedFontFamilies[commaSeparatedFontFamilies] = fontFamName;
                    return fontFamily;
                }
            }
            else
            {
                try
                {
                    var fam = SKTypeface.FromFamilyName(fontFamName);
                    KnownFontFamilies[fontFamName] = fam;
                    if (fam is not null)
                    {
                        ResolvedCommaSeparatedFontFamilies[commaSeparatedFontFamilies] = fontFamName;
                        return fam;
                    }
                }
                catch
                {
                    KnownFontFamilies[fontFamName] = null;
                }
            }
        }

        string defaultName = SKTypeface.Default.FamilyName;
        KnownFontFamilies[defaultName] = SKTypeface.Default;
        ResolvedCommaSeparatedFontFamilies[commaSeparatedFontFamilies] = defaultName;
        return SKTypeface.Default;
    }
    
    [MustDisposeResource]
    public async Task<SKBitmap> RenderMediaScreen(
        GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        SKColor backgroundColor = SKColors.White;
        SKColor textColor = SKColors.Black;
        SKColor? textStrokeColor;
        
        var screenBitmap = new SKBitmap(width, height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var screenCanvas = new SKCanvas(screenBitmap);
        
        using SKImage? thumbImage = config._FeasibleToDrawThumbnail
            ? null
            : await ReadThumbnail(mediaProperties.Thumbnail, cancellationToken);
        if (thumbImage is not null)
        {
            // TODO: recalculate bg and fg colors
        }
        
        screenCanvas.Clear(backgroundColor);
        screenCanvas.Flush();

        SKRectI? thumbnailDrawnRect = null;
        
        if (thumbImage is not null)
        {
            SKRectI thumbnailDestinationRectangle = new(
                left: config.MarginSize,
                top: config.MarginSize,
                right: config.MarginSize + config.MaxThumbnailWidth,
                bottom: config.MarginSize + config.MaxThumbnailHeight
            );

            thumbnailDrawnRect = await DrawThumbnailToBitmap(
                thumbImage,
                destinationBitmap: screenBitmap,
                destinationRectangle: thumbnailDestinationRectangle,
                cancellationToken
            );
        }
        
        // TODO: currently assuming thumbnail is always drawn, maybe adjust if not
        SKRectI artistRectangle = new(
            config.MaxThumbnailWidth + 2 * config.MarginSize,
            config.MarginSize,
            config.MarginSize,
            config.MaxThumbnailHeight
        );
        SKRectI titleRectangle = new(
            config.MarginSize,
            config.MaxThumbnailHeight + 2 * config.MarginSize,
            config.MarginSize,
            config.MarginSize
        );
        
        if (config._FeasibleToDrawText)
        {
            SKTypeface typeface = FindFontFamily(config.FontFamilies);
            
            using var mediaTextPaint = new SKPaint();
            mediaTextPaint.Color = textColor;
            
            // draw artist name
            await DrawText(screenCanvas, mediaProperties.Artist, mediaTextPaint, typeface, artistRectangle, bold: true);

            // draw media title
            await DrawText(screenCanvas, mediaProperties.Artist, mediaTextPaint, typeface, titleRectangle, italic: true);
        }
        
        screenCanvas.Flush();
        
        return screenBitmap;
    }

    private async Task<bool> DrawText(
        SKCanvas canvas,
        string text,
        SKPaint textPaint,
        SKTypeface typeface,
        SKRectI destinationRectangle,
        bool bold = false,
        bool italic = false
    )
    {
        using var font = new SKFont(
            typeface: typeface,
            size: config.FontSize,
            skewX: italic
                ? (float)(config.FontSize * ItalicsTangent) // y * tan(θ) = x
                : 0
        )
        {
            Embolden = bold,
        };
        
        int feasibleLines = (int)Math.Ceiling(destinationRectangle.Height / config.FontSize);

        var lines = BreakText(font, text, destinationRectangle.Width).Take(feasibleLines).ToArray();

        var heightOfLines = lines.Length * config.FontSize;

        var lineStartY = destinationRectangle.MidY - heightOfLines / 2;
        SKPoint lineStart = new(destinationRectangle.Top, lineStartY);
        
        foreach (var line in lines)
        {
            canvas.DrawText(line.ToString(), lineStart, SKTextAlign.Left, font, textPaint);
            lineStart.Offset(0, font.Size);
        }

        return true;
    }

    public readonly record struct TextBreak(string OriginalText, Range Range, bool Hyphenate)
    {
        public ReadOnlySpan<char> AsSpan() => OriginalText.AsSpan(Range);
        public static implicit operator ReadOnlySpan<char>(TextBreak b) => b.AsSpan();
        public override string ToString()
        {
            var s = OriginalText[Range];
            if (Hyphenate)
                return s + "-";
            else
                return s;
        }
    }

    public static IEnumerable<TextBreak> BreakText(SKFont font, string text, int maxWidth)
    {
        
        int idx = 0;
        while (idx < text.Length)
        {
            while (char.IsWhiteSpace(text[idx]))
                ++idx;
            
            var remaining = text.AsSpan(idx);
            int count = font.BreakText(remaining, maxWidth: maxWidth);
            if (remaining.Length == count)
            {
                // includes rest of text
                TextBreak b = new(text, idx.., false);
                yield return b;
                idx += count;
            }
            else
            {
                var captureCharactersUpToLastWhitespace = new Regex(@"^(.*)[-\s-\u00A0]+[^-\s-\u00A0]*$", RegexOptions.RightToLeft | RegexOptions.Singleline);
                var matches = captureCharactersUpToLastWhitespace.EnumerateMatches(text, idx);
                if (matches.MoveNext())
                {
                    // found the last breakable whitespace
                    var matchedCount = matches.Current.Length;
                    var nextChar = text[idx + matchedCount];
                    var length = nextChar is '-' ? matchedCount + 1 : matchedCount;
                    TextBreak b = new(text, idx..(length + idx), false);
                    yield return b;
                    idx += length;
                }
                else
                {
                    // did NOT find a breakable whitespace or hyphen; insert our own hyphen
                    TextBreak b = new(text, idx..(count + idx - 1), true);
                    yield return b;
                    idx += count - 1;
                }
            }
        }
    }

    private async Task<SKRectI?> DrawThumbnailToBitmap(
        SKImage thumbImage,
        SKBitmap destinationBitmap,
        SKRectI destinationRectangle,
        CancellationToken cancellationToken
    )
    {

        SKSizeI targetSize;
        bool widthFits = destinationRectangle.Width >= thumbImage.Width;
        bool heightFits = destinationRectangle.Height >= thumbImage.Height;

        if (!widthFits && !heightFits)
        {
            var boundaryAspectRatio = (double)destinationRectangle.Width / destinationRectangle.Height;
            var imageAspectRatio = (double)thumbImage.Width / thumbImage.Height;
            
            if (boundaryAspectRatio > imageAspectRatio)
            {
                // the boundary is really wide, the image is tall
                widthFits = true;
            }
            else
            {
                heightFits = true;
            }
        }
        
        if (widthFits && heightFits)
        {
            targetSize = thumbImage.Info.Size;
        }
        else if (widthFits && !heightFits)
        {
            var heightScale = (double)destinationRectangle.Height / thumbImage.Height;
            targetSize = new((int)(thumbImage.Width * heightScale), destinationRectangle.Height);
        }
        else if (!widthFits && heightFits)
        {
            var widthScale = (double)destinationRectangle.Width / thumbImage.Width;
            targetSize = new(destinationRectangle.Width, (int)(thumbImage.Height * widthScale));
        }
        else
        {
            throw new("Impo");
        }

        SKRect srcRect = thumbImage.Info.Rect;
        SKRect destRect = SKRect.Create(destinationRectangle.Location, targetSize);

        using SKCanvas canvas = new(destinationBitmap);

        canvas.DrawImage(thumbImage, source: srcRect, dest: destRect);
        canvas.Flush();
        
        cancellationToken.ThrowIfCancellationRequested();

        return destinationRectangle;

        // using SKBitmap unscaledBitmap = new(
        //     width: thumbImage.Width,
        //     height: thumbImage.Height,
        //     colorType: destinationBitmap.ColorType,
        //     alphaType: destinationBitmap.AlphaType,
        //     colorspace: destinationBitmap.ColorSpace
        // );

        // using SKBitmap unscaledBitmap = SKBitmap.FromImage(thumbImage);
        //
        // SKSamplingOptions scalingOptions = new(maxAniso: 8);
        //
        // if (point == SKPointI.Empty && targetSize == destinationBitmap.Info.Size)
        // {
        //     bool didScaleDirectly = unscaledBitmap.ScalePixels(
        //         destination: destinationBitmap,
        //         scalingOptions
        //     );
        //     return didScaleDirectly;
        // }
        //
        // using SKBitmap tmpScaledBitmap = new SKBitmap(
        //     width: targetSize.Width,
        //     height: targetSize.Height,
        //     colorType: thumbImage.ColorType,
        //     alphaType: thumbImage.AlphaType,
        //     colorspace: thumbImage.ColorSpace
        // );
        //
        // bool didScale = unscaledBitmap.ScalePixels(
        //     destination: tmpScaledBitmap,
        //     scalingOptions
        // );
        // if (!didScale) return false;



    }

    [MustDisposeResource]
    public async Task<SKImage?> ReadThumbnail(
        IRandomAccessStreamReference? thumb,
        CancellationToken cancellationToken
    )
    {
        if (thumb is null)
            return null;
        
        using var t = await thumb.OpenReadAsync();
        if (t.Size < 16 || !t.CanRead)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        
        Buffer buffer = new Buffer((uint)t.Size);

        var asyncOperation = t.ReadAsync(buffer, (uint)t.Size, InputStreamOptions.None);
        cancellationToken.Register(() => asyncOperation.Cancel());
        await asyncOperation;
        
        return SKImage.FromEncodedData(buffer.AsStream());
    }
}

internal static class CharExtensions
{
    extension(char c)
    {
        public bool IsBreakableWhitespace()
            => c is not '\u00A0' && char.IsWhiteSpace(c);

        public bool IsBreakableDash()
            => c is not '\u2011' && CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.DashPunctuation;
    }
}