using ELFSharp.ELF;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
namespace HC32L.Download;
public abstract class Message
{
    public abstract void Execute(MessageContext ctx);
}

public abstract class DownloadProcessMessage : Message
{
    public abstract void Execute(DownloadMessgeContext ctx);
    public override void Execute(MessageContext ctx)
    {
        Execute((DownloadMessgeContext)ctx);
    }
}
public class InitMessage : DownloadProcessMessage
{

    public override void Execute(DownloadMessgeContext ctx)
    {
        ctx.Log.LogInformation("Opening Serial Port");
        if (ctx.Port.IsOpen)
        {
            ctx.Port.Close();
        }
        ctx.Port.BaudRate = ctx.BaudRate;
        ctx.Port.DataBits = 8;
        ctx.Port.Parity = Parity.None;
        ctx.Port.StopBits = StopBits.One;
        ctx.Port.Open();
    }
}
public class ResetMessage : Message
{

    public override void Execute(MessageContext ctx)
    {
        if (!ctx.Port.IsOpen)
        {
            ctx.Port.BaudRate = ctx.BaudRate;
            ctx.Port.DataBits = 8;
            ctx.Port.Parity = Parity.None;
            ctx.Port.StopBits = StopBits.One;
            ctx.Port.Open();
        }
        ctx.Log.LogInformation("Resetting the MCU");
        ctx.Port.RtsEnable = true;
        ctx.Port.DtrEnable = true;
        Thread.Sleep(TimeSpan.FromMilliseconds(10));
        ctx.Port.RtsEnable = false;
        ctx.Port.DtrEnable = false;
        Thread.Sleep(TimeSpan.FromMilliseconds(100));
        ctx.Log.LogInformation("MCU Reset Done");
    }
}



public class HandshakeMessage : DownloadProcessMessage
{
    public static byte[] handshake = new byte[20] { 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF };
    public override void Execute(DownloadMessgeContext ctx)
    {
        ctx.Log.LogInformation("Initiating Handshake");
        ctx.Port.RtsEnable = true;
        ctx.Port.DtrEnable = true;
        Thread.Sleep(TimeSpan.FromMilliseconds(10));
        ctx.Port.DiscardInBuffer();
        ctx.Port.DiscardOutBuffer();
        ctx.Port.Write(handshake);
        ctx.Port.RtsEnable = false;
        ctx.Port.DtrEnable = false;
        Thread.Sleep(TimeSpan.FromMilliseconds(100));

        var result = ctx.Port.Read(1, TimeSpan.FromMilliseconds(5000));
        if (result == null || result[0] != 0x11)
        {
            throw new Exception($"Hadshake Failed, got {result?.Length ?? 0} bytes : {result.ToHexString()}");
        }
        ctx.Port.DiscardInBuffer();
        ctx.Port.DiscardOutBuffer();
        ctx.Log.LogInformation("Handshake Success");

    }
}

public class DownloadRamCodeMessage : DownloadProcessMessage
{
    private byte[] GetRamCodeBinary()
    {
        var resources = this.GetType().Assembly.GetManifestResourceNames();
        using var source = this.GetType().Assembly.GetManifestResourceStream("hc32tool.m_flash.hc005");
        using var dst = new MemoryStream();
        source.CopyTo(dst);
        return dst.ToArray();
    }
    public const uint RamBaseAddress = 0x20000000;
    public override void Execute(DownloadMessgeContext ctx)
    {
        var binData = GetRamCodeBinary();

        ctx.Log.LogInformation("Initiating DownloadMessage process");
        ctx.Port.DiscardInBuffer();
        ctx.Port.DiscardOutBuffer();
        ctx.Port.Write(new Crc8Message((byte)0x00, RamBaseAddress, (uint)binData.Length));
        var response = ctx.Port.Read(1, TimeSpan.FromSeconds(5));
        if (response?[0] != 0x01)
        {
            throw new Exception($"Failed to issue DownloadMessage got {response?.Length ?? 0} bytes :{response.ToHexString()}");

        }
        ctx.Log.LogInformation("Starting download of shim");

        ctx.Port.DiscardInBuffer();
        ctx.Port.DiscardOutBuffer();
        ctx.Port.Write(new Crc8Message(binData));
        var response2 = ctx.Port.Read(1, TimeSpan.FromSeconds(5));
        if (response2?[0] != 0x01)
        {
            throw new Exception($"Failed to download data got {response2?.Length ?? 0} bytes :{response2.ToHexString()}");

        }
        ctx.Log.LogInformation("Starting shim code");
        ctx.Port.DiscardInBuffer();
        ctx.Port.DiscardOutBuffer();
        ctx.Port.Write(new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0 });
        var response3 = ctx.Port.Read(11, TimeSpan.FromSeconds(5));
        ctx.Log.LogInformation($"Completed shim download");

    }
}
public class WriteBinaryMessage : DownloadProcessMessage
{
    private readonly uint _address;
    private readonly string _path;
    private readonly byte[] _data;
    private readonly ushort _pageSize;

    public WriteBinaryMessage(uint address, string path, ushort pageSize)
    {

        _address = address;
        _path = path;
        _data = File.ReadAllBytes(path);
        _pageSize = pageSize;
    }
    public override void Execute(DownloadMessgeContext ctx)
    {
        ctx.Log.LogDebug($"Downloading raw binary {_path} ({_data.Length} bytes) at {_address}");
        var pageCount = (int)Math.Ceiling(_data.Length / (_pageSize * 1.0));
        var pages = Enumerable.Range(0, pageCount)
            .Select(i => new WritePageMessage((uint)(_address + (_pageSize * i)), _data.Skip(i * _pageSize).Take(_pageSize).ToArray()));
        foreach (var page in pages)
        {
            page.Execute(ctx);
        }
        ctx.Log.LogInformation($"Downloading {_path} ({_data.Length} bytes) at {_address}");

    }
    public class WritePageMessage : DownloadProcessMessage
    {
        private readonly uint _address;
        private readonly byte[] _data;

        public WritePageMessage(uint address, byte[] data)
        {
            _address = address;
            _data = data;
        }

        public override void Execute(DownloadMessgeContext ctx)
        {
            ctx.Log.LogDebug($"Downloading page at 0x{_address:X8}");
            var header = new byte[] { 73, 4 }
                .Concat(BitConverter.GetBytes(_address))
                .Concat(BitConverter.GetBytes((ushort)_data.Length))
                .Concat(_data)
                .AppendChecksum8()
                .ToArray();
            ctx.Port.DiscardInBuffer();
            ctx.Port.DiscardOutBuffer();
            ctx.Port.Write(header);
            var recieved = ctx.Port.Read(9, TimeSpan.FromSeconds(5));
            if (recieved == null || recieved[0] != 73 || recieved.Take(8).Checksum8() != recieved[8])
            {
                throw new Exception($"Page write failed at {_address}, got {recieved?.Length ?? 0} bytes : {recieved.ToHexString()}");
            }

        }
    }
}
public class WriteElfMessage : DownloadProcessMessage
{
    private readonly string _path;
    private readonly uint _address;
    private readonly ushort _pageSize;

    public WriteElfMessage(uint address, string path, ushort pageSize)
    {
        _path = path;
        _address = address;
        _pageSize = pageSize;
    }


    public override void Execute(DownloadMessgeContext ctx)
    {

        var elf = ELFReader.Load(_path);
        var data = elf.Sections.FirstOrDefault(s => s.Name == ".text")?.GetContents();
        if (data == null)
        {
            throw new Exception($"unable to read the .text section from {_path}");
        }
        ctx.Log.LogInformation($"Downloading elf file {_path} ({data.Length} bytes) at {_address}");
        var pageCount = (int)Math.Ceiling(data.Length / (_pageSize * 1.0));
        var pages = Enumerable.Range(0, pageCount)
            .Select(i => new WriteBinaryMessage.WritePageMessage((uint)(_address + (_pageSize * i)), data.Skip(i * _pageSize).Take(_pageSize).ToArray()));
        foreach (var page in pages)
        {
            page.Execute(ctx);
        }
        ctx.Log.LogInformation($"Downloading {_path} ({data.Length} bytes) at {_address}");
    }
}
public class ChipEraseMessage : DownloadProcessMessage
{

    public override void Execute(DownloadMessgeContext ctx)
    {
        ctx.Log.LogInformation($"Starting Chip Erase");
        ctx.Port.DiscardInBuffer();
        ctx.Port.DiscardOutBuffer();
        ctx.Port.Write(new byte[] { 0x49, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4B });
        var response = ctx.Port.Read(9, TimeSpan.FromSeconds(5));
        if (response == null || response[0] != 73 || response.Take(8).Checksum8() != response[8])
        {
            throw new Exception("Chip erase failed");
        }
        ctx.Log.LogInformation($"Completed Chip Erase");
    }
}

public class BlankCheckMessage : DownloadProcessMessage
{
    private readonly uint _startAddress;
    private readonly int _pageSize;
    private readonly int _pageCount;

    public BlankCheckMessage(uint startAddress, int pageSize, int pageCount)
    {
        _startAddress = startAddress;
        _pageSize = pageSize;
        _pageCount = pageCount;
    }
    public override void Execute(DownloadMessgeContext ctx)
    {
        ctx.Log.LogInformation($"Starting blank check");
        var pages = Enumerable.Range(0, _pageCount)
            .Select(i => new BlankCheckSegment((uint)(_startAddress + i * _pageSize), _pageSize));
        foreach (var p in pages)
        {
            p.Execute(ctx);
        }
        ctx.Log.LogInformation($"Completed blank check");
    }
    private class BlankCheckSegment : DownloadProcessMessage
    {
        private readonly uint _startAddress;
        private readonly int _pageSize;

        public BlankCheckSegment(uint startAddress, int pageSize)
        {
            _startAddress = startAddress;
            _pageSize = pageSize;
        }
        public override void Execute(DownloadMessgeContext ctx)
        {
            ctx.Log.LogDebug($"Checking that 0x{_startAddress:X8}:{_pageSize} is blank");
            var message = new byte[] { 73, 7 }
                .Concat(BitConverter.GetBytes(_startAddress))
                .Concat(new byte[] { 4, 0 })
                .Concat(BitConverter.GetBytes(_pageSize))
                .AppendChecksum8()
                .ToArray();
            ctx.Port.DiscardInBuffer();
            ctx.Port.DiscardOutBuffer();
            ctx.Port.Write(message);
            var recieved = ctx.Port.Read(10, TimeSpan.FromSeconds(5));
            if (recieved == null || recieved[0] != 73 || recieved[6] != 1 || recieved[7] > 0 || recieved.Take(9).Checksum8() != recieved[9])
            {
                throw new Exception($"Failed BlankCheckSegment at 0x{_startAddress:X8}:{_pageSize} got {recieved?.Length ?? 0} bytes :{recieved.ToHexString()}");
            }
        }
    }
}
public class Crc8Message
{
    public static IEnumerable<byte> Splat(IEnumerable<object> parts)
    {
        foreach (var p in parts)
        {
            if (p is byte[] bytes)
            {
                foreach (var b in bytes)
                {
                    yield return b;
                }
            }
            else if (p is byte b)
            {
                yield return b;
            }
            else if (p is uint u)
            {
                foreach (var ubyte in BitConverter.GetBytes(u))
                {
                    yield return ubyte;
                }
            }
            else if (p is ushort us)
            {
                foreach (var usbyte in BitConverter.GetBytes(us))
                {
                    yield return usbyte;
                }
            }
        }
    }
    private byte[] _data;
    public Crc8Message(params object[] parts)
    {
        var tmpData = Crc8Message.Splat(parts).ToArray();
        _data = tmpData.AppendChecksum8();
    }
    public static implicit operator byte[](Crc8Message d) => d._data;
}
