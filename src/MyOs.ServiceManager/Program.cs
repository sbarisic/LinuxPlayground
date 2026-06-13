using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MyOs;
using MyOs.Ipc;
using MyOs.Services;

namespace MyOs.ServiceManager;

internal static class Program
{
    private static async Task Main()
    {
        using SerialLogScope serialLog = SerialLogScope.TryOpen(SystemPaths.Serial);
        Console.WriteLine($"[ServiceManager] starting pid={Environment.ProcessId}");

        foreach (string mountPoint in CoreMounts.All)
        {
            Console.WriteLine(IsMounted(mountPoint)
                ? $"[ServiceManager] confirmed {mountPoint} mounted"
                : $"[ServiceManager] missing mount {mountPoint}");
        }

        using CancellationTokenSource shutdown = new();
        using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });
        using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });

        IReadOnlyList<ServiceManifest> manifests = LoadServiceManifests();
        ServiceRegistry registry = new(manifests);
        AppLauncher appLauncher = new();
        ShutdownCoordinator shutdownCoordinator = new(registry, shutdown);

        Task supervisor = registry.RunAsync(shutdown.Token);
        Task ipcServer = IpcServer.RunAsync(
            IpcPaths.ServiceManagerSocket,
            (request, _) => Task.FromResult(HandleRequest(request, registry, appLauncher, shutdownCoordinator)),
            shutdown.Token);
        Console.WriteLine($"[ServiceManager] listening on {IpcPaths.ServiceManagerSocket}");

        Console.WriteLine("[ServiceManager] idle");

        try
        {
            await Task.WhenAll(supervisor, ipcServer);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }

        Console.WriteLine("[ServiceManager] stopping");
    }

    static IpcResponse HandleRequest(
        IpcRequest request,
        ServiceRegistry registry,
        AppLauncher appLauncher,
        ShutdownCoordinator shutdownCoordinator)
    {
        return request.Command switch
        {
            "service.list" => IpcResponse.Success(request.Id, BuildServiceList(registry.Snapshots())),
            "service.start" => HandleServiceCommand(request, registry.Start),
            "service.stop" => HandleServiceCommand(request, registry.Stop),
            "service.restart" => HandleServiceCommand(request, registry.Restart),
            "system.status" => IpcResponse.Success(request.Id, BuildSystemStatus(registry.Snapshots())),
            "system.shutdown" => HandleShutdown(request, shutdownCoordinator),
            "app.list" => IpcResponse.Success(request.Id, BuildAppList(appLauncher.ListApps())),
            "app.run" => HandleAppRun(request, appLauncher),
            _ => IpcResponse.Failure(request.Id, $"unknown command: {request.Command}"),
        };
    }

    static IpcResponse HandleServiceCommand(IpcRequest request, Func<string, ServiceCommandResult> command)
    {
        string? name = request.Args.TryGetPropertyValue("name", out JsonNode? nameNode)
            ? nameNode?.GetValue<string>()
            : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return IpcResponse.Failure(request.Id, "missing service name");
        }

        ServiceCommandResult result = command(name);
        JsonObject data = new()
        {
            ["message"] = result.Message,
        };

        return result.Success
            ? IpcResponse.Success(request.Id, data)
            : IpcResponse.Failure(request.Id, result.Message);
    }

    static IpcResponse HandleShutdown(IpcRequest request, ShutdownCoordinator shutdownCoordinator)
    {
        string actionText = request.Args.TryGetPropertyValue("action", out JsonNode? actionNode)
            ? actionNode?.GetValue<string>() ?? string.Empty
            : string.Empty;

        ShutdownAction action = actionText.Equals("poweroff", StringComparison.OrdinalIgnoreCase)
            ? ShutdownAction.PowerOff
            : ShutdownAction.Reboot;

        string message = shutdownCoordinator.Request(action);
        return IpcResponse.Success(request.Id, new JsonObject
        {
            ["message"] = message,
        });
    }

    static IpcResponse HandleAppRun(IpcRequest request, AppLauncher appLauncher)
    {
        string? name = request.Args.TryGetPropertyValue("name", out JsonNode? nameNode)
            ? nameNode?.GetValue<string>()
            : null;

        AppRunResult result = appLauncher.Run(name);
        JsonObject data = new()
        {
            ["name"] = result.Name,
            ["pid"] = result.Pid,
            ["message"] = result.Message,
        };

        return result.Success
            ? IpcResponse.Success(request.Id, data)
            : IpcResponse.Failure(request.Id, result.Message);
    }

    static JsonObject BuildServiceList(IReadOnlyList<ServiceSnapshot> services)
    {
        JsonArray serviceItems = new();
        foreach (ServiceSnapshot service in services)
        {
            serviceItems.Add((JsonNode)new JsonObject
            {
                ["name"] = service.Name,
                ["exec"] = service.Exec,
                ["state"] = service.State,
                ["pid"] = service.Pid,
                ["restart"] = service.Restart,
                ["critical"] = service.Critical,
                ["lastExit"] = service.LastExit,
            });
        }

        return new JsonObject
        {
            ["services"] = serviceItems,
        };
    }

    static JsonObject BuildSystemStatus(IReadOnlyList<ServiceSnapshot> services)
    {
        JsonArray mounts = new();
        foreach (string mountPoint in CoreMounts.All)
        {
            mounts.Add((JsonNode)new JsonObject
            {
                ["path"] = mountPoint,
                ["mounted"] = IsMounted(mountPoint),
            });
        }

        return new JsonObject
        {
            ["uptimeSeconds"] = ReadUptimeSeconds(),
            ["totalServices"] = services.Count,
            ["runningServices"] = services.Count(service => service.State == ServiceStateText.Running),
            ["mounts"] = mounts,
        };
    }

    static JsonObject BuildAppList(IReadOnlyList<AppManifest> apps)
    {
        JsonArray appItems = new();
        foreach (AppManifest app in apps)
        {
            appItems.Add((JsonNode)new JsonObject
            {
                ["name"] = app.Name,
                ["id"] = app.Id,
                ["version"] = app.Version,
                ["kind"] = app.Kind,
                ["bundle"] = app.Bundle,
                ["entry"] = app.Entry,
            });
        }

        return new JsonObject
        {
            ["apps"] = appItems,
        };
    }

    static IReadOnlyList<ServiceManifest> LoadServiceManifests()
    {
        try
        {
            List<ServiceManifest> manifests = new();
            if (Directory.Exists(SystemPaths.SystemServices))
            {
                foreach (string manifestPath in Directory.EnumerateFiles(SystemPaths.SystemServices, "*.service.json").OrderBy(Path.GetFileName))
                {
                    manifests.Add(ServiceManifest.ReadFromFile(manifestPath));
                }
            }

            if (manifests.Count > 0)
            {
                Console.WriteLine($"[ServiceManager] loaded {manifests.Count} service manifest(s) from {SystemPaths.SystemServices}");
                return manifests;
            }

            Console.WriteLine($"[ServiceManager] no service manifests found in {SystemPaths.SystemServices}; using fallback Shell service");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceManager] failed to load service manifests: {ex.Message}");
            Console.WriteLine("[ServiceManager] using fallback Shell service");
        }

        return new[]
        {
        new ServiceManifest("Shell", SystemPaths.Shell, ServiceRestartPolicy.Always, critical: true),
    };
    }

    static bool IsMounted(string mountPoint)
    {
        try
        {
            foreach (string line in File.ReadLines(SystemPaths.ProcMounts))
            {
                string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 && fields[1] == mountPoint)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceManager] could not read {SystemPaths.ProcMounts}: {ex.Message}");
        }

        return false;
    }

    static double ReadUptimeSeconds()
    {
        try
        {
            string uptime = File.ReadAllText(SystemPaths.ProcUptime).Trim();
            string firstField = uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return double.Parse(firstField, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }
}

static class CoreMounts
{
    public static readonly string[] All =
    {
        SystemPaths.Dev,
        SystemPaths.Proc,
        SystemPaths.Sys,
        SystemPaths.Run,
        SystemPaths.Tmp,
    };
}

static class ServiceStateText
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Failed = "failed";
    public const string Stopping = "stopping";
}

sealed class ServiceRegistry
{
    private readonly IReadOnlyList<ServiceSupervisor> _services;

    public ServiceRegistry(IReadOnlyList<ServiceManifest> manifests)
    {
        _services = manifests.Select(manifest => new ServiceSupervisor(manifest)).ToArray();
    }

    public Task RunAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_services.Select(service => Task.Run(() => service.RunAsync(cancellationToken), cancellationToken)));

    public IReadOnlyList<ServiceSnapshot> Snapshots() =>
        _services.Select(service => service.Snapshot()).ToArray();

    public ServiceCommandResult Start(string name) =>
        Find(name)?.Start() ?? ServiceCommandResult.Failure($"unknown service: {name}");

    public ServiceCommandResult Stop(string name)
    {
        ServiceSupervisor? service = Find(name);
        if (service is null)
        {
            return ServiceCommandResult.Failure($"unknown service: {name}");
        }

        if (service.IsProtectedShell)
        {
            return ServiceCommandResult.Failure($"{service.Name} is protected and cannot be stopped yet");
        }

        return service.Stop();
    }

    public ServiceCommandResult Restart(string name) =>
        Find(name)?.Restart() ?? ServiceCommandResult.Failure($"unknown service: {name}");

    public async Task StopAllForShutdownAsync()
    {
        foreach (ServiceSupervisor service in _services.Reverse())
        {
            await service.StopForShutdownAsync();
        }
    }

    private ServiceSupervisor? Find(string name) =>
        _services.FirstOrDefault(service => service.IsNamed(name));
}

sealed class ServiceSupervisor
{
    private readonly object _gate = new();
    private readonly ServiceManifest _manifest;
    private Process? _process;
    private string _state = ServiceStateText.Stopped;
    private string? _lastExit;
    private bool _enabled = true;

    public ServiceSupervisor(ServiceManifest manifest)
    {
        _manifest = manifest;
    }

    public string Name => _manifest.Name;

    public bool IsProtectedShell =>
        string.Equals(_manifest.Name, "Shell", StringComparison.OrdinalIgnoreCase);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsEnabled())
            {
                await DelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                continue;
            }

            Console.WriteLine($"[ServiceManager] starting {_manifest.Exec}");

            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _manifest.Exec,
                    UseShellExecute = false,
                },
            };

            try
            {
                SetState(ServiceStateText.Starting, null, null);
                process.Start();
                SetState(ServiceStateText.Running, process, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceManager] failed to start {_manifest.Exec}: {ex.Message}");
                SetState(ServiceStateText.Failed, null, ex.Message);
                await DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            while (!cancellationToken.IsCancellationRequested && !process.WaitForExit(250))
            {
            }

            if (cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                KillIfRunning(process);
                await WaitForExitWithTimeoutAsync(process, TimeSpan.FromSeconds(1));
                KillIfRunning(process);
            }

            if (process.HasExited)
            {
                string exitText = DescribeExit(process.ExitCode);
                Console.WriteLine($"[ServiceManager] {_manifest.Name} {exitText}");
                SetState(ServiceStateText.Stopped, null, exitText);
            }

            if (!cancellationToken.IsCancellationRequested && ShouldRestart())
            {
                Console.WriteLine($"[ServiceManager] restarting {_manifest.Name} in 1 second");
                await DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
            }
            else if (_manifest.Restart != ServiceRestartPolicy.Always)
            {
                Disable();
            }
        }
    }

    public ServiceSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ServiceSnapshot(
                _manifest.Name,
                _manifest.Exec,
                _state,
                _process is { HasExited: false } process ? process.Id : null,
                _manifest.RestartText,
                _manifest.Critical,
                _lastExit);
        }
    }

    public ServiceCommandResult Start()
    {
        lock (_gate)
        {
            _enabled = true;
            if (_process is { HasExited: false })
            {
                return ServiceCommandResult.Ok($"{_manifest.Name} is already running");
            }
        }

        return ServiceCommandResult.Ok($"{_manifest.Name} will be started by its supervisor");
    }

    public ServiceCommandResult Stop()
    {
        Process? process;
        lock (_gate)
        {
            _enabled = false;
            _state = ServiceStateText.Stopping;
            process = _process is { HasExited: false } runningProcess ? runningProcess : null;
        }

        if (process is null)
        {
            SetState(ServiceStateText.Stopped, null, "stopped by request");
            return ServiceCommandResult.Ok($"{_manifest.Name} is already stopped");
        }

        _ = Task.Run(async () =>
        {
            KillIfRunning(process);
            await WaitForExitWithTimeoutAsync(process, TimeSpan.FromSeconds(1));
            KillIfRunning(process);
        });

        return ServiceCommandResult.Ok($"{_manifest.Name} stop requested");
    }

    public ServiceCommandResult Restart()
    {
        Process? process;
        lock (_gate)
        {
            _enabled = true;
            process = _process is { HasExited: false } runningProcess ? runningProcess : null;
        }

        if (process is null)
        {
            return ServiceCommandResult.Ok($"{_manifest.Name} is not running; supervisor will start it");
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            KillIfRunning(process);
            await WaitForExitWithTimeoutAsync(process, TimeSpan.FromSeconds(1));
            KillIfRunning(process);
        });

        return ServiceCommandResult.Ok($"{_manifest.Name} restart requested");
    }

    public async Task StopForShutdownAsync()
    {
        Process? process;
        lock (_gate)
        {
            _enabled = false;
            _state = ServiceStateText.Stopping;
            process = _process is { HasExited: false } runningProcess ? runningProcess : null;
        }

        if (process is null)
        {
            SetState(ServiceStateText.Stopped, null, "stopped for shutdown");
            return;
        }

        Console.WriteLine($"[ServiceManager] stopping {_manifest.Name}");
        KillIfRunning(process);
        await WaitForExitWithTimeoutAsync(process, TimeSpan.FromSeconds(2));
        KillIfRunning(process);
    }

    public bool IsNamed(string name) =>
        string.Equals(_manifest.Name, name, StringComparison.OrdinalIgnoreCase);

    private bool IsEnabled()
    {
        lock (_gate)
        {
            return _enabled;
        }
    }

    private bool ShouldRestart()
    {
        lock (_gate)
        {
            return _enabled && _manifest.Restart == ServiceRestartPolicy.Always;
        }
    }

    private void Disable()
    {
        lock (_gate)
        {
            _enabled = false;
        }
    }

    private void SetState(string state, Process? process, string? lastExit)
    {
        lock (_gate)
        {
            _state = state;
            _process = process;
            if (lastExit is not null)
            {
                _lastExit = lastExit;
            }
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceManager] failed to kill child process: {ex.Message}");
        }
    }

    private static async Task WaitForExitWithTimeoutAsync(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string DescribeExit(int exitCode)
    {
        if (exitCode >= 128)
        {
            return $"terminated by signal {exitCode - 128}";
        }

        return $"exited with status {exitCode}";
    }
}

sealed class AppLauncher
{
    private readonly object _gate = new();
    private readonly List<Process> _runningApps = new();

    public IReadOnlyList<AppManifest> ListApps()
    {
        List<AppManifest> apps = new();
        if (!Directory.Exists(SystemPaths.Apps))
        {
            return apps;
        }

        foreach (string appDirectory in Directory.EnumerateDirectories(SystemPaths.Apps, "*.app", SearchOption.TopDirectoryOnly))
        {
            try
            {
                apps.Add(ReadManifest(appDirectory));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceManager] skipping app manifest in {appDirectory}: {ex.Message}");
            }
        }

        apps.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return apps;
    }

    public AppRunResult Run(string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return AppRunResult.Failure(string.Empty, "missing app name");
        }

        if (requestedName.Contains('/') || requestedName.Contains('\\'))
        {
            return AppRunResult.Failure(requestedName, "app names cannot contain path separators");
        }

        AppManifest app;
        try
        {
            app = ResolveApp(requestedName);
        }
        catch (AppNotFoundException)
        {
            return AppRunResult.Failure(requestedName, $"app not found: {requestedName}");
        }
        catch (Exception)
        {
            return AppRunResult.Failure(requestedName, $"app manifest is invalid: {requestedName}");
        }

        string executable;
        try
        {
            executable = ResolveEntryPath(app);
        }
        catch (InvalidOperationException ex)
        {
            return AppRunResult.Failure(app.Name, ex.Message);
        }

        if (!File.Exists(executable))
        {
            return AppRunResult.Failure(app.Name, $"app entry not found: {app.Name}");
        }

        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = app.Bundle,
                UseShellExecute = false,
            },
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            return AppRunResult.Failure(app.Name, $"failed to start {app.Name}: {ex.Message}");
        }

        lock (_gate)
        {
            _runningApps.Add(process);
        }

        _ = Task.Run(() => MonitorAppAsync(app.Name, process));

        string message = $"{app.Name} started pid={process.Id}";
        Console.WriteLine($"[ServiceManager] {message}");
        return AppRunResult.Ok(app.Name, process.Id, message);
    }

    private async Task MonitorAppAsync(string name, Process process)
    {
        try
        {
            await process.WaitForExitAsync();
            Console.WriteLine($"[ServiceManager] {name} {DescribeExit(process.ExitCode)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceManager] failed to monitor {name}: {ex.Message}");
        }
        finally
        {
            lock (_gate)
            {
                _runningApps.Remove(process);
            }

            process.Dispose();
        }
    }

    private static AppManifest ResolveApp(string requestedName)
    {
        if (!Directory.Exists(SystemPaths.Apps))
        {
            throw new AppNotFoundException();
        }

        foreach (string appDirectory in Directory.EnumerateDirectories(SystemPaths.Apps, "*.app", SearchOption.TopDirectoryOnly))
        {
            string bundleName = Path.GetFileNameWithoutExtension(appDirectory);
            if (string.Equals(bundleName, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                return ReadManifest(appDirectory);
            }

            AppManifest app;
            try
            {
                app = ReadManifest(appDirectory);
            }
            catch
            {
                continue;
            }

            if (string.Equals(app.Name, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                return app;
            }
        }

        throw new AppNotFoundException();
    }

    private static AppManifest ReadManifest(string appDirectory)
    {
        string manifestPath = Path.Combine(appDirectory, "manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = document.RootElement;

        return new AppManifest(
            ReadRequiredString(root, "id"),
            ReadRequiredString(root, "name"),
            ReadRequiredString(root, "version"),
            ReadRequiredString(root, "entry"),
            ReadRequiredString(root, "kind"),
            appDirectory);
    }

    private static string ResolveEntryPath(AppManifest app)
    {
        if (Path.IsPathRooted(app.Entry))
        {
            throw new InvalidOperationException($"app entry escapes bundle: {app.Name}");
        }

        string bundle = Path.GetFullPath(app.Bundle);
        string executable = Path.GetFullPath(Path.Combine(bundle, app.Entry));
        string bundlePrefix = bundle.EndsWith(Path.DirectorySeparatorChar)
            ? bundle
            : bundle + Path.DirectorySeparatorChar;

        if (!executable.StartsWith(bundlePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"app entry escapes bundle: {app.Name}");
        }

        return executable;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new InvalidOperationException($"missing '{propertyName}'");
    }

    private static string DescribeExit(int exitCode)
    {
        if (exitCode >= 128)
        {
            return $"terminated by signal {exitCode - 128}";
        }

        return $"exited with status {exitCode}";
    }
}

sealed class ShutdownCoordinator
{
    private readonly ServiceRegistry _registry;
    private readonly CancellationTokenSource _shutdown;
    private int _requested;

    public ShutdownCoordinator(ServiceRegistry registry, CancellationTokenSource shutdown)
    {
        _registry = registry;
        _shutdown = shutdown;
    }

    public string Request(ShutdownAction action)
    {
        if (Interlocked.Exchange(ref _requested, 1) != 0)
        {
            return "shutdown is already in progress";
        }

        _ = Task.Run(async () =>
        {
            Console.WriteLine($"[ServiceManager] {Describe(action)} requested");
            await Task.Delay(250);
            await _registry.StopAllForShutdownAsync();
            Console.WriteLine($"[ServiceManager] signaling /init to {Describe(action)}");
            SignalInit(action);
            _shutdown.Cancel();
        });

        return $"{Describe(action)} requested";
    }

    private static string Describe(ShutdownAction action) =>
        action == ShutdownAction.PowerOff ? "poweroff" : "reboot";

    private static void SignalInit(ShutdownAction action)
    {
        using Process initctl = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = SystemPaths.InitControl,
                ArgumentList = { action == ShutdownAction.PowerOff ? "poweroff" : "reboot" },
                UseShellExecute = false,
            },
        };

        try
        {
            initctl.Start();
            initctl.WaitForExit(1000);
            if (!initctl.HasExited || initctl.ExitCode != 0)
            {
                Console.WriteLine("[ServiceManager] initctl did not acknowledge shutdown request");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceManager] failed to run {SystemPaths.InitControl}: {ex.Message}");
        }
    }
}

sealed record AppManifest(
    string Id,
    string Name,
    string Version,
    string Entry,
    string Kind,
    string Bundle);

sealed record AppRunResult(bool Success, string Name, int? Pid, string Message)
{
    public static AppRunResult Ok(string name, int pid, string message) => new(true, name, pid, message);

    public static AppRunResult Failure(string name, string message) => new(false, name, null, message);
}

sealed class AppNotFoundException : Exception
{
}

sealed record ServiceSnapshot(
    string Name,
    string Exec,
    string State,
    int? Pid,
    string Restart,
    bool Critical,
    string? LastExit);

sealed record ServiceCommandResult(bool Success, string Message)
{
    public static ServiceCommandResult Ok(string message) => new(true, message);

    public static ServiceCommandResult Failure(string message) => new(false, message);
}

enum ShutdownAction
{
    Reboot,
    PowerOff,
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
