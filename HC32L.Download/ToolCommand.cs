using System.CommandLine;

namespace HC32L.Download;

public class DownloadCommand : Command
{
    public DownloadCommand() : base("download", "download a program to the mcu")
    {
        var binPath = new Argument<string>("binaryPath");
        var portPath = new Argument<string>("portPath");
        var rawOption = new Option<bool>("--raw", "binary is a raw bin file from objcopy");
        var baudOption = new Option<int?>("--baud", "use the specified baud rate (9600 default)");
        this.AddArgument(binPath);
        this.AddArgument(portPath);
        this.AddOption(rawOption);
        this.AddOption(baudOption);
        this.SetHandler((string binaryPath, string portPath, bool raw, int? baud) =>
        {
            var ctx = new DownloadMessgeContext
            {
                Port = new System.IO.Ports.SerialPort(portPath),
                BaudRate = baud ?? 9600,
                BinaryPath = binaryPath,
                UseRawBinary = raw
            };
            ctx.Port.BaudRate = ctx.BaudRate;
            ctx.Run();
        }, binPath, portPath, rawOption, baudOption);
    }

}
public class ListenCommand : Command
{
    public ListenCommand() : base("listen", "listen for debug output on a port")
    {

        var portPath = new Argument<string>("portPath");
        var baudOption = new Option<int?>("--baud", "use the specified baud rate (9600 default)");

        this.AddArgument(portPath);
        this.AddOption(baudOption);

        this.SetHandler((string portPath, int? baud) =>
        {

            var ctx = new MessageContext
            {
                Port = new System.IO.Ports.SerialPort(portPath),
                BaudRate = baud ?? 9600,
            };
            ctx.Port.BaudRate = ctx.BaudRate;
            new ResetMessage().Execute(ctx);
            using (ctx.Port)
            {
                while (true)
                {
                    if (ctx.Port.BytesToRead > 0)
                    {
                        Console.Write(ctx.Port.ReadExisting());
                    }
                }
            }
                

        }, portPath, baudOption);
    }
}
public class ShowConfigCommand : Command
{
    public ShowConfigCommand() : base("show-config", "show MCU wiring information")
    {
        this.SetHandler(() =>
        {
            Console.WriteLine(@"Please connect the serial port and MCU as such:
Serial.DTR/RTS => MCU.P00/RESET
Serial.V+ => MCU.V+ (6)
Serial.GND =>MCU.GND(4)
Serial.RXD => MCU.P31(15)
Serial.TXD => MCU.P27(14)
OR
Serial.RXD => MCU.P35(19)
Serial.TXD => MCU.P36(20)");
        });
    }
}
public class ToolCommand :RootCommand
{
    public ToolCommand() : base("a tool for managing hc32l110 mcus")
    {
       
        AddCommand(new DownloadCommand());
        AddCommand(new ListenCommand());
        AddCommand(new ShowConfigCommand());
    }

}
