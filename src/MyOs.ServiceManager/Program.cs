using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using MyOs.Ipc;

using SerialLogScope serialLog = SerialLogScope.TryOpen("/dev/ttyS0");
Console.WriteLine($"[ServiceManager] starting pid={Environment.ProcessId}");

foreach (string mountPoint in new[] { "/dev", "/proc", "/sys", "/run", "/tmp" })
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

ServiceSupervisor shell = new("Shell", "/system/Shell", "always", critical: true);

Task ipcServer = IpcServer.RunAsync(
    IpcPaths.ServiceManagerSocket,
    (request, _) => Task.FromResult(HandleRequest(request, shell)),
    shutdown.Token);
Console.WriteLine($"[ServiceManager] listening on {IpcPaths.ServiceManagerSocket}");

Console.WriteLine("[ServiceManager] idle");
await shell.RunAsync(shutdown.Token);

try
{
    await ipcServer;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}

Console.WriteLine("[ServiceManager] stopping");

static IpcResponse HandleRequest(IpcRequest request, ServiceSupervisor shell)
{
    return request.Command switch
    {
        "service.list" => IpcResponse.Success(request.Id, BuildServiceList(shell.Snapshot())),
        "service.start" => HandleServiceCommand(request, shell.Start),
        "service.stop" => HandleServiceCommand(request, shell.Stop),
        "service.restart" => HandleServiceCommand(request, shell.Restart),
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

static JsonObject BuildServiceList(ServiceSnapshot service)
{
    JsonObject item = new()
    {
        ["name"] = service.Name,
        ["exec"] = service.Exec,
        ["state"] = service.State,
        ["pid"] = service.Pid,
        ["restart"] = service.Restart,
        ["critical"] = service.Critical,
        ["lastExit"] = service.LastExit,
    };

    return new JsonObject
    {
        ["services"] = new JsonArray(item),
    };
}

static bool IsMounted(string mountPoint)
{
    try
    {
        foreach (string line in File.ReadLines("/proc/mounts"))
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
        Console.WriteLine($"[ServiceManager] could not read /proc/mounts: {ex.Message}");
    }

    return false;
}

sealed class ServiceSupervisor
{
    private readonly object _gate = new();
    private readonly string _name;
    private readonly string _exec;
    private readonly string _restart;
    private readonly bool _critical;
    private Process? _process;
    private string _state = "stopped";
    private string? _lastExit;

    public ServiceSupervisor(string name, string exec, string restart, bool critical)
    {
        _name = name;
        _exec = exec;
        _restart = restart;
        _critical = critical;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[ServiceManager] starting {_exec}");

            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _exec,
                    UseShellExecute = false,
                },
            };

            try
            {
                SetState("starting", null, null);
                process.Start();
                SetState("running", process, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceManager] failed to start {_exec}: {ex.Message}");
                SetState("failed", null, ex.Message);
                await DelayBeforeRestartAsync(cancellationToken);
                continue;
            }

            while (!cancellationToken.IsCancellationRequested && !process.WaitForExit(250))
            {
            }

            if (cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                TryKill(process);
            }

            if (process.HasExited)
            {
                string exitText = DescribeExit(process.ExitCode);
                Console.WriteLine($"[ServiceManager] {_name} {exitText}");
                SetState("stopped", null, exitText);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"[ServiceManager] restarting {_name} in 1 second");
                await DelayBeforeRestartAsync(cancellationToken);
            }
        }
    }

    public ServiceSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ServiceSnapshot(
                _name,
                _exec,
                _state,
                _process is { HasExited: false } process ? process.Id : null,
                _restart,
                _critical,
                _lastExit);
        }
    }

    public ServiceCommandResult Start(string name)
    {
        if (!IsKnownService(name))
        {
            return ServiceCommandResult.Failure($"unknown service: {name}");
        }

        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return ServiceCommandResult.Ok($"{_name} is already running");
            }
        }

        return ServiceCommandResult.Ok($"{_name} will be started by its supervisor");
    }

    public ServiceCommandResult Stop(string name)
    {
        if (!IsKnownService(name))
        {
            return ServiceCommandResult.Failure($"unknown service: {name}");
        }

        return ServiceCommandResult.Failure($"{_name} is protected and cannot be stopped yet");
    }

    public ServiceCommandResult Restart(string name)
    {
        if (!IsKnownService(name))
        {
            return ServiceCommandResult.Failure($"unknown service: {name}");
        }

        Process? process;
        lock (_gate)
        {
            process = _process is { HasExited: false } runningProcess ? runningProcess : null;
        }

        if (process is null)
        {
            return ServiceCommandResult.Ok($"{_name} is not running; supervisor will start it");
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            TryKill(process);
        });

        return ServiceCommandResult.Ok($"{_name} restart requested");
    }

    private bool IsKnownService(string name) =>
        string.Equals(name, _name, StringComparison.OrdinalIgnoreCase);

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

    private static async Task DelayBeforeRestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void TryKill(Process process)
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
            Console.WriteLine($"[ServiceManager] failed to stop child process: {ex.Message}");
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
