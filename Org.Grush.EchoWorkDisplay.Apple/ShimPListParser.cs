using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace Org.Grush.EchoWorkDisplay.Apple;

internal interface IPListElement
{
    public PListValueKind Kind { get; }
    public PListDict? AsDict() => null;
    public PListArray? AsArray() => null;
    public PListValue? AsValue() => null;
    public PListValue? AsValueOf<T>() => null;
    public T? AsValuePrimitive<T>() => default;
}


internal class PListDict : Dictionary<string, IPListElement>, IPListElement
{
    public PListValueKind Kind => PListValueKind.Dict;
    public PListDict AsDict() => this;
}

internal class PListArray : List<IPListElement>, IPListElement
{
    public PListValueKind Kind => PListValueKind.Array;
    public PListArray AsArray() => this;
}

internal enum PListValueKind
{
    Unknown = 0,
    Array = 1,
    Dict,
    String,
    Data,
    Date,
    Integer,
    Real,
    Bool,
}

[DebuggerDisplay("PListValue<{Kind}, {Value}>")]
internal abstract class PListValue : IPListElement
{
    protected object Value { get; }
    public PListValueKind Kind { get; }
    private PListValue(object value, PListValueKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public PListValue AsValue() => this;

    public PListValue? AsValueOf<T>()
        => TryGetValue<T>(out var _) ? this : null;
    public T? AsValuePrimitive<T>()
        => TryGetValue<T>(out var value) ? value : default;

    public static PListValue Create<T>(T value)
        => value switch
        {
            bool b => new PListPrimitive<bool>(b, PListValueKind.Bool),
            string s => new PListPrimitive<string>(s, PListValueKind.String),
            ReadOnlyMemory<byte> by => new PListPrimitive<ReadOnlyMemory<byte>>(by, PListValueKind.Data),
            DateTimeOffset d => new PListPrimitive<DateTimeOffset>(d, PListValueKind.Date),
            long l => new PListPrimitive<long>(l, PListValueKind.Integer),
            decimal d => new PListPrimitive<decimal>(d, PListValueKind.Real),
            _ => new PListPrimitive<object>(value, PListValueKind.Unknown),
        };

    public abstract bool TryGetValue<T>([NotNullWhen(true)] out T? value);

    internal class PListPrimitive<T>(
        T value,
        PListValueKind kind
    ) : PListValue(value, kind)
    {
        public override bool TryGetValue<T1>([NotNullWhen(true)] out T1? value) where T1 : default
        {
            try
            {
                value = (T1)Value;
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }
    }
}

internal class ShimPListParser : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    public ShimPListParser(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }
    
    private void ThrowIfCancellationRequested()
        => _cancellationTokenSource.Token.ThrowIfCancellationRequested();
    
    public async Task<IPListElement> ParseAsync(Stream xmlStream)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            // DtdProcessing = DtdProcessing.Ignore,
            DtdProcessing = DtdProcessing.Parse,
            // ValidationType = ValidationType.DTD,
        };
        // settings.ValidationEventHandler += (sender, args) =>
        // {
        //     Console.WriteLine(args.Message);
        // };
        using var reader = XmlReader.Create(xmlStream, settings);
        
        await reader.ReadAsync();
        if (reader.NodeType is XmlNodeType.XmlDeclaration)
            await reader.ReadAsync();
        
        ThrowIfCancellationRequested();
        if (reader.NodeType is XmlNodeType.DocumentType)
        {
            const string requiredDocType = "http://www.apple.com/DTDs/PropertyList-";
            
            PListUnexpectedNodeException.Assert(reader, name: "plist", nodeType: XmlNodeType.DocumentType);
            string? systemDoctype = reader.GetAttribute("SYSTEM");
            if (systemDoctype is null || !systemDoctype.StartsWith(requiredDocType))
                throw new InvalidOperationException($"PList doctype has invalid doctype <{systemDoctype}> vs expected <{requiredDocType}>");
            await reader.ReadAsync();
        }
        
        PListUnexpectedNodeException.Assert(reader, name: "plist", nodeType: XmlNodeType.Element);
        await reader.ReadAsync();

        ThrowIfCancellationRequested();
        return await ParseNode(reader);
    }

    private async Task<IPListElement> ParseNode(XmlReader reader)
    {
        ThrowIfCancellationRequested();
        PListUnexpectedNodeException.Assert(reader, nodeType: XmlNodeType.Element);

        return reader.Name switch
        {
            "array" => await ParseArray(reader),
            "dict" => await ParseDict(reader),
            "string" => new PListValue.PListPrimitive<string>(await reader.ReadElementContentAsStringAsync(), PListValueKind.String),
            "data" => await ParseData(reader),
            "date" => await ParseDate(reader),
            "integer" => new PListValue.PListPrimitive<long>(reader.ReadElementContentAsLong(), PListValueKind.Integer),
            "real" => new PListValue.PListPrimitive<decimal>(reader.ReadElementContentAsDecimal(), PListValueKind.Real),
            "true" or "false" => await ParseBool(reader),
            _ => throw new PListUnexpectedNodeException("name", "<<supported-value>>", reader.Name, PListUnexpectedNodeException.GetLine(reader))
        };
    }

    private async Task<PListValue.PListPrimitive<ReadOnlyMemory<byte>>> ParseData(XmlReader reader)
    {
        PListUnexpectedNodeException.Assert(
            reader,
            name: "data",
            nodeType: XmlNodeType.Element,
            isEmptyElement: false
        );

        const int bufferSize = 100;
        byte[] buffer = new byte[bufferSize];
        
        using var ms = new MemoryStream();
        int bytesRead;
        while ((bytesRead = await reader.ReadElementContentAsBase64Async(buffer, 0, bufferSize)) > 0)
        {
            ms.Write(buffer, 0, bytesRead);
        }

        // await reader.ReadAsync();
        
        return new(ms.ToArray(), PListValueKind.Data);
    }

    private async Task<PListValue.PListPrimitive<DateTimeOffset>> ParseDate(XmlReader reader)
    {
        PListUnexpectedNodeException.Assert(
            reader,
            name: "date",
            nodeType: XmlNodeType.Element,
            isEmptyElement: false
        );

        var str = await reader.ReadElementContentAsStringAsync();

        return new(
            DateTimeOffset.Parse(str, styles: DateTimeStyles.AllowWhiteSpaces),
            PListValueKind.Date
        );
    }
    
    private async Task<PListValue.PListPrimitive<bool>> ParseBool(XmlReader reader)
    {
        PListUnexpectedNodeException.Assert(reader, isEmptyElement: true, nodeType: XmlNodeType.Element);
        
        bool val = reader.Name switch
        {
            "true" => true,
            "false" => false,
            _ => throw new PListUnexpectedNodeException("name", "true|false", reader.Name, PListUnexpectedNodeException.GetLine(reader)),
        };
        
        await reader.ReadAsync();

        return new(val, PListValueKind.Bool);
    }

    /// <summary>
    /// Pre-condition: current element must be a &lt;dict&gt;.
    /// Post-condition: current element is the subsequent element after dict.
    /// </summary>
    /// <param name="reader"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task<PListDict> ParseDict(XmlReader reader)
    {
        PListUnexpectedNodeException.Assert(
            reader,
            name: "dict",
            nodeType: XmlNodeType.Element,
            isEmptyElement: false
        );

        PListDict dict = [];
        
        // step into first node
        await reader.ReadAsync();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            PListUnexpectedNodeException.Assert(
                reader,
                name: "key",
                nodeType: XmlNodeType.Element,
                isEmptyElement: false
            );

            string key = await reader.ReadElementContentAsStringAsync();
            // auto-moves to next
            
            IPListElement value = await ParseNode(reader);
            dict[key] = value;
            
            if (reader.Name is not "key" && reader.NodeType is not XmlNodeType.EndElement)
                 Console.WriteLine("Err");
        }

        // move to subsequent element
        await reader.ReadAsync();

        return dict;
    }

    private async Task<PListArray> ParseArray(XmlReader reader)
    {
        PListUnexpectedNodeException.Assert(
            reader,
            name: "array",
            nodeType: XmlNodeType.Element,
            isEmptyElement: false
        );
        
        PListArray array = [];
        
        // step into first node
        await reader.ReadAsync();

        while (reader.NodeType != XmlNodeType.EndElement)
        {
            array.Add(await ParseNode(reader));
        }
        
        // move to subsequent element
        await reader.ReadAsync();
        
        return array;
    }

    public abstract class PListException(string msg) : Exception(msg);

    public class PListUnexpectedNodeException(
        string wrongThing,
        string expected,
        string actual,
        (int Number, int Position)? Line
    ) : PListException($"Expected node's {wrongThing} to be {expected} but got {actual}")
    {
        public string WrongThing { get; } = wrongThing;
        public string Expected { get; } = expected;
        public string Actual { get; } = actual;

        public static void Assert(
            XmlReader reader,
            string? name = null,
            XmlNodeType? nodeType = null,
            string? value = null,
            Type? valueType = null,
            bool? isEmptyElement = null
        )
        {
            string wrongThing;
            string expected;
            string actual;
            if (name is not null && reader.Name != name)
                (wrongThing, expected, actual) = (nameof(name), name, reader.Name);
            else if (nodeType is not null && reader.NodeType != nodeType)
                (wrongThing, expected, actual) = (nameof(nodeType), nodeType.ToString()!, reader.NodeType.ToString());
            else if (value is not null && reader.Value != value)
                (wrongThing, expected, actual) = (nameof(value), value, reader.Value);
            else if (valueType is not null && reader.ValueType != valueType)
                (wrongThing, expected, actual) = (nameof(valueType), valueType.ToString(), reader.ValueType.ToString());
            else if (isEmptyElement is not null && isEmptyElement != reader.IsEmptyElement)
                (wrongThing, expected, actual) = (nameof(isEmptyElement), isEmptyElement.ToString()!, reader.IsEmptyElement.ToString());
            else
                return;

            try
            {
                var inner = reader.ReadInnerXml();
                Console.WriteLine("Error near\n{0}", inner[..Math.Min(inner.Length, 100)]);
            }
            catch
            {
            }

            throw new PListUnexpectedNodeException(wrongThing, expected, actual, GetLine(reader));
        }

        public static (int Number, int Position)? GetLine(XmlReader reader)
            => reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
                ? (lineInfo.LineNumber, lineInfo.LinePosition)
                : null
            ;
    }

    public ValueTask DisposeAsync()
    {
        return new(_cancellationTokenSource.CancelAsync());
    }
}