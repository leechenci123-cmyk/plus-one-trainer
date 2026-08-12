using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace PlusOneTrainer.Services;

public sealed record BackupEntry(string Name, string Path, DateTime CreatedAt, string Reason)
{
    public override string ToString() => $"{CreatedAt:yyyy-MM-dd HH:mm:ss}  ·  {Reason}";
}

public sealed record BackupManifest(
    int FormatVersion,
    DateTime CreatedAtUtc,
    string Reason,
    string SourcePath,
    IReadOnlyDictionary<string, string> Sha256);

public sealed class SaveVaultService
{
    private const string ManifestName = "plus-one-backup.json";
    private readonly string? _saveDirectoryOverride;
    public string VaultPath { get; }

    public SaveVaultService(string? vaultPath = null, string? saveDirectoryOverride = null)
    {
        VaultPath = vaultPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlusOneTrainer", "SaveVault");
        _saveDirectoryOverride = saveDirectoryOverride;
    }

    public string? LocateSaveDirectory(string? gameExecutable = null)
    {
        if (!string.IsNullOrWhiteSpace(_saveDirectoryOverride))
            return Directory.Exists(_saveDirectoryOverride) ? Path.GetFullPath(_saveDirectoryOverride) : null;
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var candidates = new List<string>
        {
            Path.Combine(common, "PopCap Games", "PlantsVsZombies", "userdata"),
            Path.Combine(common, "Steam", "PlantsVsZombies", "userdata")
        };
        if (!string.IsNullOrWhiteSpace(gameExecutable))
        {
            var directory = Path.GetDirectoryName(gameExecutable);
            if (directory is not null)
                candidates.Add(Path.Combine(directory, "userdata"));
        }

        return candidates.FirstOrDefault(path =>
            Directory.Exists(path) && Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any());
    }

    public BackupEntry CreateBackup(string reason, string? gameExecutable = null)
    {
        var source = LocateSaveDirectory(gameExecutable)
                     ?? throw new InvalidOperationException("No Plants vs. Zombies save directory was found.");
        Directory.CreateDirectory(VaultPath);
        var safeReason = string.Concat(reason.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{safeReason}";
        var destination = Path.Combine(VaultPath, name);
        Directory.CreateDirectory(destination);

        var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: false);
            hashes[relative] = Hash(destinationFile);
        }

        var manifest = new BackupManifest(1, DateTime.UtcNow, reason, source, hashes);
        File.WriteAllText(Path.Combine(destination, ManifestName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Verify(destination);
        return new BackupEntry(name, destination, DateTime.Now, reason);
    }

    public IReadOnlyList<BackupEntry> ListBackups()
    {
        if (!Directory.Exists(VaultPath))
            return [];
        var result = new List<BackupEntry>();
        foreach (var directory in Directory.EnumerateDirectories(VaultPath))
        {
            try
            {
                var manifest = ReadManifest(directory);
                result.Add(new BackupEntry(Path.GetFileName(directory), directory,
                    manifest.CreatedAtUtc.ToLocalTime(), manifest.Reason));
            }
            catch { }
        }
        return result.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public void Restore(BackupEntry backup, string? gameExecutable = null)
    {
        EnsureInsideVault(backup.Path);
        Verify(backup.Path);
        var destination = LocateSaveDirectory(gameExecutable)
                          ?? throw new InvalidOperationException("No Plants vs. Zombies save directory was found.");
        CreateBackup("before-restore", gameExecutable);
        var manifest = ReadManifest(backup.Path);
        ValidateManifest(manifest);
        foreach (var relative in manifest.Sha256.Keys)
        {
            var sourceFile = ResolveChildPath(backup.Path, relative);
            var destinationFile = ResolveChildPath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
            if (!Hash(destinationFile).Equals(manifest.Sha256[relative], StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Restored file failed verification: {relative}");
        }
    }

    public void Verify(string backupPath)
    {
        EnsureInsideVault(backupPath);
        var manifest = ReadManifest(backupPath);
        ValidateManifest(manifest);
        foreach (var pair in manifest.Sha256)
        {
            var path = ResolveChildPath(backupPath, pair.Key);
            if (!File.Exists(path) || !Hash(path).Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Backup verification failed: {pair.Key}");
        }
    }

    private BackupManifest ReadManifest(string directory)
    {
        var json = File.ReadAllText(Path.Combine(directory, ManifestName));
        return JsonSerializer.Deserialize<BackupManifest>(json)
               ?? throw new InvalidDataException("Backup manifest is invalid.");
    }

    private static void ValidateManifest(BackupManifest manifest)
    {
        if (manifest.FormatVersion != 1 || manifest.Sha256 is null)
            throw new InvalidDataException("Unsupported or incomplete backup manifest.");
        foreach (var pair in manifest.Sha256)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value.Length != 64 ||
                !pair.Value.All(Uri.IsHexDigit))
                throw new InvalidDataException("Backup manifest contains an invalid path or SHA-256 value.");
        }
    }

    private static string ResolveChildPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("Backup manifest contains an absolute path.");
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup manifest path escapes its allowed directory.");
        return target;
    }

    private void EnsureInsideVault(string path)
    {
        var root = Path.GetFullPath(VaultPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected backup is outside the save vault.");
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
