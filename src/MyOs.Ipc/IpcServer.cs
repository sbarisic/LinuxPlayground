using System.Net.Sockets;
using System.Text;

namespace MyOs.Ipc;

public static class IpcServer
{
    public static async Task RunAsync(
        string socketPath,
        Func<IpcRequest, CancellationToken, Task<IpcResponse>> handler,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(backlog: 16);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket connection = await listener.AcceptAsync(cancellationToken);
                _ = Task.Run(() => HandleConnectionAsync(connection, handler, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task HandleConnectionAsync(
        Socket connection,
        Func<IpcRequest, CancellationToken, Task<IpcResponse>> handler,
        CancellationToken cancellationToken)
    {
        using Socket socket = connection;
        await using NetworkStream stream = new(socket, ownsSocket: false);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        IpcResponse response;
        try
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return;
            }

            IpcRequest request = IpcProtocol.ReadRequest(line);
            response = await handler(request, cancellationToken);
        }
        catch (Exception ex)
        {
            response = IpcResponse.Failure(0, ex.Message);
        }

        await writer.WriteLineAsync(IpcProtocol.WriteResponse(response).AsMemory(), cancellationToken);
    }
}
