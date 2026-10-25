using RetainerRecall;

var root = Path.Combine(Path.GetTempPath(), "RetainerRecall-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
ConfigurationFile NewFile(string name) => new(Path.Combine(root, name + ".json"));
try
{
    double now = 0;
    var file = NewFile("settings");
    var errors = 0;
    var p = new ConfigurationPersistence(file, () => now, _ => errors++);
    for (int i = 0; i < 10; i++) { now += 60; p.Tick(); }
    Check(file.Saves == 0 && file.LogicalBytesWritten == 0, "unchanged 10 cycles: 0 saves / 0 bytes");
    for (int i = 0; i < 600; i++) { p.Config.ListingStackLimit = i + 1; p.Changed(); p.Tick(); }
    Check(file.Saves == 0, "600 UI changes do not write before boundary");
    now += 60; p.Tick();
    Check(file.Saves == 1, "600 UI changes produce one save");
    Console.WriteLine($"MEASURE 600 changes: {file.Saves} commit, {file.LogicalBytesWritten} logical bytes");
    var bytes = file.LogicalBytesWritten;
    p.Config.ListingStackLimit = 1; p.Changed(); p.Config.ListingStackLimit = 600; p.Changed(); now += 60; p.Tick();
    Check(file.Saves == 1 && file.LogicalBytesWritten == bytes, "change then revert: no write");
    p.Config.ListingStackLimit = 2; p.Changed(); p.Flush(); p.Flush();
    Check(file.Saves == 2 && file.Load().Config.ListingStackLimit == 2, "logout flush and duplicate flush");
    for (int i = 3; i <= 5; i++) { p.Config.ListingStackLimit = i; p.Changed(); now += 60; p.Tick(); }
    var backups = Directory.GetFiles(root, "settings.json.backup.*").OrderDescending().ToArray();
    Check(backups.Length == 3 && backups.Select(x => ConfigurationFile.Decode(File.ReadAllText(x)).ListingStackLimit).SequenceEqual(new[] { 4, 3, 2 }), "three previous valid generations");
    var before = backups.Select(File.ReadAllText).ToArray();
    var bytesBeforeFailure = file.LogicalBytesWritten;
    file.BeforeCommit = _ => throw new IOException("injected disk failure");
    p.Config.ListingStackLimit = 6; p.Changed();
    for (int i = 0; i < 10; i++) { now += 60; p.Tick(); for (int j = 0; j < 10; j++) p.Flush(); }
    Check(errors == 2 && file.Saves == 5 && Directory.GetFiles(root, "settings.json.backup.*").OrderDescending().Select(File.ReadAllText).SequenceEqual(before), "three failures stop retries, preserve generations; notify first and final only");
    var failedBytes = file.LogicalBytesWritten;
    p.Dispose(); now += 600; p.Tick(); p.Flush(); p.Changed();
    Check(file.LogicalBytesWritten == failedBytes, "stopped/disposed writer leaves no work");
    Check(!Directory.GetFiles(root, "*.tmp.*").Any(), "failed temporary files cleaned");
    Console.WriteLine($"MEASURE three failure attempts: {failedBytes - bytesBeforeFailure} logical bytes, 0 commits");

    File.WriteAllText(Path.Combine(root, "settings.json"), "{broken");
    File.WriteAllText(backups[0], "null");
    var recover = NewFile("settings");
    using (var restored = new ConfigurationPersistence(recover, () => now, _ => throw new Exception("unexpected failure")))
    {
        Check(restored.Recovered && restored.Config.ListingStackLimit == 3, "skip damaged newest backup and recover next valid generation");
        restored.Flush();
        Check(recover.Load().Config.ListingStackLimit == 3 && Directory.GetFiles(root, "settings.json.corrupt.*").Length == 1, "repair primary and preserve corrupt original");
    }
    Check(!recover.Load().Recovered, "healthy primary takes precedence over backups");
    var exit = NewFile("exit");
    var exiting = new ConfigurationPersistence(exit, () => now, _ => { });
    exiting.Config.Language = "ja"; exiting.Changed(); exiting.Dispose(); exiting.Dispose();
    now += 86400; exiting.Tick(); exiting.Flush();
    Check(exit.Saves == 1 && exit.Load().Config.Language == "ja", "exit flush once, zero writes after exit across midnight");

    var reentrant = NewFile("reentrant");
    using (var state = new ConfigurationPersistence(reentrant, () => now, _ => { }))
    {
        state.Config.ListingStackLimit = 4; state.Changed();
        reentrant.BeforeCommit = _ => { state.Config.ListingStackLimit = 5; state.Changed(); state.Flush(); };
        state.Flush(); Check(reentrant.Saves == 1 && reentrant.Load().Config.ListingStackLimit == 4, "reentrant flush cannot overlap snapshot write");
        reentrant.BeforeCommit = null; state.Flush();
        Check(reentrant.Saves == 2 && reentrant.Load().Config.ListingStackLimit == 5, "change during save remains pending");
        state.Config.ListingStackLimit = 6; state.Changed();
        Parallel.For(0, 20, _ => state.Flush());
        Check(reentrant.Saves == 3, "20 simultaneous flush calls commit once");
    }
    var bad = NewFile("bad"); File.WriteAllText(Path.Combine(root, "bad.json"), "{}");
    bool rejected = false; try { bad.Load(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected && File.ReadAllText(Path.Combine(root, "bad.json")) == "{}", "no valid generation: fail closed, preserve source");
    Check(!ConfigurationFile.Encode(new()).Contains("Prices"), "session transaction/price state is not serialized");
    var incomplete = ConfigurationFile.Encode(new()).Replace("\"Visible\": true,", "");
    rejected = false; try { ConfigurationFile.Decode(incomplete); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "structurally incomplete JSON is rejected");
    var legacy = Newtonsoft.Json.Linq.JObject.Parse(ConfigurationFile.Encode(new()));
    legacy.Remove("Language"); legacy["$type"] = "RetainerRecall.Configuration, RetainerRecall";
    Check(ConfigurationFile.Decode(legacy.ToString()).ListingStackLimit == 99, "legacy type metadata and missing language remain compatible");
    var recallOnly = Newtonsoft.Json.Linq.JObject.Parse(ConfigurationFile.Encode(new()));
    foreach (var key in new[] { "Language", "EnableListing", "ListingKey", "ListingDelaySeconds", "UseMarketbuddyLimit", "ListingStackLimit" }) recallOnly.Remove(key);
    recallOnly["Player"]!["DelaySeconds"] = 2.5;
    recallOnly["Retainer"]!["Visible"] = false;
    Configuration? upgraded = null;
    try { upgraded = ConfigurationFile.Decode(recallOnly.ToString()); } catch (InvalidDataException) { }
    Check(upgraded is { ListingStackLimit: 99 } && upgraded.Player.DelaySeconds == 2.5f && !upgraded.Retainer.Visible,
        "v0.1 recall-only configuration upgrades without losing button settings");
    var truncated = NewFile("truncated");
    truncated.Save(ConfigurationFile.Encode(new()));
    var original = File.ReadAllText(Path.Combine(root, "truncated.json"));
    truncated.BeforeCommit = temp => File.WriteAllText(temp, "{");
    rejected = false; try { truncated.Save(ConfigurationFile.Encode(new() { Language = "ja" })); } catch (Newtonsoft.Json.JsonException) { rejected = true; }
    Check(rejected && File.ReadAllText(Path.Combine(root, "truncated.json")) == original && !Directory.GetFiles(root, "truncated.json.backup.*").Any(), "invalid temporary file cannot replace primary or rotate backups");
    var boundary = NewFile("midnight");
    now = 86370;
    using (var state = new ConfigurationPersistence(boundary, () => now, _ => { }))
    {
        state.Config.Language = "ja"; state.Changed(); now = 86429; state.Tick();
        Check(boundary.Saves == 0, "midnight does not trigger premature write");
        now = 86430; state.Tick();
        Check(boundary.Saves == 1, "monotonic 60-second boundary crosses midnight");
        state.Config.Language = "en"; state.Changed(); state.Flush();
    }
    File.Delete(Path.Combine(root, "midnight.json"));
    Check(boundary.Load().Recovered && boundary.Load().Config.Language == "ja", "missing primary recovers valid backup after restart");
    var final = NewFile("final");
    var ending = new ConfigurationPersistence(final, () => now, _ => { });
    ending.Config.Language = "ja"; ending.Changed(); final.BeforeCommit = _ => throw new IOException("temporary failure");
    ending.Flush(); final.BeforeCommit = null; ending.Dispose();
    Check(final.Saves == 1, "Dispose attempts final flush after a transient failure without waiting 60 seconds");
    Console.WriteLine($"{checks} persistence checks passed");
}
finally { Directory.Delete(root, true); }
