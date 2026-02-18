using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Windows;

public class WindowsPlatformManager : IPlatformManager
{
    public WindowsSessionManagerBuilder SessionManagerBuilder { get; }

    async Task<ImmutableList<IEnumeratedSerialPort>> IPlatformManager.GetSerialPortsAsync(CancellationToken cancellationToken)
        => [..await GetSerialPortsAsync(cancellationToken)];
    public async Task<ImmutableList<EnumeratedWin32SerialPort>> GetSerialPortsAsync(CancellationToken cancellationToken)
    {
        
        using Process proc = new()
        {
            StartInfo =
            {
                FileName = "C:/WINDOWS/System32/WindowsPowerShell/v1.0/PowerShell.exe",
                ArgumentList =
                {
                    "Get-WmiObject",
                    "Win32_SerialPort",
                    "|",
                    "ConvertTo-Json",
                },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            },
        };

        proc.Start();

        var portListTask = JsonSerializer.DeserializeAsync(
            proc.StandardOutput.BaseStream,
            LocalSerializerCtx.Default.EnumeratedWin32SerialPortArray,
            cancellationToken
        );
        
        await proc.WaitForExitAsync(cancellationToken);
        
        var portList = await portListTask;
        
        return portList!.ToImmutableList();
    }
    
    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EnumeratedWin32SerialPort[]))]
internal partial class LocalSerializerCtx : JsonSerializerContext;

/// <summary>
/// 
/// </summary>
/// <seealso href="https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-serialport"/>
public partial record EnumeratedWin32SerialPort(
    UInt16 Availability,
    bool Binary,
    IReadOnlyList<UInt16>? Capabilities,
    IReadOnlyList<string>? CapabilityDescriptions,
    string Caption,
    UInt32 ConfigManagerErrorCode,
    bool ConfigManagerUserConfig,
    string CreationClassName,
    string Description,
    string DeviceID,
    bool? ErrorCleared,
    string? ErrorDescription,
    DateTime? InstallDate,
    UInt32? LastErrorCode,
    UInt32 MaxBaudRate,
    UInt32 MaximumInputBufferSize,
    UInt32 MaximumOutputBufferSize,
    UInt32? MaxNumberControlled,
    string Name,
    bool OSAutoDiscovered,
    // e.g. "USB\\VID_2E8A\u0026PID_000A\u0026MI_00\\6\u0026369AB57C\u00260\u00260000"
    // aka  USB\VID_2E8A&PID_000A&MI_00\6&369AB57C&0&0000
    string PNPDeviceID,
    IReadOnlyList<UInt16> PowerManagementCapabilities,
    bool PowerManagementSupported,
    UInt16? ProtocolSupported,
    string ProviderType,
    bool SettableBaudRate,
    bool SettableDataBits,
    bool SettableFlowControl,
    bool SettableParity,
    bool SettableParityCheck,
    bool SettableRLSD,
    bool SettableStopBits,
    string Status,
    UInt16 StatusInfo,
    bool Supports16BitMode,
    bool SupportsDTRDSR,
    bool SupportsElapsedTimeouts,
    bool SupportsIntTimeouts,
    bool SupportsParityCheck,
    bool SupportsRLSD,
    bool SupportsRTSCTS,
    bool SupportsSpecialCharacters,
    bool SupportsXOnXOff,
    bool SupportsXOnXOffSet,
    string SystemCreationClassName,
    string SystemName,
    DateTime? TimeOfLastReset
) : IEnumeratedSerialPort
{
    public UInt16? VendorId
        => MyRegex().Match(PNPDeviceID) is { Success: true } match
            ? UInt16.Parse(match.ValueSpan, NumberStyles.HexNumber)
            : null;

    string IEnumeratedSerialPort.PortName => DeviceID;

    uint? IEnumeratedSerialPort.MaxBaudRate => MaxBaudRate;
    bool? IEnumeratedSerialPort.SupportsRTSCTS => SupportsRTSCTS;
    bool? IEnumeratedSerialPort.SupportsDTRDSR => SupportsDTRDSR;

    [GeneratedRegex(@"(?<=\\VID_)([A-Fa-f0-9]{4})")]
    private static partial Regex MyRegex();
};