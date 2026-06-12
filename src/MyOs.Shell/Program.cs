using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MyOs;
using MyOs.Sdk;

using SerialLogScope serialLog = SerialLogScope.TryOpen(SystemPaths.Serial);
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
        case "apps":
            PrintApps();
            break;
        case "run":
            await RunAppAsync(arguments);
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
    Console.WriteLine("  apps               Show installed apps");
    Console.WriteLine("  run <app>          Run an app");
    Console.WriteLine("  start <service>    Start a service");
    Console.WriteLine("  stop <service>     Stop a service");
    Console.WriteLine("  restart <service>  Restart a service");
    Console.WriteLine("  mounts             Show mounted filesystems");
    Console.WriteLine("  pid                Show shell process ID");
    Console.WriteLine("  uptime             Show system uptime");
    Console.WriteLine("  reboot             Stub reboot command");
    Console.WriteLine("  poweroff           Stub poweroff command");
}

static void PrintApps()
{
    try
    {
        IReadOnlyList<AppManifest> apps = LoadApps();
        if (apps.Count == 0)
        {
            Console.WriteLine("No apps installed.");
            return;
        }

        Console.WriteLine($"{"NAME",-14} {"ID",-18} {"VERSION",-8} KIND");
        foreach (AppManifest app in apps)
        {
            Console.WriteLine($"{app.Name,-14} {app.Id,-18} {app.Version,-8} {app.Kind}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"apps: could not list apps: {ex.Message}");
    }
}

static async Task RunAppAsync(string appName)
{
    if (string.IsNullOrWhiteSpace(appName))
    {
        Console.WriteLine("usage: run <app>");
        return;
    }

    if (appName.Contains('/') || appName.Contains('\\'))
    {
        Console.WriteLine("run: app names cannot contain path separators");
        return;
    }

    string appDirectory = Path.Combine(SystemPaths.Apps, $"{appName}.app");
    string manifestPath = Path.Combine(appDirectory, "manifest.json");
    if (!File.Exists(manifestPath))
    {
        Console.WriteLine($"run: app not found: {appName}. Type 'apps' to list apps.");
        return;
    }

    AppManifest manifest;
    try
    {
        manifest = ReadManifest(manifestPath, appDirectory);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"run: could not read {appName} manifest: {ex.Message}");
        return;
    }

    string executable = Path.Combine(appDirectory, manifest.Entry);
    if (!File.Exists(executable))
    {
        Console.WriteLine($"run: app entry not found: {manifest.Entry}");
        return;
    }

    using Process app = new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = appDirectory,
            UseShellExecute = false,
        },
    };

    try
    {
        app.Start();
        await app.WaitForExitAsync();
        Console.WriteLine($"{manifest.Name} exited with status {app.ExitCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"run: failed to start {manifest.Name}: {ex.Message}");
    }
}

static IReadOnlyList<AppManifest> LoadApps()
{
    List<AppManifest> apps = new();
    if (!Directory.Exists(SystemPaths.Apps))
    {
        return apps;
    }

    foreach (string manifestPath in Directory.EnumerateFiles(SystemPaths.Apps, "manifest.json", SearchOption.AllDirectories))
    {
        string? appDirectory = Path.GetDirectoryName(manifestPath);
        if (appDirectory is null || !appDirectory.EndsWith(".app", StringComparison.Ordinal))
        {
            continue;
        }

        try
        {
            apps.Add(ReadManifest(manifestPath, appDirectory));
        }
        catch
        {
        }
    }

    apps.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
    return apps;
}

static AppManifest ReadManifest(string manifestPath, string appDirectory)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    JsonElement root = document.RootElement;

    return new AppManifest(
        ReadString(root, "id"),
        ReadString(root, "name"),
        ReadString(root, "version"),
        ReadString(root, "entry"),
        ReadString(root, "kind"),
        appDirectory);
}

static string ReadString(JsonElement root, string propertyName)
{
    if (root.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String)
    {
        return property.GetString() ?? string.Empty;
    }

    return string.Empty;
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
        foreach (string line in File.ReadLines(SystemPaths.ProcMounts))
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
        Console.WriteLine($"mounts: could not read {SystemPaths.ProcMounts}: {ex.Message}");
    }
}

static void PrintUptime()
{
    try
    {
        string uptime = File.ReadAllText(SystemPaths.ProcUptime).Trim();
        string firstField = uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        Console.WriteLine($"{firstField} seconds");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"uptime: could not read {SystemPaths.ProcUptime}: {ex.Message}");
    }
}

sealed record AppManifest(
    string Id,
    string Name,
    string Version,
    string Entry,
    string Kind,
    string Directory);

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
