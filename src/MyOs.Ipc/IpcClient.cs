using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace MyOs.Ipc;

public static class IpcClient
{
    private static int s_nextId;

    public static Task<IpcResponse> SendAsync(
        string command,
        JsonObject? args = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(IpcPaths.ServiceManagerSocket, command, args, cancellationToken);

    public static async Task<IpcResponse> SendAsync(
        string socketPath,
        string command,
        JsonObject? args = null,
        CancellationToken cancellationToken = default)
    {
        int id = Interlocked.Increment(ref s_nextId);

        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);

        await using NetworkStream stream = new(socket, ownsSocket: false);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(IpcProtocol.WriteRequest(id, command, args).AsMemory(), cancellationToken);

        string? line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new IOException("IPC server closed the connection without a response");
        }

        IpcResponse response = IpcProtocol.ReadResponse(line);
        if (response.Id != id)
        {
            throw new IOException($"IPC response id {response.Id} did not match request id {id}");
        }

        return response;
    }
}
