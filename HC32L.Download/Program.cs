namespace HC32L.Download;

using System.CommandLine;
public class Program
{
    public static async Task Main(string[] args)
    {
        await new ToolCommand().InvokeAsync(args);
    }
}

