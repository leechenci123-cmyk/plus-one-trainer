using PlusOneTrainer.Models;

namespace PlusOneTrainer.Core;

public sealed class DifficultyService : IDisposable
{
    private readonly GameSession _session;
    private readonly object _gate = new();
    private readonly Dictionary<uint, ZombieToken> _known = [];
    private readonly Dictionary<uint, AppliedZombie> _applied = [];
    private readonly CancellationTokenSource _stop = new();
    private DifficultySettings _settings = DifficultySettings.Default;
    private Task? _loop;
    private uint _lastBoard;
    private int _lastClock = -1;
    private int _lastCountdown = -1;
    private int _lastLevel = -1;
    private double _extraAccumulator;
    private int _settingsRevision;
    private int _observedRevision = -1;
    private bool _wasEnabled;
    private bool _disposed;

    public bool IsSupported { get; }

    public DifficultyService(GameSession session, bool enableVerifiedRuntime = false)
    {
        _session = session;
        IsSupported = enableVerifiedRuntime;
        if (IsSupported)
            _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    public DifficultySettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    public void Apply(DifficultySettings settings)
    {
        if (!IsSupported)
            throw new TrainerException("ErrorChallengeUnavailable",
                "Challenge-rule writes are disabled until the Steam 1096 object transaction passes live verification.");
        lock (_gate)
        {
            _settings = settings.Normalize();
            _settingsRevision++;
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    DifficultySettings settings;
                    int revision;
                    lock (_gate)
                    {
                        settings = _settings;
                        revision = _settingsRevision;
                    }
                    if (!_session.Memory.IsAlive)
                        continue;
                    if (!settings.Enabled || !_session.IsBattle)
                    {
                        ResetObservation(clearApplied: false);
                        _wasEnabled = false;
                        continue;
                    }
                    var enabling = !_wasEnabled;
                    _wasEnabled = true;
                    Tick(settings, revision, enabling);
                }
                catch { /* polling is best effort; UI actions still surface their own failures */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Tick(DifficultySettings settings, int revision, bool enabling)
    {
        var memory = _session.Memory;
        var profile = _session.Profile;
        var board = _session.RequireBoard();
        var boardChanged = board != _lastBoard;
        if (boardChanged)
        {
            _known.Clear();
            _applied.Clear();
            _lastBoard = board;
            _lastClock = -1;
            _lastCountdown = -1;
            _extraAccumulator = 0;
        }

        if (_observedRevision != revision)
        {
            _observedRevision = revision;
            _lastClock = -1;
            _lastCountdown = -1;
            _extraAccumulator = 0;
        }

        var level = memory.ReadInt32(board + profile.AdventureLevel);
        if (settings.ResetAtLevel && _lastLevel != -1 && level != _lastLevel)
            _extraAccumulator = 0;
        _lastLevel = level;

        AdjustSpawnCountdown(settings, board);

        var array = memory.ReadUInt32(board + profile.ZombieArray);
        var count = ValidateZombieArray(board);
        if (array == 0 || count == 0)
            return;

        var aliveNow = new Dictionary<uint, ZombieToken>();
        var newlySeen = new List<(uint Address, ZombieToken Token)>();
        for (var i = 0; i < count; i++)
        {
            var address = array + (uint)i * profile.ZombieStructSize;
            if (memory.ReadBoolean(address + profile.ZombieDead))
                continue;
            var type = memory.ReadInt32(address + profile.ZombieType);
            var row = memory.ReadInt32(address + profile.ZombieRow);
            var fromWave = memory.ReadInt32(address + profile.ZombieFromWave);
            if (type is < 0 or > 32 || row is < 0 or > 5)
                continue;
            var dataId = memory.ReadUInt32(address + profile.ZombieDataId);
            if ((dataId & 0xFFFF0000) == 0)
                continue;
            var token = new ZombieToken(type, row, fromWave, dataId);
            aliveNow[address] = token;
            if (!_known.TryGetValue(address, out var previous) || previous != token)
                newlySeen.Add((address, token));
        }

        foreach (var zombie in aliveNow)
            ApplyDurabilityRatio(zombie.Key, zombie.Value, DifficultyMath.EffectiveDurability(settings,
                memory.ReadInt32(board + profile.CurrentWave), memory.ReadInt32(board + profile.AdventureLevel)));

        _known.Clear();
        foreach (var pair in aliveNow)
            _known[pair.Key] = pair.Value;

        foreach (var zombie in newlySeen)
        {
            if (enabling || boardChanged)
                continue;
            var extra = ExtrasFor(settings.SpawnCountMultiplier, zombie.Token.Type);
            for (var i = 0; i < extra; i++)
            {
                var live = memory.ReadInt32(board + profile.ZombieLiveCount);
                var capacity = memory.ReadInt32(board + profile.ZombieCapacity);
                if (capacity is <= 0 or > 1024 || live < 0 || live >= Math.Min(capacity - 1, 960))
                    break;
                _session.Calls.PutZombie(zombie.Token.Row, 8, zombie.Token.Type);
            }
        }

        // Remote calls above may have filled recycled slots. Mark and scale them immediately,
        // so generated extras never recursively multiply on the next poll.
        if (newlySeen.Count > 0 && settings.SpawnCountMultiplier > 1)
            RefreshKnownAndScale(settings, board);
    }

    private void RefreshKnownAndScale(DifficultySettings settings, uint board)
    {
        var memory = _session.Memory;
        var profile = _session.Profile;
        var array = memory.ReadUInt32(board + profile.ZombieArray);
        var count = ValidateZombieArray(board);
        for (var i = 0; i < count; i++)
        {
            var address = array + (uint)i * profile.ZombieStructSize;
            if (memory.ReadBoolean(address + profile.ZombieDead))
                continue;
            var token = new ZombieToken(
                memory.ReadInt32(address + profile.ZombieType),
                memory.ReadInt32(address + profile.ZombieRow),
                memory.ReadInt32(address + profile.ZombieFromWave),
                memory.ReadUInt32(address + profile.ZombieDataId));
            if (token.Type is < 0 or > 32 || token.Row is < 0 or > 5 ||
                (token.DataId & 0xFFFF0000) == 0)
                continue;
            ApplyDurabilityRatio(address, token, DifficultyMath.EffectiveDurability(settings,
                memory.ReadInt32(board + profile.CurrentWave), memory.ReadInt32(board + profile.AdventureLevel)));
            _known[address] = token;
        }
    }

    private int ExtrasFor(double multiplier, int type)
    {
        if (type is 13 or 25 || multiplier <= 1)
            return 0;
        _extraAccumulator += multiplier - 1;
        var count = (int)Math.Floor(_extraAccumulator);
        _extraAccumulator -= count;
        return Math.Clamp(count, 0, 9);
    }

    private void ApplyDurabilityRatio(uint address, ZombieToken token, double multiplier)
    {
        var prior = _applied.TryGetValue(address, out var existing) && existing.Token == token
            ? existing.Multiplier
            : 1;
        if (Math.Abs(multiplier - prior) < 0.0001)
            return;
        var ratio = multiplier / prior;
        var p = _session.Profile;
        foreach (var offset in new[]
                 {
                     p.ZombieBodyHealth, p.ZombieBodyMaxHealth,
                     p.ZombieHelmHealth, p.ZombieHelmMaxHealth,
                     p.ZombieShieldHealth, p.ZombieShieldMaxHealth,
                     p.ZombieFlyingHealth, p.ZombieFlyingMaxHealth
                 })
        {
            var value = _session.Memory.ReadInt32(address + offset);
            if (value <= 0)
                continue;
            _session.Memory.WriteInt32(address + offset, DifficultyMath.ScaleHealth(value, ratio));
        }
        _applied[address] = new AppliedZombie(token, multiplier);
    }

    private void AdjustSpawnCountdown(DifficultySettings settings, uint board)
    {
        var p = _session.Profile;
        var memory = _session.Memory;
        var clock = memory.ReadInt32(board + p.GameClock);
        var countdown = memory.ReadInt32(board + p.ZombieCountDown);
        var countdownStart = Math.Max(1, memory.ReadInt32(board + p.ZombieCountDownStart));
        if (_lastClock >= 0 && clock >= _lastClock && clock - _lastClock <= 100 &&
            _lastCountdown >= 0 && Math.Abs(countdown - _lastCountdown) < 1000)
        {
            var ticks = clock - _lastClock;
            var adjustment = (int)Math.Round(ticks * (settings.SpawnSpeedMultiplier - 1));
            if (adjustment != 0 && countdown > 1)
                memory.WriteInt32(board + p.ZombieCountDown, Math.Clamp(countdown - adjustment, 1, countdownStart));
        }
        _lastClock = clock;
        _lastCountdown = countdown;
    }

    private int ValidateZombieArray(uint board)
    {
        var p = _session.Profile;
        var memory = _session.Memory;
        var array = memory.ReadUInt32(board + p.ZombieArray);
        var maxUsed = memory.ReadInt32(board + p.ZombieCountMax);
        var capacity = memory.ReadInt32(board + p.ZombieCapacity);
        if (array == 0 || capacity is <= 0 or > 1024 || maxUsed < 0 || maxUsed > capacity)
            throw new TrainerException("ErrorRuntimeSignature", "Zombie DataArray bounds failed runtime validation.");
        return maxUsed;
    }

    private void ResetObservation(bool clearApplied = false)
    {
        lock (_gate)
        {
            _known.Clear();
            _lastClock = -1;
            _lastCountdown = -1;
            if (clearApplied)
                _applied.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop.Cancel();
        try { _loop?.Wait(); } catch { }
        _stop.Dispose();
    }

    private readonly record struct ZombieToken(int Type, int Row, int FromWave, uint DataId);
    private readonly record struct AppliedZombie(ZombieToken Token, double Multiplier);
}
