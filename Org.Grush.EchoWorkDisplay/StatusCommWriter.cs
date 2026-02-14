using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;

namespace Org.Grush.EchoWorkDisplay;

using System.IO.Ports;

public sealed class StatusCommWriter(Action<string> log, Config config) : IAsyncDisposable
{
    private readonly Lock _lock = new();
    
    public const string RaspberryPiFoundationVendorId = "2E8A";
    
    public event EventHandler<StatusCommWriter, Port.RawMessage>? MessageReceived;
    
    private Port? Port { get; set; }

    public async Task<bool> RefreshPortAsync(CancellationToken cancellationToken = default)
        => await RefreshPortAsync(true, cancellationToken);


    private async Task<bool> RefreshPortAsync(bool doLock, CancellationToken cancellationToken)
    {
        if (doLock)
            _lock.Enter();
        
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (Port is not null)
            {
                if (Port.IsOpen)
                    return true;

                await Port.DisposeAsync();
                Port = null;
            }

            Port = await GetPiPortAsync(cancellationToken);
            if (Port is null)
                return false;

            try
            {
                return await Port.OpenAsync();
            }
            catch
            {
                var p = Port;
                Port = null;
                await p.DisposeAsync();
                return false;
            }
        }
        finally
        {
            if (doLock)
                _lock.Exit();
        }
    }
    
    private static async Task<Port?> GetPiPortAsync(CancellationToken cancellationToken)
    {
        var ports = await SerialPortEnumerator.GetSerialPortsAsync(cancellationToken);

        const string vidString = $"\\VID_{RaspberryPiFoundationVendorId}";
        var piPorts = ports
            .Where(port => port.PNPDeviceID.Contains(vidString))
            .ToList();

        var piPort = piPorts.FirstOrDefault();
        if (piPort is null)
            return null;

        var serialPort = new SerialPort(piPort.DeviceID, (int)piPort.MaxBaudRate);
        serialPort.RtsEnable = piPort.SupportsRTSCTS;
        serialPort.DtrEnable = piPort.SupportsDTRDSR;

        return new(serialPort, piPort);
    }

    public async void LoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitForPortRefreshAsync(cancellationToken);

            await foreach (var rawMsg in Port!.ReadMessagesAsync(cancellationToken))
            {
                MessageReceived?.Invoke(this, rawMsg);
            }
        }
    }

    public async Task<long> WriteToPortAsync(Port.RawMessage message, CancellationToken cancellationToken)
    {
        Queue<Port.RawMessage> messages = [];
        messages.Enqueue(message);
        return await WriteToPortAsync(messages, cancellationToken);
    }
    
    public async Task<long> WriteToPortAsync(Queue<Port.RawMessage> messages, CancellationToken cancellationToken)
    {
        _lock.Enter();
        
        long bytesWritten = 0;

        try
        {
            while (messages.Count is not 0)
            {
                await WaitForPortRefreshAsync(cancellationToken);

                try
                {
                    bytesWritten += await Port!.WriteMessagesAsync(messages, cancellationToken);
                }
                catch (InvalidOperationException e) when (e is ObjectDisposedException or Port.PortClosedException)
                {
                    log("Port closed; retrying...");
                    continue;
                }
            }
            
            return bytesWritten;
        }
        finally
        {
            _lock.Exit();
        }
    }

    public async Task WaitForPortRefreshAsync(CancellationToken cancellationToken)
    {
        while (!await RefreshPortAsync(cancellationToken))
        {
            log($"Waiting for port for {config.ComPortSearchDelayMilliseconds}ms ({nameof(config.ComPortSearchDelayMilliseconds)})...");
            await Task.Delay(millisecondsDelay: config.ComPortSearchDelayMilliseconds, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Port is not null)
            await Port.DisposeAsync();
        return;
    }
}


internal static class SerialPortEnumerator
{
    public static async Task<ImmutableList<EnumeratedSerialPort>> GetSerialPortsAsync(CancellationToken cancellationToken)
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
            LocalJsonSerializerContext.Default.EnumeratedSerialPortArray,
            cancellationToken
        );
        
        await proc.WaitForExitAsync(cancellationToken);
        
        var portList = await portListTask;
        
        return portList!.ToImmutableList();
    }
}

/// <summary>
/// 
/// </summary>
/// <seealso href="https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-serialport"/>
public record EnumeratedSerialPort(
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
);