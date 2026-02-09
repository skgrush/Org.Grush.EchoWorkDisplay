using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Org.Grush.EchoWorkDisplay;

public static class PiPicoMessages
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$MessageType")]
    [JsonDerivedType(typeof(NoMediaMessage), nameof(NoMediaMessage))]
    // [JsonDerivedType(typeof(MediaMetadataChanged), nameof(MediaMetadataChanged))]
    public abstract record JsonBase()
    {
        [JsonPropertyName("$MessageType"), JsonPropertyOrder(-1)]
        public abstract string MessageType { get; }
        
        public abstract Port.RawMessage ToRawMessage();
    }

    public sealed record DrawBitmap(
        SkiaSharp.SKBitmap Bitmap,
        SkiaSharp.SKPointI Position
    )
    {
        public string TypeOfTransmission => "bitmap?version=1";

        /// <summary>
        /// Byte structure:
        ///  - 00..15 [16B] = Skia SKColorType ASCII string, with trailing NUL bytes.
        ///  - 16..17  [2B] = big-endian X position
        ///  - 18..19  [2B] = big-endian Y position
        ///  - 20..21  [2B] = big-endian width
        ///  - 22..23  [2B] = big-endian height
        ///  - 24..31  [8B] = RESERVED
        /// </summary>
        public ReadOnlyMemory<byte> GetMessageHeader()
        {
            SKColorType colorType = Bitmap.ColorType;
            if (!Enum.IsDefined(colorType) || colorType is SKColorType.Unknown)
                throw new InvalidEnumArgumentException(nameof(colorType), (int)colorType, typeof(SKColorType));
            
            byte[] header = new byte[32];

            Encoding.UTF8.GetBytes(
                bytes: header.AsSpan(0, 16),
                chars: colorType.ToString()
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                destination: header.AsSpan(16, 2),
                value: (UInt16)Position.X
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                destination: header.AsSpan(18, 2),
                value: (UInt16)Position.Y
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                destination: header.AsSpan(20, 2),
                value: (UInt16)Bitmap.Width
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                destination: header.AsSpan(22, 2),
                value: (UInt16)Bitmap.Height
            );

            return header;
        }

        public Port.RawMessageBinary ToRawMessage()
        {
            byte[] body = new byte[32 + Bitmap.ByteCount];
            
            GetMessageHeader().CopyTo(body);
            Bitmap.GetPixelSpan().CopyTo(body.AsSpan(32));
            
            return Port.RawMessageBinary.FromBytes(TypeOfTransmission, body);
        }
    }

    // public sealed record MediaMetadataChanged(
    //     string Artist,
    //     string Title,
    //     bool ResetMediaImage = true
    // ) : Base
    // {
    //     public override string MessageType => nameof(MediaMetadataChanged);
    //
    //     public override Port.RawMessage ToRawMessage()
    //         => Port.RawMessageJson.FromJson(this, LocalJsonSerializerContext.Default.MediaMetadataChanged);
    // }

        
    public sealed record NoMediaMessage() : JsonBase
    {
        public override string MessageType => nameof(NoMediaMessage);

        public override Port.RawMessageJson ToRawMessage()
            => Port.RawMessageJson.FromJson(this, LocalJsonSerializerContext.Default.NoMediaMessage);
    }
}