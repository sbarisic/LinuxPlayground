using System.Text;
using MyOs;
using MyOs.Sdk;

namespace MyOs.Shell;

internal static class Program
{
    private static async Task Main()
    {
        using SerialLogScope serialLog = SerialLogScope.TryOpen(SystemPaths.Serial);
        ServiceManagerClient serviceManager = new();
        DeviceManagerClient deviceManager = new();
        AppManagerClient appManager = new();

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

            await RunCommandAsync(line.Trim(), serviceManager, deviceManager, appManager);
        }
    }

    static async Task RunCommandAsync(
        string line,
        ServiceManagerClient serviceManager,
        DeviceManagerClient deviceManager,
        AppManagerClient appManager)
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
            case "status":
                await PrintStatusAsync(serviceManager, appManager);
                break;
            case "services":
                await PrintServicesAsync(serviceManager);
                break;
            case "apps":
                await PrintAppsAsync(appManager);
                break;
            case "run":
                await RunAppAsync(arguments, appManager);
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
            case "devices":
                await PrintDevicesAsync(deviceManager);
                break;
            case "pid":
                Console.WriteLine(Environment.ProcessId);
                break;
            case "uptime":
                PrintUptime();
                break;
            case "reboot":
                await RequestShutdownAsync("reboot", ShutdownAction.Reboot, serviceManager);
                break;
            case "poweroff":
                await RequestShutdownAsync("poweroff", ShutdownAction.PowerOff, serviceManager);
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
        Console.WriteLine("  status             Show system status");
        Console.WriteLine("  services           Show services");
        Console.WriteLine("  apps               Show installed apps");
        Console.WriteLine("  run <app>          Run an app");
        Console.WriteLine("  start <service>    Start a service");
        Console.WriteLine("  stop <service>     Stop a service");
        Console.WriteLine("  restart <service>  Restart a service");
        Console.WriteLine("  mounts             Show mounted filesystems");
        Console.WriteLine("  devices            Show devices");
        Console.WriteLine("  pid                Show shell process ID");
        Console.WriteLine("  uptime             Show system uptime");
        Console.WriteLine("  reboot             Reboot the system");
        Console.WriteLine("  poweroff           Power off the system");
    }

    static async Task PrintAppsAsync(AppManagerClient appManager)
    {
        try
        {
            IReadOnlyList<AppInfo> apps = await appManager.ListAppsAsync();
            if (apps.Count == 0)
            {
                Console.WriteLine("No apps installed.");
                return;
            }

            Console.WriteLine($"{"NAME",-14} {"ID",-18} {"VERSION",-8} KIND");
            foreach (AppInfo app in apps)
            {
                Console.WriteLine($"{app.Name,-14} {app.Id,-18} {app.Version,-8} {app.Kind}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"apps: could not list apps: {ex.Message}");
        }
    }

    static async Task RunAppAsync(string appName, AppManagerClient appManager)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            Console.WriteLine("usage: run <app>");
            return;
        }

        try
        {
            AppRunResult result = await appManager.RunAppAsync(appName);
            Console.WriteLine(result.Success ? result.Message : $"run: {result.Message}. Type 'apps' to list apps.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"run: could not contact ServiceManager: {ex.Message}");
        }
    }

    static async Task PrintStatusAsync(ServiceManagerClient serviceManager, AppManagerClient appManager)
    {
        try
        {
            SystemStatusInfo status = await serviceManager.GetSystemStatusAsync();
            IReadOnlyList<AppInfo> apps = await appManager.ListAppsAsync();

            Console.WriteLine($"uptime: {status.UptimeSeconds:0.00} seconds");
            Console.WriteLine($"services: {status.RunningServices}/{status.TotalServices} running");
            Console.WriteLine($"apps: {apps.Count}");
            Console.WriteLine("mounts:");
            foreach (MountStatusInfo mount in status.Mounts)
            {
                Console.WriteLine($"  {mount.Path,-8} {(mount.Mounted ? "mounted" : "missing")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"status: could not contact ServiceManager: {ex.Message}");
        }
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

    static async Task PrintDevicesAsync(DeviceManagerClient deviceManager)
    {
        try
        {
            IReadOnlyList<DeviceInfo> devices = await deviceManager.ListDevicesAsync();
            if (devices.Count == 0)
            {
                Console.WriteLine("No devices found.");
                return;
            }

            Console.WriteLine($"{"CLASS",-14} {"NAME",-22} {"DEV",-8} SYSPATH");
            foreach (DeviceInfo device in devices)
            {
                Console.WriteLine($"{device.Class,-14} {device.Name,-22} {device.Dev ?? "-",-8} {device.SysPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"devices: could not contact devd: {ex.Message}");
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

    static async Task RequestShutdownAsync(
        string command,
        ShutdownAction action,
        ServiceManagerClient serviceManager)
    {
        try
        {
            ServiceOperationResult result = await serviceManager.RequestShutdownAsync(action);
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
