using System.Text.Json.Nodes;

namespace MyOs.Ipc;

public sealed class IpcRequest
{
    public IpcRequest(int version, int id, string command, JsonObject args)
    {
        Version = version;
        Id = id;
        Command = command;
        Args = args;
    }

    public int Version { get; }

    public int Id { get; }

    public string Command { get; }

    public JsonObject Args { get; }
}
