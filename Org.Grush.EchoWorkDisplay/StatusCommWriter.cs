namespace Org.Grush.EchoWorkDisplay;

using System.IO.Ports;

public class StatusCommWriter
{

    public async Task<object> WriteToPort()
    {
        var names = SerialPort.GetPortNames();
    }
}