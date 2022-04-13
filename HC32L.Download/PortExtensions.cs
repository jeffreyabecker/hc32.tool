namespace HC32L.Download;

public static class PortExtensions
{
    public static byte Checksum8(this IEnumerable<byte> data)
    {
        byte final = 0x00;
        foreach (var b in data)
        {
            final += b;

        }
        return final;
    }

    public static IEnumerable<byte> AppendChecksum8(this IEnumerable<byte> data)
    {
        byte final = 0x00;
        foreach (var b in data)
        {
            final += b;
            yield return b;
        }
        yield return final;
    }
    public static byte[] AppendChecksum8(this byte[] data)
    {
        byte[] result = new byte[data.Length + 1];
        var end = result.Length - 1;
        byte b = 0x00;
        for (var i = 0; i < data.Length; i++)
        {
            result[end] += data[i];
            result[i] = data[i];
        }
        return result;
    }

    public static void Write(this System.IO.Ports.SerialPort port, byte[] bytes)
    {        
        port.Write(bytes, 0, bytes.Length);
    }
    public static byte[]? Read(this System.IO.Ports.SerialPort port, int count, TimeSpan timeout)
    {
        port.ReceivedBytesThreshold = count;
        DateTime start = DateTime.Now;

        var recievedData = new byte[count];
        while (true)
        {
            if (port.BytesToRead >= count)
            {
                port.Read(recievedData, 0, count);
                return recievedData;
            }
            else if ((DateTime.Now - start) >= timeout)
            {
                var found = port.BytesToRead;
                port.Read(recievedData, 0, found);
                throw new Exception($"Failed to read {count} bytes, got {found}: {recievedData.ToHexString()} ");
            }
            Thread.Sleep(TimeSpan.FromMilliseconds(10));
        }

    }


    public static string ToHexString(this IEnumerable<byte>? data)
    {
        if (data == null)
        {
            return "";
        }
        return data.Select(x => x.ToString("X2"))
        .Aggregate(new System.Text.StringBuilder(), (a, c) => a.Append(c))
        .ToString();
    }
}