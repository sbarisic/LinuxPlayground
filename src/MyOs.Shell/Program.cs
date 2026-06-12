using System.Text;
using MyOs.Sdk;

using SerialLogScope serialLog = SerialLogScope.TryOpen("/dev/ttyS0");
ServiceManagerClient serviceManager = new();

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

    await RunCommandAsync(line.Trim(), serviceManager);
}

static async Task RunCommandAsync(string line, ServiceManagerClient serviceManager)
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
        arguments = line[(firstSpace + 1)..].Trim();
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
        case "services":
            await PrintServicesAsync(serviceManager);
            break;
        case "start":
            await RunServiceOperationAsync("start", arguments, serviceManager.StartServiceAsync);
            break;
        case "stop":
            await RunServiceOperationAsync("stop", arguments, serviceManager.StopServiceAsync);
            break;
        case "restart":
            await RunServiceOperationAsync("restart", arguments, serviceManager.RestartServiceAsync);
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
    Console.WriteLine("  help               Show this help");
    Console.WriteLine("  clear              Clear the console");
    Console.WriteLine("  echo <text>        Print text");
    Console.WriteLine("  services           Show services");
    Console.WriteLine("  start <service>    Start a service");
    Console.WriteLine("  stop <service>     Stop a service");
    Console.WriteLine("  restart <service>  Restart a service");
    Console.WriteLine("  mounts             Show mounted filesystems");
    Console.WriteLine("  pid                Show shell process ID");
    Console.WriteLine("  uptime             Show system uptime");
    Console.WriteLine("  reboot             Stub reboot command");
    Console.WriteLine("  poweroff           Stub poweroff command");
}

static async Task PrintServicesAsync(ServiceManagerClient serviceManager)
{
    try
    {
        IReadOnlyList<ServiceInfo> services = await serviceManager.ListServicesAsync();
        Console.WriteLine($"{"NAME",-14} {"STATE",-10} {"PID",-6} {"RESTART",-8} EXEC");
        foreach (ServiceInfo service in services)
        {
            string pid = service.Pid?.ToString() ?? "-";
            Console.WriteLine($"{service.Name,-14} {service.State,-10} {pid,-6} {service.Restart,-8} {service.Exec}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"services: could not contact ServiceManager: {ex.Message}");
    }
}

static async Task RunServiceOperationAsync(
    string command,
    string serviceName,
    Func<string, CancellationToken, Task<ServiceOperationResult>> operation)
{
    if (string.IsNullOrWhiteSpace(serviceName))
    {
        Console.WriteLine($"usage: {command} <service>");
        return;
    }

    try
    {
        ServiceOperationResult result = await operation(serviceName, CancellationToken.None);
        Console.WriteLine(result.Success ? result.Message : $"{command}: {result.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{command}: could not contact ServiceManager: {ex.Message}");
    }
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
