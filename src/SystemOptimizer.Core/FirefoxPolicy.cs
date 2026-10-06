using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemOptimizer.Core;

public record PolicyResult(bool Changed, string? BackupPath);

public static class FirefoxPolicy
{
    public static PolicyResult Apply(string policyPath)
    {
        policyPath = Path.GetFullPath(policyPath);
        FileSafety.RejectLinks(policyPath);
        byte[]? original = File.Exists(policyPath) ? File.ReadAllBytes(policyPath) : null;
        var root = original is null ? new JsonObject() :
            JsonNode.Parse(original) as JsonObject ?? throw new InvalidDataException("Firefox policy must be a JSON object.");
        if (root.ContainsKey("policies") && root["policies"] is not JsonObject)
            throw new InvalidDataException("Existing Firefox policies have an unsupported structure; no changes made.");
        var policies = root["policies"] as JsonObject ?? new JsonObject();
        if (!root.ContainsKey("policies")) root["policies"] = policies;
        if (policies.ContainsKey("Preferences") && policies["Preferences"] is not JsonObject)
            throw new InvalidDataException("Existing Firefox Preferences are not an object; no changes made.");
        var preferences = policies["Preferences"] as JsonObject ?? new JsonObject();
        if (!policies.ContainsKey("Preferences")) policies["Preferences"] = preferences;
        // Respect an existing administrator policy rather than silently replacing it.
        if (preferences.ContainsKey("browser.tabs.unloadOnLowMemory")) return new(false, null);
        preferences["browser.tabs.unloadOnLowMemory"] = new JsonObject { ["Value"] = true, ["Status"] = "default" };
        Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
        string temporary = policyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? backup = original is null ? null : policyPath + "." + Guid.NewGuid().ToString("N") + ".bak";
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            FileSafety.RejectLinks(policyPath);
            if (original is null)
            {
                File.Move(temporary, policyPath, overwrite: false);
            }
            else
            {
                if (!File.ReadAllBytes(policyPath).AsSpan().SequenceEqual(original))
                    throw new IOException("Firefox policy changed during this operation; no changes made.");
                File.Replace(temporary, policyPath, backup);
            }
            return new(true, backup);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
