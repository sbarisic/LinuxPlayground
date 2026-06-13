using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using MyOs;
using MyOs.Ipc;

using SerialLogScope serialLog = SerialLogScope.TryOpen(SystemPaths.Serial);
Console.WriteLine($"[devd] starting pid={Environment.ProcessId}");

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

Console.WriteLine($"[devd] listening on {SystemPaths.DeviceSocket}");
await IpcServer.RunAsync(
    SystemPaths.DeviceSocket,
    (request, _) => Task.FromResult(HandleRequest(request)),
    shutdown.Token);

Console.WriteLine("[devd] stopping");

static IpcResponse HandleRequest(IpcRequest request) =>
    request.Command switch
    {
        "device.list" => IpcResponse.Success(request.Id, BuildDeviceList()),
        _ => IpcResponse.Failure(request.Id, $"unknown command: {request.Command}"),
    };

static JsonObject BuildDeviceList()
{
    JsonArray devices = new();

    if (!Directory.Exists(SystemPaths.SysClass))
    {
        return new JsonObject
        {
            ["devices"] = devices,
        };
    }

    foreach (string classDirectory in Directory.EnumerateDirectories(SystemPaths.SysClass).OrderBy(Path.GetFileName))
    {
        string deviceClass = Path.GetFileName(classDirectory);
        foreach (string deviceDirectory in Directory.EnumerateDirectories(classDirectory).OrderBy(Path.GetFileName))
        {
            string name = Path.GetFileName(deviceDirectory);
            string? dev = ReadDeviceNumber(deviceDirectory);

            devices.Add((JsonNode)new JsonObject
            {
                ["name"] = name,
                ["class"] = deviceClass,
                ["sysPath"] = deviceDirectory,
                ["dev"] = dev,
            });
        }
    }

    return new JsonObject
    {
        ["devices"] = devices,
    };
}

static string? ReadDeviceNumber(string deviceDirectory)
{
    string devPath = Path.Combine(deviceDirectory, "dev");
    try
    {
        return File.Exists(devPath) ? File.ReadAllText(devPath).Trim() : null;
    }
    catch
    {
        return null;
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
