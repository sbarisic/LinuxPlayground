using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

using SerialLogScope serialLog = SerialLogScope.TryOpen("/dev/ttyS0");
Console.WriteLine($"[ServiceManager] starting pid={Environment.ProcessId}");

foreach (string mountPoint in new[] { "/dev", "/proc", "/sys", "/run", "/tmp" })
{
    Console.WriteLine(IsMounted(mountPoint)
        ? $"[ServiceManager] confirmed {mountPoint} mounted"
        : $"[ServiceManager] missing mount {mountPoint}");
}

using ManualResetEventSlim shutdown = new(false);
using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
{
    context.Cancel = true;
    shutdown.Set();
});
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    shutdown.Set();
});

Console.WriteLine("[ServiceManager] idle");
SuperviseShell(shutdown);
Console.WriteLine("[ServiceManager] stopping");

static void SuperviseShell(ManualResetEventSlim shutdown)
{
    const string shellPath = "/system/Shell";

    while (!shutdown.IsSet)
    {
        Console.WriteLine($"[ServiceManager] starting {shellPath}");

        using Process shell = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                UseShellExecute = false,
            },
        };

        try
        {
            shell.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceManager] failed to start {shellPath}: {ex.Message}");
            shutdown.Wait(TimeSpan.FromSeconds(1));
            continue;
        }

        while (!shutdown.IsSet && !shell.WaitForExit(250))
        {
        }

        if (shutdown.IsSet && !shell.HasExited)
        {
            try
            {
                shell.Kill();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceManager] failed to stop Shell: {ex.Message}");
            }
        }

        if (shell.HasExited)
        {
            LogShellExit(shell.ExitCode);
        }

        if (!shutdown.IsSet)
        {
            Console.WriteLine("[ServiceManager] restarting Shell in 1 second");
            shutdown.Wait(TimeSpan.FromSeconds(1));
        }
    }
}

static void LogShellExit(int exitCode)
{
    if (exitCode >= 128)
    {
        Console.WriteLine($"[ServiceManager] Shell terminated by signal {exitCode - 128}");
        return;
    }

    Console.WriteLine($"[ServiceManager] Shell exited with status {exitCode}");
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
