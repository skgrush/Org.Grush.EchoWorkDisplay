using Org.Grush.EchoWorkDisplay.Common;

namespace Org.Grush.EchoWorkDisplay.Apple;

internal record AppleEnumeratedSerialPort(
    UInt16 VendorId,
    UInt16 ProductId,
    string? UsbVendorName,
    string? UsbProductName,
    string? IoCallOutPath,
    string? IoDialInPath,
    string? IOTTYDevice,
    uint? MaxBaudRate,
    bool? SupportsRTSCTS,
    bool? SupportsDTRDSR,
    string? UsbSerialNumber
) : IEnumeratedSerialPort
{
    UInt16? IEnumeratedSerialPort.VendorId => VendorId;
    // TODO
    public string? PortName => IoDialInPath;
}

// tty.usbmodem1101
//             1100000_16  device locationID (int)
//            01000000     xhci hub locationID (str)
