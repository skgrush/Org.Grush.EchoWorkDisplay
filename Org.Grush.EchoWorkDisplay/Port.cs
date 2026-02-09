using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Org.Grush.EchoWorkDisplay;

public static class ControlBytes
{
    public const char StartOfHeading = '\u0001';
    public const char StartOfText = '\u0002';
    public const char EndOfText = '\u0003';
    public const char EndOfTransmission = '\u0004';
    public const char Enquiry = '\u0005';
    public const char Acknowledge = '\u0006';
    public const char ShiftOut = '\u000E';
    public const char ShiftIn = '\u000F';
    public const char Cancel = '\u0018';
}

public sealed class Port : IAsyncDisposable
{
    private SerialPort? SerialPort { get; set; }
    public EnumeratedSerialPort PortInfo { get; }

    public bool IsOpen => SerialPort?.IsOpen ?? false;
    public bool IsDisposed => SerialPort is null;

    public Port(SerialPort serialPort, EnumeratedSerialPort portInfo)
    {
        SerialPort = serialPort;
        PortInfo = portInfo;

        SerialPort.Disposed += _SerialPortOnDisposed;
    }

    public async Task<bool> OpenAsync()
    {
        if (SerialPort is null)
            return false;
        if (SerialPort.IsOpen)
            return true;

        try
        {
            SerialPort.Open();
            return true;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private async IAsyncEnumerable<RawMessage> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ProtocolEncoder encoder = new();
        
        while (SerialPort is { IsOpen: true })
        {
            yield return await encoder.ReadMessageAsync(SerialPort.BaseStream, cancellationToken);
        }
    }

    public async Task<long> WriteMessagesAsync(Queue<RawMessage> messages, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!IsOpen)
            throw new PortException("Port closed");
        return await WriteMessagesAsync(SerialPort!.BaseStream, messages, cancellationToken);
    }

    private async Task<long> WriteMessagesAsync(Stream stream, Queue<RawMessage> messages, CancellationToken cancellationToken)
    {
        ProtocolEncoder encoder = new();

        long bytesWritten = 0;
        
        while (messages.Count is not 0)
        {
            // don't cancel midway through a message, wait until between messages
            cancellationToken.ThrowIfCancellationRequested();

            var nextMessage = messages.Peek();
            bytesWritten += await encoder.WriteMessageAsync(stream, nextMessage);
            if (messages.Dequeue() != nextMessage)
                throw new InvalidOperationException("dequeued message doesn't equal previously peeked message");
        }
        
        return bytesWritten;
    }
    
    
    public class ProtocolEncoder
    {
        const int sizeof_typeOfTransmission = 32;
        private const int sizeof_hexMessageId = 8;
        const int sizeof_transmissionSize = 8;

        public async Task<long> WriteMessageAsync(Stream stream, RawMessage message)
        {

            int size;
            ReadOnlyMemory<byte> subsequentMessage;
            byte openingChar;
            byte closingChar;

            if (message is RawMessageText messageText)
            {
                size = messageText.Bytes.Length + 1;

                subsequentMessage = messageText.Bytes;
                openingChar = (byte)messageText.ControlCharStart;
                closingChar = (byte)ControlBytes.EndOfText;
            }
            else if (message is RawMessageBinary messageBinary)
            {
                size = sizeof_typeOfTransmission + 1 + messageBinary.Bytes.Length + 1;

                byte[] msg = new byte[size - 1];
                Encoding.UTF8.GetBytes(
                    bytes: msg.AsSpan(0, sizeof_typeOfTransmission),
                    chars: messageBinary.TypeOfTransmission
                );
                msg[sizeof_typeOfTransmission] = 0;
                
                messageBinary.Bytes.Span.CopyTo(msg.AsSpan(sizeof_typeOfTransmission + 2, messageBinary.Bytes.Length));

                subsequentMessage = msg;
                openingChar = (byte)ControlBytes.ShiftOut;
                closingChar = (byte)ControlBytes.ShiftIn;
            }
            else
            {
                throw new(); // impo
            }
            
            byte[] messageBuffer = new byte[1 + sizeof_hexMessageId + 1 + sizeof_transmissionSize + 1];

            int bytesWritten = 0;
            messageBuffer[bytesWritten] = (byte)ControlBytes.StartOfHeading;
            bytesWritten += 1;
            
            Encoding.ASCII.GetBytes(
                bytes: messageBuffer.AsSpan(bytesWritten, 8),
                chars: message.MessageId.ToString("X8")
            );
            bytesWritten += 8;

            messageBuffer[bytesWritten] = 0;
            bytesWritten += 1;
            
            Encoding.ASCII.GetBytes(
                bytes: messageBuffer.AsSpan(bytesWritten),
                chars: size.ToString("D8")
            );
            bytesWritten += sizeof_hexMessageId;
            
            messageBuffer[bytesWritten] = openingChar;
            bytesWritten += 1;

            await stream.WriteAsync(messageBuffer);
            
            await stream.WriteAsync(subsequentMessage);
            bytesWritten += subsequentMessage.Length;
            await stream.WriteAsync(new[]{ closingChar });
            bytesWritten += 1;

            await stream.FlushAsync();

            return bytesWritten;
        }
        
        public async Task<RawMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] transmissionInitialBuffer = new byte[1 + sizeof_hexMessageId + 1 + sizeof_transmissionSize + 1];
            
            long lastPos = stream.Position;
            
            // new transmission
            //      byte 0: ␁
            //      bytes 1-8: hex message ID
            //      byte 9: \0
            //      bytes 10-17: ascii-numeric length
            //      byte 18: ␂ or ␎
            await stream.ReadExactlyAsync(transmissionInitialBuffer, cancellationToken);
            
            PortUnexpectedCharException.ThrowIfNot(lastPos, ControlBytes.StartOfHeading, transmissionInitialBuffer[0]);

            uint messageId = uint.Parse(transmissionInitialBuffer.AsSpan(start: 1, length: sizeof_hexMessageId), NumberStyles.AllowHexSpecifier);
            int transmissionSize = int.Parse(transmissionInitialBuffer.AsSpan(start: 2 + sizeof_hexMessageId, length: sizeof_transmissionSize), NumberStyles.None);

            char transmissionTypeChar = (char)transmissionInitialBuffer[^1];
            switch (transmissionTypeChar)
            {
                case RawMessageJson.ControlCharStart or RawMessageAck.ControlCharStart or RawMessageEnq.ControlCharStart:
                {
                    byte[] jsonBufferPlusTwoControlChars = new byte[transmissionSize + 2];
                    await stream.ReadExactlyAsync(jsonBufferPlusTwoControlChars, cancellationToken);
                
                    PortUnexpectedCharException.ThrowIfNot(stream.Position - 2, ControlBytes.EndOfText, jsonBufferPlusTwoControlChars[^2]);
                    PortUnexpectedCharException.ThrowIfNot(stream.Position - 1, ControlBytes.EndOfTransmission, jsonBufferPlusTwoControlChars[^1]);

                    ReadOnlyMemory<byte> msgBytesMemory = jsonBufferPlusTwoControlChars.AsMemory(..^2);

                    return transmissionTypeChar switch
                    {
                        RawMessageJson.ControlCharStart => new RawMessageJson(messageId, transmissionSize, msgBytesMemory),
                        RawMessageAck.ControlCharStart => new RawMessageAck(messageId, transmissionSize, msgBytesMemory),
                        RawMessageEnq.ControlCharStart => new RawMessageEnq(messageId, transmissionSize, msgBytesMemory),
                        _ => throw new(), // unpozibl
                    };
                    break;
                }
                case RawMessageBinary.ControlCharStart:
                {
                    byte[] typeOfTransmissionPlusNull = new byte[sizeof_typeOfTransmission + 1];
                    await stream.ReadExactlyAsync(typeOfTransmissionPlusNull, cancellationToken);
                
                    string typeOfTransmission = Encoding.UTF8.GetString(typeOfTransmissionPlusNull, 0, sizeof_typeOfTransmission);

                    int bodySize = transmissionSize - typeOfTransmissionPlusNull.Length - 1;

                    byte[] bodyBufferPlusTwoControlChars = new byte[bodySize + 2];

                    await stream.ReadExactlyAsync(bodyBufferPlusTwoControlChars, cancellationToken);

                    PortUnexpectedCharException.ThrowIfNot(stream.Position - 2, ControlBytes.ShiftIn, bodyBufferPlusTwoControlChars[^2]);
                    PortUnexpectedCharException.ThrowIfNot(stream.Position - 1, ControlBytes.EndOfTransmission, bodyBufferPlusTwoControlChars[^1]);
                
                    return new RawMessageBinary(
                        messageId,
                        transmissionSize,
                        bodySize,
                        typeOfTransmission,
                        bodyBufferPlusTwoControlChars.AsMemory(..^2)
                    );
                    break;
                }
                default:
                {
                    throw new PortUnexpectedCharException(stream.Position - 1, (byte)ControlBytes.StartOfText, transmissionInitialBuffer[^1]);
                }
            }
        }
    }

    public abstract record RawMessage(uint MessageId, int MessageSize)
    {
        internal static uint IncrementalSenderMessageId
        {
            get;
            set
            {
                if (value <= field)
                    throw new ArgumentOutOfRangeException();
                field = value;
            }
        }
    }

    public abstract record RawMessageText(uint MessageId, int MessageSize, ReadOnlyMemory<byte> Bytes)
        : RawMessage(MessageId, MessageSize)
    {
        public char ControlCharStart => (char)GetType().GetField("ControlCharStart", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetRawConstantValue();
    }

    public record RawMessageJson(uint MessageId, int MessageSize, ReadOnlyMemory<byte> Bytes) : RawMessageText(MessageId, MessageSize, Bytes)
    {
        public const char ControlCharStart = ControlBytes.StartOfText;

        public static RawMessageJson FromJson<T>(T obj, JsonTypeInfo<T> jsonTypeInfo)
        {
            var messageJsonBytes = JsonSerializer.SerializeToUtf8Bytes<T>(obj, jsonTypeInfo);

            return new(
                MessageId: ++IncrementalSenderMessageId,
                MessageSize: messageJsonBytes.Length,
                Bytes: messageJsonBytes
            );
        }
    }

    public record RawMessageBinary(uint MessageId, int MessageSize, int BodySize, [Length(0, 32)] string TypeOfTransmission, ReadOnlyMemory<byte> Bytes) : RawMessage(MessageId, MessageSize)
    {
        public const char ControlCharStart = ControlBytes.ShiftOut;

        public static RawMessageBinary FromBytes(string typeOfTransmission, ReadOnlyMemory<byte> bytes)
        {
            int bodySize = bytes.Length;
            int messageSize = 32 + 1 + bodySize + 1;

            byte[] typeOfTransmissionBytes = new byte[32];

            Encoding.UTF8.GetBytes(typeOfTransmission, typeOfTransmissionBytes);
            
            return new(
                MessageId: ++IncrementalSenderMessageId,
                MessageSize: messageSize,
                BodySize: bodySize,
                TypeOfTransmission: typeOfTransmission,
                Bytes: bytes
            );
        }
    }

    public record RawMessageAck(uint MessageId, int MessageSize, ReadOnlyMemory<byte> Bytes) : RawMessageText(MessageId, MessageSize, Bytes)
    {
        public const char ControlCharStart = ControlBytes.Acknowledge;
    }

    public record RawMessageEnq(uint MessageId, int MessageSize, ReadOnlyMemory<byte> Bytes) : RawMessageText(MessageId, MessageSize, Bytes)
    {
        public const char ControlCharStart = ControlBytes.Enquiry;
    }

    
    public async ValueTask DisposeAsync()
    {
        var serialPort = SerialPort;
        if (serialPort is null)
            return;

        SerialPort = null;
        serialPort.Disposed -= _SerialPortOnDisposed;
        try
        {
            serialPort.Close();
        }
        catch
        {
        }
        serialPort.Dispose();
    }

    private async void _SerialPortOnDisposed(object? sender, EventArgs e)
    {
        await DisposeAsync();
    }
    
    
    public class PortException(string message) : InvalidOperationException(message);
    public class PortClosedException(string message) : PortException(message);
    public class PortUnexpectedCharException(long position, byte expected, byte actual)
        : PortException($"Unexpected byte at position {position}; expected '{expected}' but got '{actual}'.")
    {
        public long Position { get; } = position;
        public byte Expected { get; } = expected;
        public byte Actual { get; } = actual;

        public static void ThrowIfNot(long position, byte expected, byte actual)
        {
            if (expected != actual)
                throw new PortUnexpectedCharException(position, expected, actual);
        }
        public static void ThrowIfNot(long position, char expected, byte actual)
            => ThrowIfNot(position, (byte)expected, actual);
    }
}