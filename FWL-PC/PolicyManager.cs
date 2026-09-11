using Microsoft.Win32;
using System.Text.Json;

namespace FwlPc;

public sealed class PolicyManager
{
    private const string AppDataDir = @"C:\ProgramData\FWL PC";
    private const string BackupFile = AppDataDir + "\policy-backup.json";
    private const string SettingsFile = AppDataDir + "\settings.json";

    private static readonly BrowserPolicy[] Browsers =
    [
        new("Google Chrome", @"SOFTWARE\Policies\Google\Chrome"),
        new("Microsoft Edge", @"SOFTWARE\Policies\Microsoft\Edge"),
        // Opera is Chromium-based. Opera publishes fewer Windows policy docs than Chrome/Edge,
        // so we target the standard Opera Stable machine policy location as a best-effort adapter.
        new("Opera", @"SOFTWARE\Policies\Opera Software\Opera Stable")
    ];

    public AppSettings LoadSettings()
    {
        Directory.CreateDirectory(AppDataDir);
        if (!File.Exists(SettingsFile))
            return new AppSettings { Enabled = false, AllowedSites = ["google.com", "canva.com"] };

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile))
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    public void Enable(IReadOnlyCollection<string> domains)
    {
        Directory.CreateDirectory(AppDataDir);
        var backup = LoadBackup() ?? new PolicyBackup();

        foreach (var browser in Browsers)
        {
            var blockKey = browser.Root + @"\URLBlocklist";
            var allowKey = browser.Root + @"\URLAllowlist";

            if (!backup.Browsers.ContainsKey(browser.Name))
            {
                backup.Browsers[browser.Name] = new BrowserBackup
                {
                    Blocklist = ReadValues(blockKey),
                    Allowlist = ReadValues(allowKey)
                };
            }

            WriteValues(blockKey, new Dictionary<string, string> { ["1"] = "*" });
            var allowValues = new Dictionary<string, string>();
            var index = 1;
            foreach (var domain in domains.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                allowValues[index++.ToString()] = $"*://{domain}/*";
                allowValues[index++.ToString()] = $"*://*.{domain}/*";
            }

            if (allowValues.Count == 0)
                DeleteTree(allowKey);
            else
                WriteValues(allowKey, allowValues);
        }

        File.WriteAllText(BackupFile, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Disable()
    {
        var backup = LoadBackup();
        if (backup is null)
        {
            foreach (var browser in Browsers)
            {
                DeleteTree(browser.Root + @"\URLBlocklist");
                DeleteTree(browser.Root + @"\URLAllowlist");
            }
            return;
        }

        foreach (var browser in Browsers)
        {
            if (!backup.Browsers.TryGetValue(browser.Name, out var state))
                continue;

            RestoreValues(browser.Root + @"\URLBlocklist", state.Blocklist);
            RestoreValues(browser.Root + @"\URLAllowlist", state.Allowlist);
        }

        try { File.Delete(BackupFile); } catch { }
    }

    public bool IsBackupPresent() => File.Exists(BackupFile);

    private static PolicyBackup? LoadBackup()
    {
        try
        {
            if (!File.Exists(BackupFile)) return null;
            return JsonSerializer.Deserialize<PolicyBackup>(File.ReadAllText(BackupFile));
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ReadValues(string path)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
            if (key is null) return result;
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string value)
                    result[name] = value;
            }
        }
        catch { }
        return result;
    }

    private static void WriteValues(string path, Dictionary<string, string> values)
    {
        DeleteTree(path);
        using var key = Registry.LocalMachine.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Unable to create registry key: {path}");
        foreach (var pair in values)
            key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
    }

    private static void RestoreValues(string path, Dictionary<string, string> values)
    {
        if (values.Count == 0)
            DeleteTree(path);
        else
            WriteValues(path, values);
    }

    private static void DeleteTree(string path)
    {
        try { Registry.LocalMachine.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { }
    }

    private sealed record BrowserPolicy(string Name, string Root);
}

public sealed class AppSettings
{
    public bool Enabled { get; set; }
    public List<string> AllowedSites { get; set; } = [];
}

public sealed class PolicyBackup
{
    public Dictionary<string, BrowserBackup> Browsers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BrowserBackup
{
    public Dictionary<string, string> Blocklist { get; set; } = new();
    public Dictionary<string, string> Allowlist { get; set; } = new();
}
