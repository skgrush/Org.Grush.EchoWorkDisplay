using System.Collections.Immutable;

namespace Org.Grush.EchoWorkDisplay.Common;

public interface IEnumeratedSerialPort
{
    public UInt16? VendorId { get; }
    public string? PortName { get; }
    public uint? MaxBaudRate { get; }
    public bool? SupportsRTSCTS { get; }
    public bool? SupportsDTRDSR { get; }
}

public interface IPlatformManager : IAsyncDisposable
{
    // public BaseSessionManagerBuilder SessionManagerBuilder { get; }

    public Task<ImmutableList<IEnumeratedSerialPort>> GetSerialPortsAsync(CancellationToken cancellationToken);
}