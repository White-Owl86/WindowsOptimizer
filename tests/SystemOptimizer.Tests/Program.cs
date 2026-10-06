using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SystemOptimizer.Core;

if (args.Length > 0 && args[0] == "--fixture")
{
    switch (args[1])
    {
        case "echo": Console.Write(JsonSerializer.Serialize(args.Skip(2))); return 0;
        case "fail": Console.Error.Write("expected failure"); return 7;
        case "flood":
            Console.Out.Write(new string('o', 200_000));
            Console.Error.Write(new string('e', 200_000));
            return 0;
        case "hang":
            File.WriteAllText(args[2], Environment.ProcessId.ToString());
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
    }
}

var tests = new List<(string Name, Func<Task> Test)>();
void Test(string name, Action test) => tests.Add((name, () => { test(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> test) => tests.Add((name, test));
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
T Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T ex) { return ex; }
    throw new Exception($"Expected {typeof(T).Name}");
}
async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T ex) { return ex; }
    throw new Exception($"Expected {typeof(T).Name}");
}
string directory = Path.Combine(Path.GetTempPath(), "SystemOptimizerTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string Folder(string name) { string path = Path.Combine(directory, name); Directory.CreateDirectory(path); return path; }
string[] Fixture(params string[] options)
{
    string[] prefix = Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? [typeof(CommandRunner).Assembly.Location.Replace("SystemOptimizer.Core.dll", "SystemOptimizer.Tests.dll"), "--fixture"]
        : ["--fixture"];
    return prefix.Concat(options).ToArray();
}
var runner = new CommandRunner();
string executable = Environment.ProcessPath!;

AsyncTest("Command: argument boundaries and Unicode are preserved", async () => {
    string[] expected = ["with spaces", "\"quoted\"", "a&b;$(nothing)", "متن فارسی"];
    string result = await runner.RunAsync(executable, Fixture(["echo", .. expected]), TimeSpan.FromSeconds(10));
    Assert(JsonSerializer.Deserialize<string[]>(result)!.SequenceEqual(expected));
});
AsyncTest("Command: nonzero exit propagates stderr", async () => {
    var error = await ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(executable, Fixture("fail"), TimeSpan.FromSeconds(10)));
    Assert(error.Message.Contains("code 7") && error.Message.Contains("expected failure"));
});
AsyncTest("Command: concurrent output streams do not deadlock", async () => {
    string result = await runner.RunAsync(executable, Fixture("flood"), TimeSpan.FromSeconds(10));
    Assert(result.Length == 200_000);
});
AsyncTest("Command: timeout terminates the process", async () => {
    string pidFile = Path.Combine(directory, "timeout.pid");
    await ThrowsAsync<TimeoutException>(() => runner.RunAsync(executable, Fixture("hang", pidFile), TimeSpan.FromSeconds(2)));
    int pid = int.Parse(File.ReadAllText(pidFile));
    try { using var process = Process.GetProcessById(pid); Assert(process.HasExited, "Timed-out process is still running"); }
    catch (ArgumentException) { }
});
AsyncTest("Command: caller cancellation is distinguished from timeout", async () => {
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    await ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(executable,
        Fixture("hang", Path.Combine(directory, "cancel.pid")), TimeSpan.FromSeconds(10), cancellation.Token));
});
AsyncTest("Command: missing executable is reported", async () => {
    await ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(Path.Combine(directory, "missing.exe"), [], TimeSpan.FromSeconds(2)));
});

Test("Firefox: existing policies and update settings survive", () => {
    string path = Path.Combine(Folder("policy-existing"), "policies.json");
    const string original = "{\"custom\":42,\"policies\":{\"DisableAppUpdate\":false,\"Certificates\":{\"ImportEnterpriseRoots\":true},\"Preferences\":{\"custom.pref\":123}}}";
    File.WriteAllText(path, original);
    var result = FirefoxPolicy.Apply(path);
    Assert(result.Changed && result.BackupPath is not null);
    Assert(File.ReadAllText(result.BackupPath!) == original);
    var json = JsonNode.Parse(File.ReadAllText(path))!;
    Assert(json["custom"]!.GetValue<int>() == 42);
    Assert(!json["policies"]!["DisableAppUpdate"]!.GetValue<bool>());
    Assert(json["policies"]!["Preferences"]!["custom.pref"]!.GetValue<int>() == 123);
    Assert(json["policies"]!["Preferences"]!["browser.tabs.unloadOnLowMemory"]!["Value"]!.GetValue<bool>());
});
Test("Firefox: existing memory policy is not overwritten", () => {
    string path = Path.Combine(Folder("policy-locked"), "policies.json");
    const string original = "{\"policies\":{\"Preferences\":{\"browser.tabs.unloadOnLowMemory\":{\"Value\":false,\"Status\":\"locked\"}}}}";
    File.WriteAllText(path, original);
    Assert(!FirefoxPolicy.Apply(path).Changed);
    Assert(File.ReadAllText(path) == original);
});
Test("Firefox: malformed JSON is not replaced", () => {
    string path = Path.Combine(Folder("policy-invalid"), "policies.json");
    File.WriteAllText(path, "{invalid");
    Throws<JsonException>(() => FirefoxPolicy.Apply(path));
    Assert(File.ReadAllText(path) == "{invalid");
});
Test("Firefox: unsupported Preferences structure is not replaced", () => {
    string path = Path.Combine(Folder("policy-array"), "policies.json");
    const string original = "{\"policies\":{\"Preferences\":[]}}";
    File.WriteAllText(path, original);
    Throws<InvalidDataException>(() => FirefoxPolicy.Apply(path));
    Assert(File.ReadAllText(path) == original);
});
Test("Firefox: new policy does not disable browser updates", () => {
    string path = Path.Combine(Folder("policy-new"), "distribution", "policies.json");
    Assert(FirefoxPolicy.Apply(path).Changed);
    Assert(!File.ReadAllText(path).Contains("DisableAppUpdate"));
    Assert(!FirefoxPolicy.Apply(path).Changed);
});
Test("Firefox: linked policy is rejected", () => {
    string outside = Path.Combine(Folder("policy-target"), "original.json");
    File.WriteAllText(outside, "{}");
    string link = Path.Combine(Folder("policy-link"), "policies.json");
    File.CreateSymbolicLink(link, outside);
    Throws<IOException>(() => FirefoxPolicy.Apply(link));
    Assert(File.ReadAllText(outside) == "{}");
});

Test("Cleanup: recent files survive and only old files are deleted", () => {
    string root = Folder("cleanup-age");
    string old = Path.Combine(root, "old.tmp");
    string recent = Path.Combine(root, "recent.tmp");
    File.WriteAllText(old, "abc"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
    File.WriteAllText(recent, "recent");
    var result = TempCleaner.Clean(root, DateTime.UtcNow.AddDays(-7));
    Assert(!File.Exists(old) && File.Exists(recent));
    Assert(result.Deleted == 1 && result.BytesDeleted == 3);
});
Test("Cleanup: directories remain and old nested files can be cleaned", () => {
    string root = Folder("cleanup-nested");
    string nested = Path.Combine(root, "sub"); Directory.CreateDirectory(nested);
    string file = Path.Combine(nested, "old.tmp"); File.WriteAllText(file, "old");
    File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-10));
    TempCleaner.Clean(root, DateTime.UtcNow.AddDays(-7));
    Assert(!File.Exists(file) && Directory.Exists(nested));
});
Test("Cleanup: linked folders and files never reach outside the root", () => {
    string root = Folder("cleanup-links");
    string outside = Folder("cleanup-outside");
    string target = Path.Combine(outside, "old.tmp"); File.WriteAllText(target, "keep");
    File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(-10));
    Directory.CreateSymbolicLink(Path.Combine(root, "linked-folder"), outside);
    File.CreateSymbolicLink(Path.Combine(root, "linked-file"), target);
    var result = TempCleaner.Clean(root, DateTime.UtcNow.AddDays(-7));
    Assert(File.Exists(target) && File.ReadAllText(target) == "keep" && result.Deleted == 0);
});
Test("Cleanup: linked root is rejected", () => {
    string outside = Folder("root-link-target");
    string link = Path.Combine(directory, "root-link"); Directory.CreateSymbolicLink(link, outside);
    Throws<IOException>(() => TempCleaner.Clean(link, DateTime.UtcNow.AddDays(-7)));
});
Test("Cleanup: filesystem root is rejected", () => {
    Throws<ArgumentException>(() => TempCleaner.Clean(Path.GetPathRoot(directory)!, DateTime.UtcNow));
});
Test("Cleanup: protected runtime directories are preserved", () => {
    string root = Folder("cleanup-protected"); string preserved = Path.Combine(root, ".net");
    Directory.CreateDirectory(preserved); string file = Path.Combine(preserved, "runtime.dll");
    File.WriteAllText(file, "keep"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-10));
    Assert(TempCleaner.Clean(root, DateTime.UtcNow.AddDays(-7), [preserved]).Deleted == 0);
    Assert(File.Exists(file));
});
Test("Path boundaries: similarly named siblings are not children", () => {
    Assert(!FileSafety.IsWithin(Path.Combine(directory, "ab", "file"), Path.Combine(directory, "a")));
    Assert(FileSafety.IsWithin(Path.Combine(directory, "a", "file"), Path.Combine(directory, "a")));
});

AsyncTest("Operations: failures and skipped tasks never become success", async () => {
    var results = await OperationRunner.RunAsync([
        new("success", () => Task.FromResult("done")),
        new("failure", () => throw new IOException("failed command")),
        new("skip", () => throw new NotApplicableException("not installed")),
        new("after failure", () => Task.FromResult("still executed"))
    ]);
    Assert(results.Select(result => result.Status).SequenceEqual([
        OperationStatus.Success, OperationStatus.Failed, OperationStatus.Skipped, OperationStatus.Success]));
});
AsyncTest("Operations: empty selection performs no work", async () => {
    Assert((await OperationRunner.RunAsync([])).Count == 0);
});

int failed = 0;
try
{
    foreach (var test in tests)
    {
        try { await test.Test(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + "\n" + ex); }
    }
    Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
}
finally { Directory.Delete(directory, recursive: true); }
return failed == 0 ? 0 : 1;
