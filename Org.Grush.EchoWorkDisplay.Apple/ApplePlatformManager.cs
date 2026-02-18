using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;


internal sealed partial class ApplePlatformManager : IPlatformManager
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly HttpClient _client = new();

    public ApplePlatformManager(
        ILogger<ApplePlatformManager> logger
    )
    {
        ConsoleLoop();
    }

    private const string Prompt = 
        """
        Enter (1) change media, (2) change presence:
        """;

    internal event EventHandler<ApplePlatformManager, AppleMediaProperties?>? consoleDeclaredMediaProperties;
    internal event EventHandler<ApplePlatformManager, PresenceAvailability>? consoleDeclaredPresence;

    private async void ConsoleLoop()
    {
        await using var stdin = Console.OpenStandardInput();
        using var stdinReader = new StreamReader(stdin);

        while (true)
        {
            Console.Write(Prompt + " ");
            var line = await stdinReader.ReadLineAsync(_cancellationTokenSource.Token);
            if (line is null)
            {
                Console.WriteLine("?");
                continue;
            }

            switch (line.Trim().ToLowerInvariant())
            {
                case "1":
                case "change media":
                    await PromptChangeMedia(stdinReader);
                    break;
                case "2":
                case "change presence":
                    await PromptPresence(stdinReader);
                    break;
                default:
                    await Console.Error.WriteLineAsync("Unsupported answer...");
                    break;
            }
        }
    }

    private async Task PromptPresence(StreamReader streamReader)
    {
        PresenceAvailability presenceAvailability;
        try
        {
            while (true)
            {
                Console.WriteLine("Presence options: {0}", string.Join(", ", Enum.GetNames<PresenceAvailability>()));
                Console.Write("Enter presence [PresenceUnknown]: ");

                var str = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
                if (str is null)
                {
                    Console.WriteLine("?");
                    continue;
                }

                str = str.Trim();
                if (str is "")
                {
                    presenceAvailability = PresenceAvailability.PresenceUnknown;
                    break;
                }

                if (Enum.TryParse(str, true, out presenceAvailability))
                {
                    break;
                }
                else
                {
                    await Console.Error.WriteLineAsync("ERROR: Failed to parse presence option...\n");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"ERROR: {ex.GetType()}: {ex.Message}\n");
            return;
        }
        
        Console.WriteLine("Successfully chose presence: {0}", presenceAvailability);
        consoleDeclaredPresence?.Invoke(this, presenceAvailability);
    }

    private async Task PromptChangeMedia(StreamReader streamReader)
    {
        try
        {
            Console.Write("Enter Arist: ");
            var artist = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
            
            Console.WriteLine("Enter Album: ");
            var album = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
            
            Console.Write("Enter Title: ");
            var title = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);
            
            Uri? thumbnailUri = null;
            byte[]? thumbnail = null;
            while (true)
            {
                Console.WriteLine("Enter Thumbnail URL: ");
                var url = await streamReader.ReadLineAsync(_cancellationTokenSource.Token);

                if (url is null or "")
                {
                    break;
                }

                try
                {
                    thumbnailUri = new(url);
                    using HttpRequestMessage req = new(HttpMethod.Get, thumbnailUri);
                    req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("image/png,image/jpeg;q=0.8,image/*;q=0.5"));

                    using var response = await _client.GetAsync(thumbnailUri, HttpCompletionOption.ResponseHeadersRead);
                    
                    response.EnsureSuccessStatusCode();

                    var mediaType = response.Content.Headers.ContentType?.MediaType;

                    if (mediaType is not null && mediaType.StartsWith("image/"))
                    {
                        thumbnail = await response.Content.ReadAsByteArrayAsync(_cancellationTokenSource.Token);
                        new ReadOnlyMemory<byte>(thumbnail);
                        break;
                    }
                    else
                    {
                        Console.WriteLine("?");
                        continue;
                    }
                }
                catch (Exception thumbE)
                {
                    await Console.Error.WriteLineAsync($"THUMB ERROR: {thumbE.GetType()}: {thumbE.Message}");
                }
            }
            
            AppleMediaProperties? props = new(
                artist,
                album,
                title,
                thumbnail
            );
            if (props == AppleMediaProperties.Default)
                props = null;
            
            consoleDeclaredMediaProperties?.Invoke(this, props);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
    
    public async Task<ImmutableList<IEnumeratedSerialPort>> GetSerialPortsAsync(CancellationToken cancellationToken)
    {
        IPListElement ele;

        using (Process proc = new()
               {
                   StartInfo =
                   {
                       FileName = "/usr/sbin/ioreg",
                       ArgumentList =
                       {
                           
                           // "-p", "IOUSB", // specifies the IOUSB "plane"
                           // "-l", // show all properties of everything
                           "-k", "IOTTYBaseName",
                           "-a", // output as PList
                           "-t", // "show tree location of each subtree"
                           "-r", // ignore other subtrees
                           "-l", // full properties on visible nodes
                       },
                       CreateNoWindow = true,
                       UseShellExecute = false,
                       RedirectStandardOutput = true,
                   },
               })
        {
            await using ShimPListParser shim = new(cancellationToken);

            proc.Start();

            ele = await shim.ParseAsync(proc.StandardOutput.BaseStream);

            await proc.WaitForExitAsync(cancellationToken);
        }

        var enumeration = ele switch
        {
            PListDict dict => EnumeratePortsOfIORegistryEntry(dict, []),
            PListArray ary => ary.OfType<PListDict>().SelectMany(d => EnumeratePortsOfIORegistryEntry(d, [])),
            _ => throw new NotSupportedException(),
        };

        return enumeration.Select(chain => chain.Port).ToImmutableList();
    }

    private record IOChain(
        ImmutableList<IOChain.Ancestor> Ancestors,
        IEnumeratedSerialPort Port
    )
    {
        public record Ancestor(string IOObjectClass, string IORegistryEntryName);
    }
    
    private IEnumerable<IOChain> EnumeratePortsOfIORegistryEntry(
        PListDict dict,
        ImmutableList<(IOChain.Ancestor Ancestor, PListDict Dict)> ancestors
    )
    {
        string? ioObjectClass = dict.GetValueOrDefault("IOObjectClass")?.AsValuePrimitive<string>();
        string? ioRegistryEntryName = dict.GetValueOrDefault("IORegistryEntryName")?.AsValuePrimitive<string>();
        
        if (ioObjectClass is null || ioRegistryEntryName is null)
            yield break;

        if (ioObjectClass is "IOSerialBSDClient")
        {
            (IOChain.Ancestor Ancestor, PListDict Dict) usbHostInterfaceParent = ancestors.Last();
            if (
                usbHostInterfaceParent.Dict
                    .GetValueOrDefault("IOProviderClass")
                    ?.AsValuePrimitive<string>()
                is "IOUSBHostInterface"
            )
            {
                (IOChain.Ancestor? Ancestor, PListDict? Dict) usbHostInterfaceGrandParent = default;
                if (usbHostInterfaceParent != default)
                    usbHostInterfaceGrandParent = ancestors.SkipLast(1).LastOrDefault();

                AppleEnumeratedSerialPort port = new(
                    VendorId: (UInt16)usbHostInterfaceParent.Dict["idVendor"].AsValuePrimitive<long>(),
                    ProductId: (UInt16)usbHostInterfaceParent.Dict["idProduct"].AsValuePrimitive<long>(),
                    UsbVendorName: usbHostInterfaceGrandParent.Dict?.GetValueOrDefault("USB Vendor Name")
                        ?.AsValuePrimitive<string>(),
                    UsbProductName: usbHostInterfaceGrandParent.Dict?.GetValueOrDefault("USB Product Name")
                        ?.AsValuePrimitive<string>(),
                    IoCallOutPath: dict.GetValueOrDefault("IOCalloutDevice")?.AsValuePrimitive<string>(),
                    IoDialInPath: dict.GetValueOrDefault("IODialinDevice")?.AsValuePrimitive<string>(),
                    IOTTYDevice: dict.GetValueOrDefault("IOTTYDevice")?.AsValuePrimitive<string>(),
                    MaxBaudRate: null,
                    SupportsRTSCTS: true,
                    SupportsDTRDSR: true,
                    UsbSerialNumber: usbHostInterfaceGrandParent.Dict?.GetValueOrDefault("USB Serial Number")
                        ?.AsValuePrimitive<string>()
                );

                yield return new(
                    [..ancestors.Select(pair => pair.Ancestor)],
                    port
                );
            }
            else
            {
                Console.WriteLine("Non-USB serial device, ignoring");
            }
        }
        
        if (!dict.TryGetValue("IORegistryEntryChildren", out var t))
            yield break;

        List<PListDict> children = t switch
        {
            PListArray pla => pla.OfType<PListDict>().ToList(),
            PListDict d => [d],
            _ => [],
        };

        var newAncestors = ancestors.Add(
            (
                Ancestor: new IOChain.Ancestor(ioObjectClass, ioRegistryEntryName),
                dict
            )
        );
        
        foreach (var child in children)
        {
            var childEnumeration = EnumeratePortsOfIORegistryEntry(
                child,
                newAncestors
            );
            foreach (var childIoChain in childEnumeration)
                yield return childIoChain;
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return new(_cancellationTokenSource.CancelAsync());
    }

    [GeneratedRegex("""
                        ^"(?<PropName>[^"]+)" *= *(?<PropRawValue>.*)$
                        """)]
    private static partial Regex LineRe();
}