using System.Text.Json;

namespace MyOs.Services;

public sealed class ServiceManifest
{
    public ServiceManifest(string name, string exec, ServiceRestartPolicy restart, bool critical)
    {
        Name = name;
        Exec = exec;
        Restart = restart;
        Critical = critical;
    }

    public string Name { get; }

    public string Exec { get; }

    public ServiceRestartPolicy Restart { get; }

    public bool Critical { get; }

    public static ServiceManifest ReadFromFile(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        string name = ReadRequiredString(root, "name", path);
        string exec = ReadRequiredString(root, "exec", path);
        string restartText = ReadString(root, "restart") ?? "never";
        bool critical = ReadBool(root, "critical") ?? false;

        ServiceRestartPolicy restart = restartText.Equals("always", StringComparison.OrdinalIgnoreCase)
            ? ServiceRestartPolicy.Always
            : ServiceRestartPolicy.Never;

        return new ServiceManifest(name, exec, restart, critical);
    }

    public string RestartText => Restart == ServiceRestartPolicy.Always ? "always" : "never";

    private static string ReadRequiredString(JsonElement root, string propertyName, string path) =>
        ReadString(root, propertyName) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{path} is missing '{propertyName}'");

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? ReadBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
}
