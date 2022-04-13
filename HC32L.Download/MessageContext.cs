using Microsoft.Extensions.Logging;
using Mono.Options;
using System.IO.Ports;

namespace HC32L.Download;

public class MessageContext
{
    public MessageContext()
    {
        var loggerFactory =
 LoggerFactory.Create(builder =>
     builder.AddSimpleConsole(options =>
     {
         options.IncludeScopes = false;
         options.SingleLine = false;
         options.TimestampFormat = "";
     }));
        Log = loggerFactory.CreateLogger<Program>();
    }
    public SerialPort Port { get; set; }
    public int BaudRate { get; set; }
    public ILogger Log { get; private set; }
}
public class DownloadMessgeContext : MessageContext
{
 
    public bool UseRawBinary { get; set; }
    public string BinaryPath { get; set; }
    


    public void Run()
    {
        string step = "none";
        try
        {
            foreach (var msg in GetMessages())
            {
                step = msg.GetType().Name;
                msg.Execute(this);
            }
        }
        catch (Exception ex)
        {
            Log.LogCritical($"Error uploading {BinaryPath} over {Port.PortName} at step {step}: {ex.Message}");
        }

    }
    private IEnumerable<Message> GetMessages()
    {
        yield return new InitMessage();
        yield return new HandshakeMessage();
        yield return new DownloadRamCodeMessage();
        yield return new ChipEraseMessage();
        yield return new BlankCheckMessage(0x00000000, 512, 64);
        if (UseRawBinary)
        {
            yield return new WriteBinaryMessage(0x00000000, BinaryPath, 64);
        }
        else
        {
            yield return new WriteElfMessage(0x00000000, BinaryPath, 64);
        }
        yield return new ResetMessage();

    }



}
