using System.Text;

using SerialLogScope serialLog = SerialLogScope.TryOpen("/dev/ttyS0");
Console.WriteLine($"[Shell] starting pid={Environment.ProcessId}");
Console.WriteLine("Type 'help' for commands.");

while (true)
{
    Console.Write("myos> ");
    string? line = Console.ReadLine();
    if (line is null)
    {
        Console.WriteLine();
        return;
    }

    RunCommand(line.Trim());
}

static void RunCommand(string line)
{
    if (line.Length == 0)
    {
        return;
    }

    string command;
    string arguments;
    int firstSpace = line.IndexOf(' ');
    if (firstSpace < 0)
    {
        command = line;
        arguments = string.Empty;
    }
    else
    {
        command = line[..firstSpace];
        arguments = line[(firstSpace + 1)..];
    }

    switch (command)
    {
        case "help":
            PrintHelp();
            break;
        case "clear":
            Console.Write("\x1b[2J\x1b[H");
            break;
        case "echo":
            Console.WriteLine(arguments);
            break;
        case "mounts":
            PrintMounts();
            break;
        case "pid":
            Console.WriteLine(Environment.ProcessId);
            break;
        case "uptime":
            PrintUptime();
            break;
        case "reboot":
        case "poweroff":
            Console.WriteLine($"{command}: shutdown control is not implemented yet");
            break;
        default:
            Console.WriteLine($"{command}: unknown command. Type 'help' for commands.");
            break;
    }
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  help           Show this help");
    Console.WriteLine("  clear          Clear the console");
    Console.WriteLine("  echo <text>    Print text");
    Console.WriteLine("  mounts         Show mounted filesystems");
    Console.WriteLine("  pid            Show shell process ID");
    Console.WriteLine("  uptime         Show system uptime");
    Console.WriteLine("  reboot         Stub reboot command");
    Console.WriteLine("  poweroff       Stub poweroff command");
}

static void PrintMounts()
{
    try
    {
        foreach (string line in File.ReadLines("/proc/mounts"))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 3)
            {
                Console.WriteLine($"{fields[1]} {fields[2]}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"mounts: could not read /proc/mounts: {ex.Message}");
    }
}

static void PrintUptime()
{
    try
    {
        string uptime = File.ReadAllText("/proc/uptime").Trim();
        string firstField = uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        Console.WriteLine($"{firstField} seconds");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"uptime: could not read /proc/uptime: {ex.Message}");
    }
}

sealed class SerialLogScope : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly TextWriter _serialWriter;

    private SerialLogScope(TextWriter originalOut, TextWriter originalError, TextWriter serialWriter)
    {
        _originalOut = originalOut;
        _originalError = originalError;
        _serialWriter = serialWriter;
    }

    public static SerialLogScope TryOpen(string path)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            Stream stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            StreamWriter serialWriter = new(stream, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(new TeeTextWriter(originalOut, serialWriter));
            Console.SetError(new TeeTextWriter(originalError, serialWriter));
            return new SerialLogScope(originalOut, originalError, serialWriter);
        }
        catch
        {
            return new SerialLogScope(originalOut, originalError, TextWriter.Null);
        }
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _serialWriter.Dispose();
    }
}

sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _first;
    private readonly TextWriter _second;

    public TeeTextWriter(TextWriter first, TextWriter second)
    {
        _first = first;
        _second = second;
    }

    public override Encoding Encoding => _first.Encoding;

    public override void Write(char value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(string? value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _first.WriteLine(value);
        _second.WriteLine(value);
    }

    public override void Flush()
    {
        _first.Flush();
        _second.Flush();
    }
}
