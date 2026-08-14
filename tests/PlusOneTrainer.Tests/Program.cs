using PlusOneTrainer.Core;
using PlusOneTrainer.Models;
using System.IO;

if (args.SequenceEqual(["--live-attach-probe"]))
{
    var result = GameSession.TryAttach();
    Console.WriteLine($"{result.State}: {result.Details}");
    result.Session?.Dispose();
    return result.State == AttachmentState.Attached ? 0 : 1;
}

var tests = new (string Name, Action Run)[]
{
    ("Supported profile constants", ProfileConstants),
    ("Difficulty normalization", DifficultyNormalization),
    ("x86 relative CALL encoding", RelativeCallEncoding),
    ("Zombie catalog completeness", ZombieCatalogCompleteness),
    ("Masked byte pattern", MaskedPattern),
    ("Advanced pause fails closed", AdvancedPauseFailClosed),
    ("Difficulty math boundaries", DifficultyMathBoundaries),
    ("Difficulty rejects non-finite input", DifficultyRejectsNonFinite),
    ("Localization keys match", LocalizationKeysMatch),
    ("Save vault round trip", SaveVaultRoundTrip),
    ("Save vault blocks traversal", SaveVaultBlocksTraversal),
    ("Profile patch invariants", ProfilePatchInvariants),
    ("Health bar durability math", HealthBarDurabilityMath),
    ("Main window resource references", MainWindowResourceReferences),
    ("Steam runtime timestamp gate", SteamRuntimeTimestampGate),
    ("Wallet money math", WalletMoneyMath)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void ProfileConstants()
{
    var profile = new GameVersionProfile();
    Equal(0x00731C50u, profile.LawnPointer);
    Equal(0x868u, profile.Board);
    Equal(0x54u, profile.Money);
    Equal(0x138u, profile.GridItemCountMax);
    Equal(0x0042DCE0u, profile.CallPutZombie);
    Equal(0x00411290u, profile.CallPutZombieInRow);
    Equal(0x0040C420u, profile.CallPutLadder);
}

static void DifficultyNormalization()
{
    var input = DifficultySettings.Default with
    {
        SpawnCountMultiplier = 99,
        SpawnSpeedMultiplier = 0,
        DurabilityMultiplier = 5000,
        StartIndex = -3
    };
    var output = input.Normalize();
    Equal(10d, output.SpawnCountMultiplier);
    Equal(0.25d, output.SpawnSpeedMultiplier);
    Equal(1000d, output.DurabilityMultiplier);
    Equal(1, output.StartIndex);
}

static void RelativeCallEncoding()
{
    var bytes = new X86CodeBuilder().Call(0x00402000).Ret().Build(0x00401000);
    Equal((byte)0xE8, bytes[0]);
    Equal(0xFFBu, BitConverter.ToUInt32(bytes, 1));
    Equal((byte)0xC3, bytes[5]);
}

static void ZombieCatalogCompleteness()
{
    Equal(33, ZombieOption.All.Count);
    EqualSequence(Enumerable.Range(0, 33).ToArray(), ZombieOption.All.Select(x => x.Id).ToArray());
    Equal("Zombie Yeti", ZombieOption.All[19].English);
    Equal("Giga-Gargantuar", ZombieOption.All[32].English);
}

static void MaskedPattern()
{
    var pattern = MaskedBytePattern.Parse("AA ?? CC");
    EqualSequence(new[] { 0, 3 }, pattern.FindAll(new byte[] { 0xAA, 0x10, 0xCC, 0xAA, 0x20, 0xCC }));
    Equal(0, MaskedBytePattern.Parse("AA BB").FindAll(new byte[] { 0xAA }).Count);
}

static void AdvancedPauseFailClosed()
{
    var signature = new AdvancedPauseSignature("fixture", MaskedBytePattern.Parse("10 20 ?? 40"),
        1, [0x20, 0x30], [0xEB, 0x30]);
    var one = AdvancedPauseController.ResolveUnique([0x10, 0x20, 0x30, 0x40], 0x400000, [signature]);
    Equal(0x400001u, one!.Value.Address);
    var many = AdvancedPauseController.ResolveUnique(
        [0x10, 0x20, 0x30, 0x40, 0x10, 0x20, 0x30, 0x40], 0x400000, [signature]);
    Equal<(uint, AdvancedPauseSignature)?>(null, many);
    var none = AdvancedPauseController.ResolveUnique([0x00, 0x00], 0x400000, [signature]);
    Equal<(uint, AdvancedPauseSignature)?>(null, none);
}

static void DifficultyMathBoundaries()
{
    var add = DifficultySettings.Default with
    {
        DurabilityMultiplier = 2,
        GrowthUnit = GrowthUnit.Flag,
        GrowthFormula = GrowthFormula.Add,
        GrowthStep = 0.5,
        StartIndex = 2,
        MaximumMultiplier = 10
    };
    Equal(2d, DifficultyMath.EffectiveDurability(add, 0, 1));
    Equal(2.5d, DifficultyMath.EffectiveDurability(add, 10, 1));
    Equal(3d, DifficultyMath.EffectiveDurability(add, 20, 1));
    Equal(150, DifficultyMath.ScaleHealth(100, 1.5));
    Equal(-1, DifficultyMath.ScaleHealth(-1, 10));
}

static void DifficultyRejectsNonFinite()
{
    var normalized = (DifficultySettings.Default with
    {
        SpawnCountMultiplier = double.NaN,
        SpawnSpeedMultiplier = double.PositiveInfinity,
        DurabilityMultiplier = double.NegativeInfinity,
        GrowthFormula = GrowthFormula.Multiply,
        GrowthStep = 0
    }).Normalize();
    Equal(1d, normalized.SpawnCountMultiplier);
    Equal(1d, normalized.SpawnSpeedMultiplier);
    Equal(1d, normalized.DurabilityMultiplier);
    Equal(0.01d, normalized.GrowthStep);
}

static void LocalizationKeysMatch()
{
    var root = FindRepositoryRoot();
    var zh = LoadResourceKeys(Path.Combine(root, "src", "PlusOneTrainer", "Resources", "Strings.zh-CN.xaml"));
    var en = LoadResourceKeys(Path.Combine(root, "src", "PlusOneTrainer", "Resources", "Strings.en-US.xaml"));
    EqualSequence(zh.Order().ToArray(), en.Order().ToArray());
    if (zh.Count != zh.Distinct().Count())
        throw new InvalidOperationException("Duplicate localization key found.");
}

static void SaveVaultRoundTrip()
{
    var temp = NewTempDirectory();
    try
    {
        var saves = Directory.CreateDirectory(Path.Combine(temp, "存档")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(saves, "nested")).FullName;
        File.WriteAllText(Path.Combine(saves, "user1.dat"), "before");
        File.WriteAllBytes(Path.Combine(nested, "空.dat"), []);
        var vault = Path.Combine(temp, "vault");
        var service = new PlusOneTrainer.Services.SaveVaultService(vault, saves);
        var backup = service.CreateBackup("test");
        File.WriteAllText(Path.Combine(saves, "user1.dat"), "after");
        service.Restore(backup);
        Equal("before", File.ReadAllText(Path.Combine(saves, "user1.dat")));
        service.Verify(backup.Path);
    }
    finally { Directory.Delete(temp, true); }
}

static void SaveVaultBlocksTraversal()
{
    var temp = NewTempDirectory();
    try
    {
        var saves = Directory.CreateDirectory(Path.Combine(temp, "saves")).FullName;
        File.WriteAllText(Path.Combine(saves, "user1.dat"), "safe");
        var vault = Directory.CreateDirectory(Path.Combine(temp, "vault")).FullName;
        var backup = Directory.CreateDirectory(Path.Combine(vault, "evil")).FullName;
        File.WriteAllText(Path.Combine(backup, "plus-one-backup.json"),
            "{\"FormatVersion\":1,\"CreatedAtUtc\":\"2026-01-01T00:00:00Z\",\"Reason\":\"test\",\"SourcePath\":\"x\",\"Sha256\":{\"../escape.dat\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}}");
        var service = new PlusOneTrainer.Services.SaveVaultService(vault, saves);
        Throws<InvalidDataException>(() => service.Verify(backup));
    }
    finally { Directory.Delete(temp, true); }
}

static void ProfilePatchInvariants()
{
    var profile = new GameVersionProfile();
    if (profile.Sha256.Length != 64 || profile.Sha256.Any(x => !Uri.IsHexDigit(x)))
        throw new InvalidOperationException("Supported executable SHA-256 is malformed.");

    var patches = new[]
        {
            profile.MainLoopGuard, profile.AutoCollect, profile.UnlockSunLimit,
            profile.MushroomsAwake, profile.UnlockLimbo
        }
        .Concat(profile.NoCooldown)
        .Concat(profile.PlantInvincible)
        .ToArray();
    foreach (var patch in patches)
    {
        if (patch.Original.Length == 0 || patch.Original.Length != patch.Enabled.Length)
            throw new InvalidOperationException($"Invalid patch length at 0x{patch.Address:X8}.");
        if (patch.Original.SequenceEqual(patch.Enabled))
            throw new InvalidOperationException($"No-op patch at 0x{patch.Address:X8}.");
        var start = (ulong)patch.Address;
        var end = start + (uint)patch.Original.Length;
        if (start < GameVersionProfile.PreferredImageBase || end > uint.MaxValue + 1UL)
            throw new InvalidOperationException($"Patch address is out of x86 range at 0x{patch.Address:X8}.");
    }
    for (var i = 0; i < patches.Length; i++)
    for (var j = i + 1; j < patches.Length; j++)
    {
        var aEnd = (ulong)patches[i].Address + (uint)patches[i].Original.Length;
        var bEnd = (ulong)patches[j].Address + (uint)patches[j].Original.Length;
        if ((ulong)patches[i].Address < bEnd && (ulong)patches[j].Address < aEnd)
            throw new InvalidOperationException("Memory patch ranges overlap.");
    }
}

static void HealthBarDurabilityMath()
{
    Equal(0.5d, HealthBarMath.CombinedRatio((50, 100)));
    Equal(0.5d, HealthBarMath.CombinedRatio((100, 200), (25, 50), (-10, 0)));
    Equal(1d, HealthBarMath.CombinedRatio((500, 100)));
    Equal(0d, HealthBarMath.CombinedRatio((10, 0), (10, -1)));
}

static void MainWindowResourceReferences()
{
    var root = FindRepositoryRoot();
    var resources = new HashSet<string>(StringComparer.Ordinal);
    foreach (var relative in new[]
             {
                 "src/PlusOneTrainer/Resources/Strings.zh-CN.xaml",
                 "src/PlusOneTrainer/Resources/Styles.xaml",
                 "src/PlusOneTrainer/App.xaml"
             })
    {
        foreach (var key in LoadResourceKeys(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            resources.Add(key);
    }

    var main = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", "PlusOneTrainer", "MainWindow.xaml"));
    var references = main.Descendants().SelectMany(element => element.Attributes())
        .Select(attribute => attribute.Value)
        .SelectMany(value => ExtractResourceKeys(value))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    var missing = references.Where(key => !resources.Contains(key)).ToArray();
    if (missing.Length > 0)
        throw new InvalidOperationException("Missing XAML resources: " + string.Join(", ", missing));
}

static void SteamRuntimeTimestampGate()
{
    Equal(true, GameSession.IsSupportedRuntimeStamp(0x4D02B058));
    Equal(false, GameSession.IsSupportedRuntimeStamp(0x48ECEE74));
    Equal(false, GameSession.IsSupportedRuntimeStamp(0));
    Equal(false, GameSession.IsSupportedRuntimeStamp(0x49ECF563));
}

static void WalletMoneyMath()
{
    Equal(1_120, TrainerEngine.CalculateMoneyRaw(1_020, 1_000));
    Equal(99_999, TrainerEngine.CalculateMoneyRaw(99_950, 1_000));
    Throws<TrainerException>(() => TrainerEngine.CalculateMoneyRaw(-1, 1_000));
    Throws<ArgumentOutOfRangeException>(() => TrainerEngine.CalculateMoneyRaw(1_020, 1));
}

static IEnumerable<string> ExtractResourceKeys(string value)
{
    foreach (var prefix in new[] { "{StaticResource ", "{DynamicResource " })
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith('}'))
            continue;
        yield return value[prefix.Length..^1].Trim();
    }
}

static List<string> LoadResourceKeys(string path)
{
    var document = System.Xml.Linq.XDocument.Load(path);
    var x = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
    return document.Descendants().SelectMany(element => element.Attributes(x + "Key"))
        .Select(attribute => attribute.Value).ToList();
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PlusOneTrainer.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Repository root not found.");
}

static string NewTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "plus-one-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void EqualSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException("Sequences differ.");
}
