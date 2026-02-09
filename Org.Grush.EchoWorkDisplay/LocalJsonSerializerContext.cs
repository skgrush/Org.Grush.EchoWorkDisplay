using System.Text.Json.Serialization;

namespace Org.Grush.EchoWorkDisplay;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(EnumeratedSerialPort))]
[JsonSerializable(typeof(EnumeratedSerialPort[]))]
[JsonSerializable(typeof(PiPicoMessages.JsonBase))]
public partial class LocalJsonSerializerContext : JsonSerializerContext;
