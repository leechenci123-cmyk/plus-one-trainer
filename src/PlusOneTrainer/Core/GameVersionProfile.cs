namespace PlusOneTrainer.Core;

public sealed record MemoryPatch(uint Address, byte[] Enabled, byte[] Original)
{
    public static MemoryPatch Of(uint address, byte enabled, byte original) =>
        new(address, [enabled], [original]);
}

public sealed class GameVersionProfile
{
    public const uint PreferredImageBase = 0x00400000;
    public const string SupportedSha256 = "868F8E2BAB0D6A7EF8AFC4C5960C608ECCEF82BD086BD6E0C0E2670199A5CA45";

    public string DisplayName { get; init; } = "Steam GOTY 1.2.0.1096 (English)";
    public string FileVersion { get; init; } = "1.2.0.1096";
    public string Sha256 { get; init; } = SupportedSha256;

    public uint LawnPointer { get; init; } = 0x00731C50;
    public uint FrameDuration { get; init; } = 0x4B4;
    public uint Board { get; init; } = 0x868;
    public uint GameMode { get; init; } = 0x918;
    public uint GameUi { get; init; } = 0x91C;
    public uint FreePlanting { get; init; } = 0x934;
    public uint UserData { get; init; } = 0x94C;
    public uint PlayerAdventurePlaythrough { get; init; } = 0x58;

    public uint ZombieArray { get; init; } = 0xA8;
    public uint ZombieCountMax { get; init; } = 0xAC;
    public uint ZombieCapacity { get; init; } = 0xB0;
    public uint ZombieFreeListHead { get; init; } = 0xB4;
    public uint ZombieLiveCount { get; init; } = 0xB8;
    public uint ZombieStructSize { get; init; } = 0x168;
    public uint ZombieRow { get; init; } = 0x1C;
    public uint ZombieType { get; init; } = 0x24;
    public uint ZombieStatus { get; init; } = 0x28;
    public uint ZombiePositionX { get; init; } = 0x2C;
    public uint ZombiePositionY { get; init; } = 0x30;
    public uint ZombieFromWave { get; init; } = 0x6C;
    public uint ZombieBodyHealth { get; init; } = 0xC8;
    public uint ZombieBodyMaxHealth { get; init; } = 0xCC;
    public uint ZombieHelmHealth { get; init; } = 0xD0;
    public uint ZombieHelmMaxHealth { get; init; } = 0xD4;
    public uint ZombieShieldHealth { get; init; } = 0xDC;
    public uint ZombieShieldMaxHealth { get; init; } = 0xE0;
    public uint ZombieFlyingHealth { get; init; } = 0xE4;
    public uint ZombieFlyingMaxHealth { get; init; } = 0xE8;
    public uint ZombieDead { get; init; } = 0xEC;
    public uint ZombieDataId { get; init; } = 0x164;

    public uint PlantArray { get; init; } = 0xC4;
    public uint PlantCountMax { get; init; } = 0xC8;
    public uint PlantCapacity { get; init; } = 0xCC;
    public uint PlantStructSize { get; init; } = 0x14C;
    public uint PlantRow { get; init; } = 0x1C;
    public uint PlantType { get; init; } = 0x24;
    public uint PlantColumn { get; init; } = 0x28;
    public uint PlantDead { get; init; } = 0x141;
    public uint PlantSquished { get; init; } = 0x142;
    public uint PlantPositionX { get; init; } = 0x08;
    public uint PlantPositionY { get; init; } = 0x0C;
    public uint PlantHealth { get; init; } = 0x40;
    public uint PlantMaxHealth { get; init; } = 0x44;
    public uint PlantDataId { get; init; } = 0x148;

    public uint GridItemArray { get; init; } = 0x134;
    public uint GridItemCountMax { get; init; } = 0x138;
    public uint GridItemCapacity { get; init; } = 0x13C;
    public uint GridItemFreeListHead { get; init; } = 0x140;
    public uint GridItemLiveCount { get; init; } = 0x144;
    public uint GridItemStructSize { get; init; } = 0xEC;
    public uint GridItemType { get; init; } = 0x08;
    public uint GridItemColumn { get; init; } = 0x10;
    public uint GridItemRow { get; init; } = 0x14;
    public uint GridItemDead { get; init; } = 0x20;
    public uint GridItemDataId { get; init; } = 0xE8;

    public uint Challenge { get; init; } = 0x178;
    public uint GamePaused { get; init; } = 0x17C;
    public uint Scene { get; init; } = 0x5564;
    public uint AdventureLevel { get; init; } = 0x5568;
    public uint Sun { get; init; } = 0x5578;
    public uint GameClock { get; init; } = 0x5580;
    public uint IceTrailCooldown { get; init; } = 0x63C;
    public uint CurrentWave { get; init; } = 0x5594;
    public uint TotalSpawnedWaves { get; init; } = 0x5598;
    public uint ZombieCountDown { get; init; } = 0x55B4;
    public uint ZombieCountDownStart { get; init; } = 0x55B8;
    public uint HugeWaveCountDown { get; init; } = 0x55BC;
    public uint DebugMode { get; init; } = 0x5610;

    public MemoryPatch MainLoopGuard { get; init; } = MemoryPatch.Of(0x005DD25E, 0xFE, 0xC8);
    public MemoryPatch AutoCollect { get; init; } = MemoryPatch.Of(0x004352F2, 0xEB, 0x75);
    public MemoryPatch UnlockSunLimit { get; init; } = MemoryPatch.Of(0x0041F4E5, 0xEB, 0x7E);
    // A complete instant-ready group: reload, growth, and both packet cooldown paths.
    public IReadOnlyList<MemoryPatch> NoCooldown { get; init; } =
    [
        MemoryPatch.Of(0x004673EB, 0x80, 0x85),
        MemoryPatch.Of(0x00466204, 0x80, 0x85),
        MemoryPatch.Of(0x00465F53, 0x70, 0x75),
        MemoryPatch.Of(0x00467905, 0x70, 0x75),
        MemoryPatch.Of(0x004681F7, 0x80, 0x85)
    ];
    public MemoryPatch MushroomsAwake { get; init; } = MemoryPatch.Of(0x004641A2, 0xEB, 0x74);
    public MemoryPatch UnlockLimbo { get; init; } = new(0x00431CE0, [0x38, 0x58, 0x64], [0x88, 0x58, 0x64]);

    public IReadOnlyList<MemoryPatch> PlantInvincible { get; init; } =
    [
        new(0x005447A1, [0x46, 0x40, 0x00], [0x46, 0x40, 0xFC]),
        MemoryPatch.Of(0x004207DF, 0xEB, 0x74),
        MemoryPatch.Of(0x0053BEDA, 0xEB, 0x75),
        new(0x00474FFB, [0x90, 0x90, 0x90], [0x29, 0x50, 0x40]),
        new(0x004757B9, [0x90, 0x90, 0x90], [0x29, 0x4E, 0x40]),
        MemoryPatch.Of(0x005433AB, 0xEB, 0x74),
        MemoryPatch.Of(0x0046511A, 0x70, 0x75),
        MemoryPatch.Of(0x00464F76, 0x00, 0xCE),
        new(0x00468F60, [0xC2, 0x04, 0x00], [0x53, 0x55, 0x8B])
    ];

    public uint CallPutZombie { get; init; } = 0x0042DCE0;
    public uint CallPutZombieInRow { get; init; } = 0x00411290;
    public uint CallPutLadder { get; init; } = 0x0040C420;
    public uint CallDeleteGridItem { get; init; } = 0x00451BD0;
    public uint CallPickBackground { get; init; } = 0x0040D5A0;
}
