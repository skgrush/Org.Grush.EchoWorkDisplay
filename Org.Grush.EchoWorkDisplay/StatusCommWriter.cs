using Microsoft.Extensions.Logging;
using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay;

using System.IO.Ports;

public sealed class StatusCommWriter(
    ILogger<StatusCommWriter> logger,
    ConfigProvider configProvider,
    IPlatformManager platformManager
) : IAsyncDisposable
{
    private readonly MyLittleSemaphore _lock = new(TimeSpan.FromMilliseconds(configProvider.Config.ComPortSearchDelayMilliseconds));
    
    public const UInt16 RaspberryPiFoundationVendorId = 0x2E8A;
    
    public event EventHandler<StatusCommWriter, Port.RawMessage>? MessageReceived;
    
    private Port? Port { get; set; }

    public async Task<bool> RefreshPortAsync(CancellationToken cancellationToken = default)
        => await RefreshPortAsync(true, cancellationToken);


    private async Task<bool> RefreshPortAsync(bool doLock, CancellationToken cancellationToken)
    {
        await using var _ = doLock
            ? await _lock.WaitAsync(cancellationToken)
            : new MyLittleSemaphore.FakeScope()
        ;
        
        cancellationToken.ThrowIfCancellationRequested();

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
    
    private async Task<Port?> GetPiPortAsync(CancellationToken cancellationToken)
    {
        var ports = await platformManager.GetSerialPortsAsync(cancellationToken);

        var piPorts = ports
            .Where(port => port.VendorId is RaspberryPiFoundationVendorId)
            .ToList();

        var piPort = piPorts.FirstOrDefault();
        if (piPort is null)
            return null;

        int baud = (int?)piPort.MaxBaudRate ?? configProvider.Config.BaudRate;

        var serialPort = new SerialPort(piPort.PortName, baud);
        serialPort.RtsEnable = piPort.SupportsRTSCTS ?? true;
        serialPort.DtrEnable = piPort.SupportsDTRDSR ?? true;

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
        await using var _ = await _lock.WaitAsync(cancellationToken);
        
        long bytesWritten = 0;

        while (messages.Count is not 0)
        {
            await WaitForPortRefreshAsync(cancellationToken);

            try
            {
                bytesWritten += await Port!.WriteMessagesAsync(messages, cancellationToken);
            }
            catch (InvalidOperationException e) when (e is ObjectDisposedException or Port.PortClosedException)
            {
                logger.LogWarning("Port closed; retrying...");
                continue;
            }
        }
            
        return bytesWritten;
    }

    public async Task WaitForPortRefreshAsync(CancellationToken cancellationToken)
    {
        while (!await RefreshPortAsync(cancellationToken))
        {
            ref var config = ref configProvider.Config;
            logger.LogTrace("Waiting for port for {ConfigComPortSearchDelayMilliseconds}ms ({ConfigComPortSearchDelayMillisecondsName})...", config.ComPortSearchDelayMilliseconds, nameof(config.ComPortSearchDelayMilliseconds));
            await Task.Delay(millisecondsDelay: config.ComPortSearchDelayMilliseconds, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.DisposeAsync();
        if (Port is not null)
            await Port.DisposeAsync();
        return;
    }
}
