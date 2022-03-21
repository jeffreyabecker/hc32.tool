using  HC32L.Download;
using (var port = new System.IO.Ports.SerialPort("COM7"))
{
    
    port.Execute(new InitCommand());
    Console.WriteLine("Shaking hands");
    port.Execute(new HandshakeCommand());
    Console.WriteLine("Downloading ram code");
    port.Execute(new DownloadRamCodeCommand());
    Console.WriteLine("Done download");
    Console.WriteLine("Erasing");
    port.Execute(new ChipEraseCommand());
    port.Execute(new BlankCheckCommand(0x00000000, 512, 64));
    port.Execute(new WriteElfCommand(0x00000000, @"C:\ode\demo_hc32\main.elf", 64));

}
