namespace PlusOneTrainer.Core;

public enum HealthBarKind
{
    Plant,
    Zombie
}

public readonly record struct HealthBarSnapshot(
    HealthBarKind Kind,
    double X,
    double Y,
    double Width,
    double Ratio,
    int Current,
    int Maximum);

public static class HealthBarMath
{
    public static double CombinedRatio(params (int Current, int Maximum)[] parts)
    {
        long current = 0;
        long maximum = 0;
        foreach (var part in parts)
        {
            if (part.Maximum is <= 0 or > 10_000_000)
                continue;
            maximum += part.Maximum;
            current += Math.Clamp(part.Current, 0, part.Maximum);
        }

        return maximum == 0 ? 0 : Math.Clamp((double)current / maximum, 0, 1);
    }
}

public sealed class HealthBarSnapshotReader(GameSession session)
{
    private readonly GameSession _session = session;

    public IReadOnlyList<HealthBarSnapshot> Read(bool showZombies, bool showPlants)
    {
        if ((!showZombies && !showPlants) || !_session.Memory.IsAlive || !_session.IsBattle)
            return [];

        var board = _session.RequireBoard();
        var result = new List<HealthBarSnapshot>(128);
        if (showZombies)
            ReadZombies(board, result);
        if (showPlants)
            ReadPlants(board, result);

        // Never present a snapshot assembled across a Board transition.
        return _session.Memory.ResolveBoard(_session.Profile) == board ? result : [];
    }

    private void ReadZombies(uint board, List<HealthBarSnapshot> result)
    {
        var p = _session.Profile;
        var bytes = ReadArraySnapshot(board, p.ZombieArray, p.ZombieCountMax, p.ZombieCapacity,
            p.ZombieStructSize, 1024, "zombie", out var count);
        var stride = checked((int)p.ZombieStructSize);

        for (var i = 0; i < count; i++)
        {
            var slot = i * stride;
            if (!IsActive(bytes, slot, p.ZombieDataId) || ReadBoolean(bytes, slot, p.ZombieDead))
                continue;

            var type = ReadInt32(bytes, slot, p.ZombieType);
            var row = ReadInt32(bytes, slot, p.ZombieRow);
            var x = ReadSingle(bytes, slot, p.ZombiePositionX);
            var y = ReadSingle(bytes, slot, p.ZombiePositionY);
            if (type is < 0 or > 32 || row is < 0 or > 5 || !IsSaneCoordinate(x, y))
                continue;

            var body = Pair(bytes, slot, p.ZombieBodyHealth, p.ZombieBodyMaxHealth);
            var helmet = Pair(bytes, slot, p.ZombieHelmHealth, p.ZombieHelmMaxHealth);
            var shield = Pair(bytes, slot, p.ZombieShieldHealth, p.ZombieShieldMaxHealth);
            var flying = Pair(bytes, slot, p.ZombieFlyingHealth, p.ZombieFlyingMaxHealth);
            var totals = Totals(body, helmet, shield, flying);
            if (totals.Maximum <= 0)
                continue;

            var width = type switch
            {
                25 => 136d,
                23 or 32 => 88d,
                _ => 62d
            };
            var drawX = type == 25 ? Math.Max(600, x) : x + 8;
            var drawY = type == 25 ? Math.Max(55, y - 18) : y - 10;
            result.Add(new HealthBarSnapshot(HealthBarKind.Zombie, drawX, drawY, width,
                HealthBarMath.CombinedRatio(body, helmet, shield, flying), totals.Current, totals.Maximum));
        }
    }

    private void ReadPlants(uint board, List<HealthBarSnapshot> result)
    {
        var p = _session.Profile;
        var bytes = ReadArraySnapshot(board, p.PlantArray, p.PlantCountMax, p.PlantCapacity,
            p.PlantStructSize, 1024, "plant", out var count);
        var stride = checked((int)p.PlantStructSize);
        var cellLayers = new Dictionary<(int Row, int Column), int>();

        for (var i = 0; i < count; i++)
        {
            var slot = i * stride;
            if (!IsActive(bytes, slot, p.PlantDataId) || ReadBoolean(bytes, slot, p.PlantDead) ||
                ReadBoolean(bytes, slot, p.PlantSquished))
                continue;

            var type = ReadInt32(bytes, slot, p.PlantType);
            var row = ReadInt32(bytes, slot, p.PlantRow);
            var column = ReadInt32(bytes, slot, p.PlantColumn);
            var x = ReadInt32(bytes, slot, p.PlantPositionX);
            var y = ReadInt32(bytes, slot, p.PlantPositionY);
            var current = ReadInt32(bytes, slot, p.PlantHealth);
            var maximum = ReadInt32(bytes, slot, p.PlantMaxHealth);
            if (type is < 0 or > 48 || row is < 0 or > 5 || column is < 0 or > 8 ||
                !IsSaneCoordinate(x, y) || maximum is <= 0 or > 10_000_000)
                continue;

            var cell = (row, column);
            cellLayers.TryGetValue(cell, out var layer);
            cellLayers[cell] = layer + 1;
            result.Add(new HealthBarSnapshot(HealthBarKind.Plant, x + 10, y - 8 - layer * 7, 54,
                HealthBarMath.CombinedRatio((current, maximum)), Math.Clamp(current, 0, maximum), maximum));
        }
    }

    private byte[] ReadArraySnapshot(
        uint board,
        uint blockOffset,
        uint maxUsedOffset,
        uint capacityOffset,
        uint stride,
        int hardCapacity,
        string name,
        out int count)
    {
        var memory = _session.Memory;
        var block = memory.ReadUInt32(board + blockOffset);
        var maxUsed = memory.ReadInt32(board + maxUsedOffset);
        var capacity = memory.ReadInt32(board + capacityOffset);
        if (block == 0 || capacity is <= 0 || capacity > hardCapacity || maxUsed < 0 || maxUsed > capacity)
            throw new TrainerException("ErrorRuntimeSignature",
                $"The {name} health-bar snapshot failed its DataArray bounds check.");

        count = maxUsed;
        if (count == 0)
            return [];
        var length = checked(count * checked((int)stride));
        var snapshot = memory.ReadBytes(block, length);
        if (memory.ResolveBoard(_session.Profile) != board || memory.ReadUInt32(board + blockOffset) != block)
            throw new TrainerException("ErrorRuntimeSignature",
                $"The {name} DataArray changed while its health-bar snapshot was read.");
        return snapshot;
    }

    private static bool IsActive(byte[] bytes, int slot, uint idOffset) =>
        (ReadUInt32(bytes, slot, idOffset) & 0xFFFF0000) != 0;

    private static bool IsSaneCoordinate(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) && x is >= -400 and <= 1400 && y is >= -300 and <= 900;

    private static (int Current, int Maximum) Pair(byte[] bytes, int slot, uint current, uint maximum) =>
        (ReadInt32(bytes, slot, current), ReadInt32(bytes, slot, maximum));

    private static (int Current, int Maximum) Totals(params (int Current, int Maximum)[] parts)
    {
        long current = 0;
        long maximum = 0;
        foreach (var part in parts)
        {
            if (part.Maximum is <= 0 or > 10_000_000)
                continue;
            maximum += part.Maximum;
            current += Math.Clamp(part.Current, 0, part.Maximum);
        }
        return ((int)Math.Min(current, int.MaxValue), (int)Math.Min(maximum, int.MaxValue));
    }

    private static bool ReadBoolean(byte[] bytes, int slot, uint offset) => bytes[checked(slot + (int)offset)] != 0;
    private static int ReadInt32(byte[] bytes, int slot, uint offset) => BitConverter.ToInt32(bytes, checked(slot + (int)offset));
    private static uint ReadUInt32(byte[] bytes, int slot, uint offset) => BitConverter.ToUInt32(bytes, checked(slot + (int)offset));
    private static float ReadSingle(byte[] bytes, int slot, uint offset) => BitConverter.ToSingle(bytes, checked(slot + (int)offset));
}
