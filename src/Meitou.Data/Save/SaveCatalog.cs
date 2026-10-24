namespace Meitou.Data.Save;

public sealed record SaveSlot(string Name, string Folder, DateTime ModifiedUtc, bool Writable);
public sealed record SaveSlotInfo(string Version, string Faction, int Funds, int Members, int Day, int Hour, int Minute, string Area);

/// <summary>Menu save discovery and safe named slots. Reading an original slot never makes it a write destination.</summary>
public static class SaveCatalog
{
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() || name.EndsWith('.') ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || name is "." or "..")
            throw new ArgumentException("Use a name of 1-80 characters without path separators or trailing dots/spaces.", nameof(name));
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9')
            throw new ArgumentException("That name is reserved by the operating system.", nameof(name));
        return name;
    }
    public static string Folder(string root, string name) => Path.Combine(Path.GetFullPath(root), ValidateName(name));
    public static IReadOnlyList<SaveSlot> Scan(string writableRoot, params string[] readOnlyRoots)
    {
        var found = new Dictionary<string, SaveSlot>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, writable) in new[] { (writableRoot, true) }.Concat(readOnlyRoots.Select(r => (r, false))))
        {
            if (!Directory.Exists(root)) continue;
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                string folder = Path.GetFullPath(directory), quick = Path.Combine(folder, SaveFolder.QuickSaveName);
                if (File.Exists(quick)) found.TryAdd(folder, new(Path.GetFileName(folder), folder, File.GetLastWriteTimeUtc(quick), writable));
            }
        }
        return found.Values.OrderByDescending(s => s.ModifiedUtc).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    /// <summary>
    /// The folder of the save the original's Continue loads: <c>settings.cfg</c> <c>continue</c> in the save folder <c>User save location</c> picks
    /// (docs/formats/save.md); null without a <c>continue</c> name.
    /// </summary>
    public static string? OriginalContinue(Meitou.Content.GameInstall install)
    {
        var settings = install.ReadSettings();
        if (settings.GetValueOrDefault("continue") is not { Length: > 0 } name) return null;
        bool user = settings.TryGetValue("User save location", out var flag)
            ? flag is not ("0" or "false" or "no")
            : !(Directory.Exists(install.SaveDirectory) && Directory.EnumerateDirectories(install.SaveDirectory).Any(d => File.Exists(Path.Combine(d, SaveFolder.QuickSaveName))));
        return Path.Combine(user ? Meitou.Content.GameInstall.UserSaveDirectory : install.SaveDirectory, name);
    }

    /// <summary>Whether the original loads a save of this version ("a.b.c", at least 0.99.0); an empty or unparsable version counts as 0.</summary>
    public static bool LoadableVersion(string version)
    {
        var m = System.Text.RegularExpressions.Regex.Match(version, @"^\s*([+-]?\d+)\.\s*([+-]?\d+)\.\s*([+-]?\d+)");
        uint code = m.Success && int.TryParse(m.Groups[1].Value, out int a) && int.TryParse(m.Groups[2].Value, out int b) && int.TryParse(m.Groups[3].Value, out int c)
            ? (uint)(c | (b | a << 12) << 12) : 0;
        return code >= 99u << 12;
    }

    public static SaveSlotInfo ReadInfo(SaveSlot slot)
    {
        var game = new SaveGame(SaveFile.Read(File.ReadAllBytes(Path.Combine(slot.Folder, SaveFolder.QuickSaveName))));
        var c = game.Camera;
        return new(c.Version, c.PlayerFactionName, c.PlayerMoney, c.Members, c.Day, c.Hour, c.Minute, c.Area);
    }
}
