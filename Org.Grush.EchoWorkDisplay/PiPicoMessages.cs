using System.Text.Json.Serialization;

namespace Org.Grush.EchoWorkDisplay;

public static class PiPicoMessages
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$MessageType")]
    [JsonDerivedType(typeof(NoMediaMessage), nameof(NoMediaMessage))]
    [JsonDerivedType(typeof(MediaMetadataChanged), nameof(MediaMetadataChanged))]
    public abstract record Base()
    {
        [JsonPropertyName("$MessageType"), JsonPropertyOrder(-1)]
        public abstract string MessageType { get; }
        
        public abstract Port.RawMessage ToRawMessage();
    }

    public sealed record MediaMetadataChanged(
        string Artist,
        string Title,
        bool ResetMediaImage = true
    ) : Base
    {
        public override string MessageType => nameof(MediaMetadataChanged);

        public override Port.RawMessage ToRawMessage()
            => Port.RawMessageJson.FromJson(this, LocalJsonSerializerContext.Default.MediaMetadataChanged);
    }

        
    public sealed record NoMediaMessage() : Base
    {
        public override string MessageType => nameof(NoMediaMessage);

        public override Port.RawMessageJson ToRawMessage()
            => Port.RawMessageJson.FromJson(this, LocalJsonSerializerContext.Default.NoMediaMessage);
    }
}