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
    private readonly Func<bool>? _gameRunningOverride;
    public string VaultPath { get; }

    public SaveVaultService(string? vaultPath = null, string? saveDirectoryOverride = null,
        Func<bool>? gameRunningOverride = null)
    {
        VaultPath = vaultPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlusOneTrainer", "SaveVault");
        _saveDirectoryOverride = saveDirectoryOverride;
        _gameRunningOverride = gameRunningOverride;
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

        var matches = candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => Directory.Exists(path) && File.Exists(Path.Combine(path, "users.dat"))).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException("发现多套存档，无法自动选择 / Multiple save folders found: " + string.Join("; ", matches));
        return matches.SingleOrDefault();
    }

    public BackupEntry CreateBackup(string reason, string? gameExecutable = null)
    {
        var source = LocateSaveDirectory(gameExecutable)
                     ?? throw new InvalidOperationException("No Plants vs. Zombies save directory was found.");
        RejectLinks(source);
        Directory.CreateDirectory(VaultPath);
        var safeReason = string.Concat(reason.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{safeReason}";
        var destination = Path.Combine(VaultPath, name);
        Directory.CreateDirectory(destination);

        var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles)
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: false);
            hashes[relative] = Hash(destinationFile);
        }

        var afterFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        if (!sourceFiles.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(afterFiles.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) ||
            hashes.Any(pair => !Hash(ResolveChildPath(source, pair.Key)).Equals(pair.Value, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("备份期间存档发生变化，请重试 / Saves changed during backup; retry.");

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
        EnsureGameClosed();
        EnsureInsideVault(backup.Path);
        Verify(backup.Path);
        var destination = LocateSaveDirectory(gameExecutable)
                          ?? throw new InvalidOperationException("No Plants vs. Zombies save directory was found.");
        RejectLinks(destination);
        var manifest = ReadManifest(backup.Path);
        ValidateManifest(manifest);
        if (!Path.GetFullPath(manifest.SourcePath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("备份来源与当前存档目录不符 / Backup belongs to a different save folder.");
        CreateBackup("before-restore", gameExecutable);
        var staging = destination + ".plus-one-stage-" + Guid.NewGuid().ToString("N");
        var previous = destination + ".plus-one-before-restore-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        foreach (var relative in manifest.Sha256.Keys)
        {
            var sourceFile = ResolveChildPath(backup.Path, relative);
            var destinationFile = ResolveChildPath(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
            if (!Hash(destinationFile).Equals(manifest.Sha256[relative], StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Restored file failed verification: {relative}");
        }
        EnsureGameClosed();
        // Same-volume directory renames avoid exposing partially copied saves.
        // Keep the previous directory for recovery, including files absent from the backup.
        Directory.Move(destination, previous);
        try { Directory.Move(staging, destination); }
        catch
        {
            Directory.Move(previous, destination);
            throw;
        }
    }

    public void Verify(string backupPath)
    {
        EnsureInsideVault(backupPath);
        RejectLinks(backupPath);
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

    private void EnsureGameClosed()
    {
        if (_gameRunningOverride is not null)
        {
            if (_gameRunningOverride())
                throw new InvalidOperationException("请先关闭游戏再恢复存档 / Close the game before restoring saves.");
            return;
        }
        foreach (var name in new[] { "PlantsVsZombies", "popcapgame1" })
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            var running = processes.Length != 0;
            foreach (var process in processes) process.Dispose();
            if (running) throw new InvalidOperationException("请先关闭游戏再恢复存档 / Close the game before restoring saves.");
        }
    }

    private static void RejectLinks(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count != 0)
        {
            var path = pending.Pop();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("存档目录不能包含链接 / Linked save paths are not supported: " + path);
            if ((attributes & FileAttributes.Directory) != 0)
                foreach (var entry in Directory.EnumerateFileSystemEntries(path)) pending.Push(entry);
        }
    }
}
