using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Org.Grush.EchoWorkDisplay.Common;
using SkiaSharp;

namespace Org.Grush.EchoWorkDisplay;

public class ScreenRenderer(Config config)
{
    public const double ItalicsAngle = 10;
    public const double ItalicsRadians = ItalicsAngle * Math.PI / 180;
    public static readonly double ItalicsTangent = Math.Tan(ItalicsRadians);
    
    private static readonly ConcurrentDictionary<string, SKTypeface?> KnownFontFamilies = [];
    private static readonly ConcurrentDictionary<string, string> ResolvedCommaSeparatedFontFamilies = [];

    private readonly IconDrawer _iconDrawer = new(
        thumbSize: config._FeasibleToDrawThumbnail
            ? Math.Min(config.MaxThumbnailHeight, config.MaxThumbnailWidth)
            : 100
    );


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
    public async Task<SKBitmap?> RenderPresenceScreen(
        MicrosoftPresenceService.PresenceDescription presenceDescription,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        
        SKColor backgroundColor = config._BackgroundColor;
        SKColor textColor = config._TextColor;
        SKColor? textStrokeColor;
        
        SKBitmap? thumbnail = _iconDrawer.GetIcon(presenceDescription.Availability);

        if (thumbnail is null)
            return null;
        
        var screenBitmap = new SKBitmap(width, height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var screenCanvas = new SKCanvas(screenBitmap);
        
        screenCanvas.Clear(backgroundColor);
        screenCanvas.Flush();
        
        screenCanvas.DrawBitmap(thumbnail, new SKPoint(config.MarginSize, config.MarginSize));

        if (_iconDrawer.ThumbSize * 2 <= config.ScreenHardwareHeight)
        {
            SKRectI statusRectangle = new(
                left: (int)(2 * config.MarginSize + _iconDrawer.ThumbSize),
                top: config.MarginSize,
                right: config.MarginSize,
                bottom: config.MarginSize + (int)_iconDrawer.ThumbSize
            );
            var status = _iconDrawer.GetAvailabilityName(presenceDescription.Availability!.Value);
            
            SKTypeface typeface = FindFontFamily(config.FontFamilies);
            using SKPaint mediaTextPaint = new()
            {
                Color = textColor,
            };
            await DrawTextLinesCentered(
                screenCanvas,
                status,
                mediaTextPaint,
                typeface,
                statusRectangle,
                bold: true
            );

            string? subtextMessage =
                presenceDescription.OutOfOfficeMessage
                    ?? presenceDescription.StatusMessage
                ;
            
            if (subtextMessage is not null)
            {
                SKRectI bottomRectangle = new(
                    left: config.MarginSize,
                    top: statusRectangle.Left,
                    right: config.MarginSize,
                    bottom: config.MarginSize
                );

                await DrawTextLinesCentered(screenCanvas, subtextMessage, mediaTextPaint, typeface, bottomRectangle);
            }
        }

        screenCanvas.Flush();
        return screenBitmap;
    }
    
    [MustDisposeResource]
    public async Task<SKBitmap> RenderMediaScreen(
        IMediaProperties mediaProperties,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        SKColor backgroundColor = config._BackgroundColor;
        SKColor textColor = config._TextColor;
        SKColor? textStrokeColor;
        
        var screenBitmap = new SKBitmap(width, height, SKColorType.Rgb565, SKAlphaType.Opaque);
        using var screenCanvas = new SKCanvas(screenBitmap);
        
        using SKImage? thumbImage = config._FeasibleToDrawThumbnail
            ? null
            : await ReadThumbnail(mediaProperties, cancellationToken);
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
            await DrawTextLinesCentered(screenCanvas, mediaProperties.Artist, mediaTextPaint, typeface, artistRectangle, bold: true);

            // draw media title
            await DrawTextLinesCentered(screenCanvas, mediaProperties.Artist, mediaTextPaint, typeface, titleRectangle, italic: true);
        }
        
        screenCanvas.Flush();
        
        return screenBitmap;
    }

    private async Task<bool> DrawTextLinesCentered(
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
        IMediaProperties mediaProps,
        CancellationToken cancellationToken
    )
    {
        var thumbStream = await mediaProps.GetThumbnailStream(cancellationToken);
        
        if (thumbStream is null)
            return null;
        
        return SKImage.FromEncodedData(thumbStream);
    }
}

public class IconDrawer
{
    private static readonly Dictionary<PresenceAvailability, string> _nameCache = [];
    
    private readonly SKPoint _center;
    private readonly float _lineWidth;
    private readonly SKPaint _linePaint;
    
    public readonly Lazy<SKBitmap> Available;
    public readonly Lazy<SKBitmap> Away;
    public readonly Lazy<SKBitmap> Busy;
    public readonly Lazy<SKBitmap> DoNotDisturb;
    public readonly Lazy<SKBitmap> Offline;

    public readonly float ThumbSize;

    public IconDrawer(float thumbSize)
    {
        float _20 = thumbSize * 0.20f;
        float _35 = thumbSize * 0.35f;
        float _45 = thumbSize * 0.45f;
        float _50 = thumbSize * 0.5f;
        float _55 = thumbSize - _45;
        float _65 = thumbSize - _35;
        float _70 = thumbSize * 0.70f;
        float _80 = thumbSize - _20;

        float _1_3 = thumbSize / 3f;
        float _2_3 = 2 * _1_3;

        ThumbSize = thumbSize;
        
        _center = new SKPoint(_50, _50);
        _lineWidth = thumbSize * 0.1f;
        _linePaint = new()
        {
            Color = SKColors.White,
            StrokeWidth = _lineWidth,
            IsStroke = true,
            StrokeJoin = SKStrokeJoin.Miter,
            StrokeCap = SKStrokeCap.Butt,
        };
        
        Available = new(() =>
        {
            SKBitmap availableBitmap = new((int)thumbSize, (int)thumbSize);
            using SKCanvas canvas = new(availableBitmap);
            
            DrawFullCircle(canvas, new(0, 0xFF, 0));

            using SKPath checkPath = new();
            checkPath.MoveTo(_65, _45);
            checkPath.LineTo(_55, _65);
            checkPath.LineTo(_35, _35);
            
            canvas.DrawPath(
                checkPath,
                _linePaint
            );
            
            canvas.Flush();
            return availableBitmap;
        });

        Away = new(() =>
        {
            SKBitmap awayBitmap = new((int)thumbSize, (int)thumbSize);
            using SKCanvas canvas = new(awayBitmap);

            DrawFullCircle(canvas, new(0xF8, 0xD6, 0x21));
            
            using SKPath clockPath = new();
            clockPath.MoveTo(_45, _20);
            clockPath.LineTo(_45, _55);
            clockPath.LineTo(_70, _70);
            
            canvas.DrawPath(
                clockPath,
                _linePaint
            );
            
            canvas.Flush();
            return awayBitmap;
        });

        Busy = new(() =>
        {
            SKBitmap busyBitmap = new((int)thumbSize, (int)thumbSize);
            using SKCanvas canvas = new(busyBitmap);

            DrawFullCircle(canvas, new(0xFF, 0, 0));
            
            canvas.Flush();
            return busyBitmap;
        });

        DoNotDisturb = new(() =>
        {
            SKBitmap dndBitmap = new((int)thumbSize, (int)thumbSize);
            using SKCanvas canvas = new(dndBitmap);

            DrawFullCircle(canvas, new(0xFF, 0, 0));

            canvas.DrawLine(
                new(_20, _50),
                new(_80, _50),
                _linePaint
            );

            canvas.Flush();
            return dndBitmap;
        });
        
        
        Offline = new(() =>
        {
            SKBitmap dndBitmap = new((int)thumbSize, (int)thumbSize);
            using SKCanvas canvas = new(dndBitmap);
            
            canvas.DrawCircle(
                c: _center,
                radius: _center.X - (_lineWidth / 2),
                paint: _linePaint
            );

            canvas.DrawLine(
                new(_1_3, _1_3),
                new(_2_3, _2_3),
                _linePaint
            );
            canvas.DrawLine(
                new(_1_3, _2_3),
                new(_2_3, _1_3),
                _linePaint
            );

            canvas.Flush();
            return dndBitmap;
        });
    }
    
    public SKBitmap? GetIcon(PresenceAvailability? availability)
        => availability switch
        {
            PresenceAvailability.Available
                => Available.Value,
            PresenceAvailability.Away or
            PresenceAvailability.BeRightBack
                => Away.Value,
            PresenceAvailability.Busy or
            PresenceAvailability.InAMeeting or
            PresenceAvailability.InACall
                => Busy.Value,
            PresenceAvailability.DoNotDisturb or
            PresenceAvailability.Presenting or
            PresenceAvailability.Focusing
                => DoNotDisturb.Value,
            PresenceAvailability.Offline
                => Offline.Value,
            _ 
                => null,
        };

    public string GetAvailabilityName(PresenceAvailability availability)
    {
        if (_nameCache.TryGetValue(availability, out var name))
            return name;

        var pascalName = availability.ToString();
        if (!Enum.IsDefined(availability))
        {
            _nameCache.Add(availability, pascalName);
            return pascalName;
        }

        name = _pascalReplacer.Replace(pascalName, match => $" {match.Value}");
        _nameCache.Add(availability, name);
        return name;
    }

    private readonly Regex _pascalReplacer = new("(?<=[a-z])([A-Z])");

    private void DrawFullCircle(SKCanvas canvas, SKColor color)
        => canvas.DrawCircle(_center, _center.X, new SKPaint { Color = color, IsStroke = false });
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