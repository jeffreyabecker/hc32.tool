using ELFSharp.ELF;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HC32L.Download;
public abstract class Command
{
    public abstract bool Execute(SerialPort port);
}

public class InitCommand : Command
{
    public override bool Execute(SerialPort port)
    {
        if (port.IsOpen)
        {
            port.Close();
        }
        port.BaudRate = 9600;
        port.DataBits = 8;
        port.Parity = Parity.None;
        port.StopBits = StopBits.One;
        port.Open();
        return true;
    }
}

public class HandshakeCommand : Command
{
    public static byte[] handshake = new byte[20] { 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF, 0x18, 0xFF };
    public override bool Execute(SerialPort port)
    {
        port.RtsEnable = true;
        port.DtrEnable = true;
        System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(10));
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.Write(handshake);        
        port.RtsEnable = false;
        port.DtrEnable = false;
        System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(100));

        var result = port.Read(1, TimeSpan.FromMilliseconds(5000));
        if(result== null ||  result[0] != 0x11)
        {
            throw new Exception($"Hadshake Failed, got {result?.Length ?? 0} bytes : {result.ToHexString()}");
        }
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        Console.WriteLine("Handshake Success");
        return true;
    }
}

public class DownloadRamCodeCommand : Command
{
    private byte[] GetRamCodeBinary()
    {
        var resources = this.GetType().Assembly.GetManifestResourceNames();
        using var source = this.GetType().Assembly.GetManifestResourceStream("HC32L.Download.m_flash.hc005");
        using var dst = new MemoryStream();
        source.CopyTo(dst);
        return dst.ToArray();
    }
    public const uint RamBaseAddress = 0x20000000;
    public override bool Execute(SerialPort port)
    {
        var binData = GetRamCodeBinary();

        Console.WriteLine("Starting handshake DownloadCommand");
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.Write(new Crc8Message((byte)0x00, RamBaseAddress, (uint) binData.Length));
        var response = port.Read(1, TimeSpan.FromSeconds(5));
        if (response?[0] != 0x01)
        {
            Console.WriteLine($"Failed to issue DownloadCommand got {response?.Length ?? 0} bytes :{response.ToHexString()}");
            return false;
        }
        Console.WriteLine("Starting download of shim");
        
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.Write(new Crc8Message(binData));
        var response2 = port.Read(1, TimeSpan.FromSeconds(5));
        if (response2?[0] != 0x01)
        {
            Console.WriteLine($"Failed to download data got {response2?.Length ?? 0} bytes :{response2.ToHexString()}");
            return false;
        }
        Console.WriteLine("Jumping to 0x20000000");
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.Write(new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0 });
        var response3 = port.Read(11, TimeSpan.FromSeconds(5));
        Console.WriteLine($"Jumped to 0x20000000 got {response3?.Length ??0} bytes :{response3.ToHexString()}");

        return true;

    }
}
public class WriteBinaryCommand : Command
{
    private readonly uint _address;
    private readonly byte[] _data;
    private readonly ushort _pageSize;

    public WriteBinaryCommand(uint address, byte[] data, ushort pageSize)
    {
        
        _address = address;
        _data = data;
        _pageSize = pageSize;
    }
    public override bool Execute(SerialPort port)
    {
        var pageCount = (int)Math.Ceiling(_data.Length / (_pageSize * 1.0));
        var pages = Enumerable.Range(0, pageCount)
            .Select(i => new WritePageCommand((uint)(_address + (_pageSize * i)), _data.Skip(i * _pageSize).Take(_pageSize).ToArray()));
        foreach(var page in pages)
        {
            page.Execute(port);
        }
        return true;
    }
    public class WritePageCommand : Command
    {
        private readonly uint _address;
        private readonly byte[] _data;

        public WritePageCommand(uint address, byte[] data)
        {
            _address = address;
            _data = data;
        }

        public override bool Execute(SerialPort port)
        {
            var header = new byte[] { 73, 4 }
                .Concat(BitConverter.GetBytes(_address))
                .Concat(BitConverter.GetBytes((ushort)_data.Length))
                .Concat(_data)
                .AppendChecksum8()
                .ToArray();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            port.Write(header);
            var recieved = port.Read(9, TimeSpan.FromSeconds(5));
            if (recieved == null || recieved[0] != 73 || recieved.Take(8).Checksum8() != recieved[8])
            {
                throw new Exception($"Page write failed at {_address}, got {recieved?.Length ?? 0} bytes : {recieved.ToHexString()}");
            }
            return true;
        }
    }
}
public class WriteElfCommand : Command
{
    private readonly string _path;
    private readonly uint _address;
    private readonly ushort _pageSize;

    public WriteElfCommand(uint address, string path, ushort pageSize)
    {
        _path = path;
        _address = address;
        _pageSize = pageSize;
    }


    public override bool Execute(SerialPort port)
    {
        var elf = ELFReader.Load(_path);
        var text = elf.Sections.FirstOrDefault(s => s.Name == ".text")?.GetContents();
        if (text == null)
        {
            Console.WriteLine($"unable to read the .text section from {_path}");
        }
        return new WriteBinaryCommand(_address, text!, _pageSize).Execute(port);
    }
}
public class ChipEraseCommand : Command
{

    public override bool Execute(SerialPort port)
    {
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        port.Write(new byte[] { 0x49, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4B });
        var response = port.Read(9, TimeSpan.FromSeconds(5));
        if(response == null || response[0] != 73 || response.Take(8).Checksum8() != response[8])
        {
            throw new Exception("Chip erase failed");
        }
        return true;
    }
}

public class BlankCheckCommand : Command
{
    private readonly uint _startAddress;
    private readonly int _pageSize;
    private readonly int _pageCount;

    public BlankCheckCommand(uint startAddress, int pageSize, int pageCount)
    {
        _startAddress = startAddress;
        _pageSize = pageSize;
        _pageCount = pageCount;
    }
    public override bool Execute(SerialPort port)
    {
        var pages = Enumerable.Range(0, _pageCount)
            .Select(i => new BlankCheckSegment((uint)(_startAddress + i * _pageSize), _pageSize));
        foreach(var p in pages)
        {
            port.Execute(p);
        }
        return true;
    }
    private class BlankCheckSegment : Command
    {
        private readonly uint _startAddress;
        private readonly int _pageSize;

        public BlankCheckSegment(uint startAddress, int pageSize)
        {
            _startAddress = startAddress;
            _pageSize = pageSize;
        }
        public override bool Execute(SerialPort port)
        {

            var message = new byte[] { 73, 7 }
                .Concat(BitConverter.GetBytes(_startAddress))
                .Concat(new byte[] { 4, 0 })
                .Concat(BitConverter.GetBytes(_pageSize))
                .AppendChecksum8()
                .ToArray();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            port.Write(message);
            var recieved = port.Read(10, TimeSpan.FromSeconds(5));
            if (recieved == null || recieved[0] != 73 || recieved[6] != 1 || recieved[7] > 0 || recieved.Take(9).Checksum8() != recieved[9])
            {
                throw new Exception($"Failed BlankCheckSegment at {_startAddress}/{_pageSize} got {recieved?.Length ?? 0} bytes :{recieved.ToHexString()}");
            }
            return true;
        }
    }
}
public class Crc8Message
{
    public static IEnumerable<byte> Splat(IEnumerable<object> parts) 
    { 
        foreach(var p in parts)
        {
            if(p is byte[] bytes)
            {
                foreach(var b in bytes)
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
                foreach(var ubyte in BitConverter.GetBytes(u))
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
//public class Crc16Message
//{
//    private byte[] _data;
//    public Crc16Message(params object[] parts)
//    {
//        var tmpData = Crc8Message.Splat(parts).ToArray();
//        _data = tmpData.AppendChecksum16();
//    }
//    public static implicit operator byte[](Crc16Message d) => d._data;
//}
//public class QueryCommand : Command
//{
//    public override bool Execute(SerialPort port)
//    {
//        var msg = new Crc16Message(0x65, 01, 10);
//        port.DiscardInBuffer();
//        port.DiscardOutBuffer();
//        port.Write(msg);
 
//        var data = port.Read(9, TimeSpan.FromSeconds(5));
//        Console.WriteLine($" query got Got {data?.Length ?? 0} bytes : {data.ToHexString()}");
//        return true;
//    }
//}