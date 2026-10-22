using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RetainerRecall;

// Small synchronous snapshots: no worker, timer, queue or task can outlive Dispose.
public sealed class ConfigurationPersistence : IDisposable
{
    private readonly object gate = new();
    private readonly ConfigurationFile file;
    private readonly Func<double> clock;
    private readonly Action<Exception> failed;
    private string saved;
    private long revision;
    private long savedRevision;
    private double next;
    private int failures;
    private double retryAt;
    private bool writing;
    private bool disposed;
    public Configuration Config { get; }
    public bool Recovered { get; }

    public ConfigurationPersistence(ConfigurationFile file, Func<double> clock, Action<Exception> failed)
    {
        this.file = file; this.clock = clock; this.failed = failed;
        (Config, Recovered) = file.Load();
        saved = ConfigurationFile.Encode(Config);
        if (Recovered) saved = ""; // Repair on the next boundary, preserving the damaged primary.
        revision = Recovered ? 1 : 0;
        next = clock() + 60;
    }

    public void Changed() { lock (gate) { if (!disposed) revision++; } }
    public void Tick()
    {
        lock (gate)
        {
            if (disposed || clock() < next) return;
            next = clock() + 60;
            FlushCore();
        }
    }
    public void Flush() { lock (gate) { if (!disposed) FlushCore(); } }
    private void FlushCore(bool final = false)
    {
        if (writing || failures >= 3 || (!final && clock() < retryAt) || revision == savedRevision) return;
        writing = true;
        var captured = revision;
        try
        {
            var json = ConfigurationFile.Encode(Config);
            if (json != saved) file.Save(json);
            saved = json;
            savedRevision = captured; // A reentrant change during Save remains dirty.
            failures = 0;
        }
        catch (Exception e)
        {
            failures++;
            retryAt = clock() + 60;
            if (failures == 1 || failures == 3) failed(e);
        }
        finally { writing = false; }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            try { FlushCore(final: true); }
            finally { disposed = true; }
        }
    }
}

public sealed class ConfigurationFile(string path)
{
    private readonly string path = Path.GetFullPath(path);
    public long LogicalBytesWritten { get; private set; }
    public int Saves { get; private set; }
    // Isolated failure injection; production leaves this null.
    public Action<string>? BeforeCommit { get; set; }
    private string DirectoryPath => Path.GetDirectoryName(path)!;
    private string Prefix => Path.GetFileName(path) + ".backup.";

    public static string Encode(Configuration config) => JsonConvert.SerializeObject(config, Formatting.Indented,
        new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
    public static Configuration Decode(string json)
    {
        var root = JObject.Parse(json);
        if (root["Version"]?.Type != JTokenType.Integer || !ButtonShape(root["Player"]) || !ButtonShape(root["Retainer"]) ||
            root["EnableListing"]?.Type != JTokenType.Boolean || root["ListingStackLimit"]?.Type != JTokenType.Integer ||
            root["ListingKey"] == null || root["ListingDelaySeconds"] == null || root["UseMarketbuddyLimit"]?.Type != JTokenType.Boolean)
            throw new InvalidDataException("Invalid configuration structure.");
        // Never instantiate types supplied by JSON, including legacy Dalamud $type metadata.
        var c = root.ToObject<Configuration>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None }))!;
        if (c.Version != 1 || c.Player == null || c.Retainer == null || !Enum.IsDefined(c.ListingKey) ||
            !float.IsFinite(c.ListingDelaySeconds) || !Valid(c.Player) || !Valid(c.Retainer))
            throw new InvalidDataException("Invalid configuration values.");
        return c;
    }
    private static bool Valid(ButtonSettings b) => float.IsFinite(b.DelaySeconds) && float.IsFinite(b.Offset.X) && float.IsFinite(b.Offset.Y);
    private static bool ButtonShape(JToken? b) => b is JObject && b["Visible"]?.Type == JTokenType.Boolean &&
        b["DelaySeconds"] != null && b["Offset"] is JObject o && o["X"] != null && o["Y"] != null;
    private static bool TryRead(string candidate, out Configuration config)
    {
        try { config = Decode(File.ReadAllText(candidate)); return true; }
        catch (Exception e) when (e is JsonException or InvalidDataException) { config = null!; return false; }
    }
    private string[] Backups() => Directory.Exists(DirectoryPath)
        ? Directory.GetFiles(DirectoryPath, Prefix + "*.json").OrderByDescending(x => x, StringComparer.Ordinal).ToArray() : [];

    public (Configuration Config, bool Recovered) Load()
    {
        if (File.Exists(path) && TryRead(path, out var primary)) return (primary, false);
        foreach (var backup in Backups())
            if (TryRead(backup, out var recovered)) return (recovered, true);
        if (File.Exists(path) || Backups().Length != 0)
            throw new InvalidDataException("No valid configuration or backup; existing files were preserved.");
        return (new(), false);
    }

    public void Save(string json)
    {
        Decode(json);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes); LogicalBytesWritten += bytes.Length; stream.Flush(true);
            }
            BeforeCommit?.Invoke(temporary);
            Decode(File.ReadAllText(temporary));
            if (File.Exists(path))
            {
                var valid = TryRead(path, out _);
                // Immutable generation name: File.Replace creates the previous generation atomically.
                // A failed replacement never shuffles existing generations.
                var suffix = valid ? ".backup." : ".corrupt.";
                var generation = Backups().Select(x => long.TryParse(Path.GetFileName(x)[Prefix.Length..^5], out var n) ? n : 0).DefaultIfEmpty().Max() + 1;
                var old = valid ? path + suffix + generation.ToString("D20") + ".json" : path + suffix + Guid.NewGuid().ToString("N");
                File.Replace(temporary, path, old);
            }
            else File.Move(temporary, path);
            Saves++;
            // Cleanup is not part of commit success; extra backups are preferable to data loss.
            try
            {
                foreach (var old in Backups().Where(x => TryRead(x, out _)).Skip(3)) File.Delete(old);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
