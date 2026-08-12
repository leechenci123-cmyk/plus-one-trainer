using PlusOneTrainer.Models;
using PlusOneTrainer.Services;

namespace PlusOneTrainer.Core;

public sealed class TrainerEngine : IDisposable
{
    private readonly Dictionary<uint, OwnedZombieToken> _labZombies = [];
    private readonly Dictionary<uint, OwnedGridItemToken> _labGridItems = [];
    private int? _originalFrameDuration;
    private int? _lastWrittenFrameDuration;
    private byte? _originalFreePlanting;
    private byte? _lastWrittenFreePlanting;
    private uint _observedBoard;
    private uint _observedZombieBlock;
    private uint _observedGridBlock;
    private int _observedClock = -1;
    private int _observedMode = -1;
    private int _observedLevel = -1;
    private int _observedScene = -1;
    private bool _shutdownStarted;
    private bool _disposed;

    public GameSession Session { get; }
    public DifficultyService Difficulty { get; }
    public SaveVaultService SaveVault { get; }
    public bool SupportsAdvancedPause => Session.AdvancedPause.IsSupported;
    public bool SupportsChallengeRules => Difficulty.IsSupported;
    public string AdvancedPauseUnavailableReason => Session.AdvancedPause.UnavailableReason;
    public bool AdvancedPaused => Session.AdvancedPause.IsPaused;
    public double LastSpeed { get; private set; } = 1;

    public TrainerEngine(GameSession session, SaveVaultService saveVault)
    {
        Session = session;
        SaveVault = saveVault;
        Difficulty = new DifficultyService(session);
    }

    public void SetAdvancedPause(bool enabled)
    {
        if (enabled)
            RequireBattle();
        Session.AdvancedPause.SetPaused(enabled);
    }

    public void ToggleAdvancedPause() => SetAdvancedPause(!AdvancedPaused);

    public void SetSpeed(double multiplier)
    {
        multiplier = Math.Clamp(multiplier, 0.1, 10);
        var lawn = RequireLawn();
        _originalFrameDuration ??= Session.Memory.ReadInt32(lawn + Session.Profile.FrameDuration);
        var duration = Math.Clamp((int)Math.Round(10.0 / multiplier), 1, 100);
        Session.Memory.WriteInt32(lawn + Session.Profile.FrameDuration, duration);
        _lastWrittenFrameDuration = duration;
        LastSpeed = 10.0 / duration;
    }

    public void ResetSpeed()
    {
        if (!_originalFrameDuration.HasValue || !_lastWrittenFrameDuration.HasValue)
            return;
        var lawn = RequireLawn();
        var address = lawn + Session.Profile.FrameDuration;
        if (Session.Memory.ReadInt32(address) == _lastWrittenFrameDuration.Value)
            Session.Memory.WriteInt32(address, _originalFrameDuration.Value);
        _lastWrittenFrameDuration = null;
        LastSpeed = 1;
    }

    public void SetAutoCollect(bool enabled) => Session.SetPatch(Session.Profile.AutoCollect, enabled);
    public void SetUnlockSunLimit(bool enabled) => Session.SetPatch(Session.Profile.UnlockSunLimit, enabled);
    public void SetNoCooldown(bool enabled) => Session.SetPatchGroup(Session.Profile.NoCooldown, enabled);
    public void SetPlantInvincible(bool enabled) => Session.SetPatchGroup(Session.Profile.PlantInvincible, enabled);
    public void SetMushroomsAwake(bool enabled) => Session.SetPatch(Session.Profile.MushroomsAwake, enabled);
    public void SetLimboPage(bool enabled) => Session.SetPatch(Session.Profile.UnlockLimbo, enabled);

    public bool HasCompletedFirstAdventure()
    {
        var lawn = RequireLawn();
        var userData = Session.Memory.ReadUInt32(lawn + Session.Profile.UserData);
        return userData != 0 &&
               Session.Memory.ReadInt32(userData + Session.Profile.PlayerAdventurePlaythrough) >= 1;
    }

    public void SetNightRoofExperiment(bool enabled)
    {
        RequireBattle();
        if (Session.ReadGameMode() != 15)
            throw new InvalidOperationException("Start Roof Endless first, then apply the Night Roof experiment.");
        Session.Calls.SetBackground(enabled ? 5 : 4);
    }

    public void SetFreePlanting(bool enabled)
    {
        var lawn = RequireLawn();
        var address = lawn + Session.Profile.FreePlanting;
        _originalFreePlanting ??= Session.Memory.ReadByte(address);
        if (enabled)
        {
            Session.Memory.WriteByte(address, 1);
            _lastWrittenFreePlanting = 1;
        }
        else if (_lastWrittenFreePlanting.HasValue)
        {
            if (Session.Memory.ReadByte(address) == _lastWrittenFreePlanting.Value)
                Session.Memory.WriteByte(address, _originalFreePlanting.Value);
            _lastWrittenFreePlanting = null;
        }
    }

    public void SetSun(int sun)
    {
        RequireBattleOrSeedChooser();
        Session.Memory.WriteInt32(Session.RequireBoard() + Session.Profile.Sun, Math.Clamp(sun, 0, 999_999));
    }

    public IReadOnlyList<uint> SpawnZombie(int row, int column, int type)
    {
        RequireBattle();
        ValidateCell(row, column);
        if (type is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(type));
        if (type == 13)
            throw new TrainerException("ErrorBobsledUnavailable",
                "Bobsled Team placement is disabled until all four native team members can be tracked safely.");
        ValidateZombieTerrain(row, type);
        var liveCount = CaptureAliveZombies().Count;
        if (liveCount > 960)
            throw new InvalidOperationException("Zombie capacity safety reserve is too low for another test spawn.");
        if (type == 25)
            ValidateBossSpawn();

        var createdToken = Session.Calls.PutZombie(row, column, type);
        if (createdToken.Address == 0)
            throw new InvalidOperationException("The game did not create a zombie at the requested cell.");
        ObserveGameContext();
        _labZombies[createdToken.Address] = createdToken;
        return [createdToken.Address];
    }

    public uint PlaceLadder(int row, int column)
    {
        RequireBattle();
        ValidateCell(row, column);
        if (!HasLadderPlant(row, column))
            throw new TrainerException("InvalidLadderCell", "The cell has no valid defensive plant.");
        if (HasGridItem(row, column, 3))
            throw new InvalidOperationException("A ladder already exists in that cell.");
        if (HasAnyGridItem(row, column))
            throw new InvalidOperationException("That cell contains another grid object and is not safe for a ladder.");
        if (CaptureGridItems().Count >= 120)
            throw new InvalidOperationException("Grid-item safety reserve reached (120/128 live slots). Remove some grid items first.");
        if (Session.Scene is 2 or 3 && row is 2 or 3)
            throw new InvalidOperationException("Standalone ladders are not supported on pool water rows.");

        var createdToken = Session.Calls.PutLadder(row, column);
        if (createdToken.Address == 0)
            throw new InvalidOperationException("The game did not create a ladder at the requested cell.");
        ObserveGameContext();
        _labGridItems[createdToken.Address] = createdToken;
        return createdToken.Address;
    }

    public void ClearLabObjects()
    {
        if (!Session.Memory.IsAlive)
            return;
        RequireBattle();
        ObserveGameContext();
        Session.Calls.RequestOwnedZombieRemoval(_labZombies.Values);
        Session.Calls.DeleteOwnedGridItems(_labGridItems.Values);
        _labZombies.Clear();
        _labGridItems.Clear();
    }

    public void ObserveGameContext()
    {
        if (!Session.Memory.IsAlive || !Session.IsBattle)
        {
            ResetLabContext();
            return;
        }

        try
        {
            var board = Session.RequireBoard();
            var p = Session.Profile;
            var zombieBlock = Session.Memory.ReadUInt32(board + p.ZombieArray);
            var gridBlock = Session.Memory.ReadUInt32(board + p.GridItemArray);
            var clock = Session.Memory.ReadInt32(board + p.GameClock);
            var mode = Session.ReadGameMode();
            var level = Session.Memory.ReadInt32(board + p.AdventureLevel);
            var scene = Session.Memory.ReadInt32(board + p.Scene);
            var contextChanged = _observedBoard != 0 &&
                                 (_observedBoard != board || _observedZombieBlock != zombieBlock ||
                                  _observedGridBlock != gridBlock || clock < _observedClock ||
                                  mode != _observedMode || level != _observedLevel || scene != _observedScene);
            if (contextChanged ||
                _labZombies.Values.Any(x => x.Board != board || x.ArrayBlock != zombieBlock) ||
                _labGridItems.Values.Any(x => x.Board != board || x.ArrayBlock != gridBlock))
            {
                // A previous battle's addresses must never be touched after a context change.
                _labZombies.Clear();
                _labGridItems.Clear();
            }
            _observedBoard = board;
            _observedZombieBlock = zombieBlock;
            _observedGridBlock = gridBlock;
            _observedClock = clock;
            _observedMode = mode;
            _observedLevel = level;
            _observedScene = scene;
        }
        catch
        {
            ResetLabContext();
        }
    }

    private void ResetLabContext()
    {
        _labZombies.Clear();
        _labGridItems.Clear();
        _observedBoard = 0;
        _observedZombieBlock = 0;
        _observedGridBlock = 0;
        _observedClock = -1;
        _observedMode = -1;
        _observedLevel = -1;
        _observedScene = -1;
    }

    public void BeginShutdown()
    {
        if (_shutdownStarted)
            return;
        _shutdownStarted = true;
        Difficulty.Dispose();
        Session.Calls.BeginClose();
        // Automatic cross-process deletion during shutdown is intentionally avoided.
        // The explicit clear button performs guarded identity checks while a battle is live.
        ResetLabContext();
    }

    public bool HasLadderPlant(int row, int column)
    {
        var board = Session.RequireBoard();
        var p = Session.Profile;
        var array = Session.Memory.ReadUInt32(board + p.PlantArray);
        var count = ValidateDataArray(board, p.PlantArray, p.PlantCountMax, p.PlantCapacity, 1024, "plant");
        for (var i = 0; i < count; i++)
        {
            var address = array + (uint)i * p.PlantStructSize;
            if (Session.Memory.ReadBoolean(address + p.PlantDead) ||
                Session.Memory.ReadBoolean(address + p.PlantSquished))
                continue;
            if (Session.Memory.ReadInt32(address + p.PlantRow) != row ||
                Session.Memory.ReadInt32(address + p.PlantColumn) != column)
                continue;
            if (Session.Memory.ReadInt32(address + p.PlantType) is 3 or 23 or 30)
                return true;
        }
        return false;
    }

    private bool HasGridItem(int row, int column, int type)
    {
        var p = Session.Profile;
        return CaptureGridItems().Any(address =>
            Session.Memory.ReadInt32(address + p.GridItemType) == type &&
            Session.Memory.ReadInt32(address + p.GridItemRow) == row &&
            Session.Memory.ReadInt32(address + p.GridItemColumn) == column);
    }

    private bool HasAnyGridItem(int row, int column)
    {
        var p = Session.Profile;
        return CaptureGridItems().Any(address =>
            Session.Memory.ReadInt32(address + p.GridItemRow) == row &&
            Session.Memory.ReadInt32(address + p.GridItemColumn) == column);
    }

    private HashSet<uint> CaptureAliveZombies()
    {
        var p = Session.Profile;
        var board = Session.RequireBoard();
        var array = Session.Memory.ReadUInt32(board + p.ZombieArray);
        var count = ValidateDataArray(board, p.ZombieArray, p.ZombieCountMax, p.ZombieCapacity, 1024, "zombie");
        var result = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var address = array + (uint)i * p.ZombieStructSize;
            var id = Session.Memory.ReadUInt32(address + p.ZombieDataId);
            if ((id & 0xFFFF0000) != 0 && !Session.Memory.ReadBoolean(address + p.ZombieDead))
                result.Add(address);
        }
        return result;
    }

    private HashSet<uint> CaptureGridItems()
    {
        var p = Session.Profile;
        var board = Session.RequireBoard();
        var array = Session.Memory.ReadUInt32(board + p.GridItemArray);
        var count = ValidateDataArray(board, p.GridItemArray, p.GridItemCountMax, p.GridItemCapacity, 128, "grid item");
        var result = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var address = array + (uint)i * p.GridItemStructSize;
            var id = Session.Memory.ReadUInt32(address + p.GridItemDataId);
            if ((id & 0xFFFF0000) != 0 && !Session.Memory.ReadBoolean(address + p.GridItemDead))
                result.Add(address);
        }
        return result;
    }

    private void ValidateBossSpawn()
    {
        var board = Session.RequireBoard();
        var nativeBossBattle = Session.ReadGameMode() == 35 ||
                               Session.Memory.ReadInt32(board + Session.Profile.AdventureLevel) == 50;
        if (!nativeBossBattle || Session.Scene is 2 or 3)
            throw new InvalidOperationException("Dr. Zomboss is enabled only in the original final-boss battle. Other scenes may lack required animations and scripts.");
        var p = Session.Profile;
        var alive = CaptureAliveZombies();
        if (alive.Count > 900)
            throw new InvalidOperationException("Zombie capacity safety reserve is too low for Dr. Zomboss.");
        foreach (var address in alive)
        {
            if (Session.Memory.ReadInt32(address + p.ZombieType) == 25)
                throw new InvalidOperationException("Only one Dr. Zomboss can exist at a time.");
        }
    }

    private void ValidateZombieTerrain(int row, int type)
    {
        var scene = Session.Scene;
        var poolScene = scene is 2 or 3;
        var waterRow = poolScene && row is 2 or 3;
        var waterCapable = type is 0 or 1 or 2 or 4 or 10 or 11 or 14 or 16 or 26 or 27 or 28 or 29 or 31;
        if (waterRow && !waterCapable)
            throw new InvalidOperationException("That zombie type has no safe native state for a pool water row.");
        if (!waterRow && type is 10 or 11 or 14)
            throw new InvalidOperationException("Ducky Tube, Snorkel, and Dolphin Rider zombies are limited to pool water rows.");
        if (scene is 4 or 5 && type is 12 or 13)
            throw new InvalidOperationException("Zomboni and Bobsled Team are blocked on roof high ground.");
        if (type == 8 && (row == 0 || row == Session.RowCount - 1))
            throw new InvalidOperationException("Dancing Zombie needs valid rows above and below for its backup dancers.");
    }

    private void ValidateCell(int row, int column)
    {
        if (row < 0 || row >= Session.RowCount)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (column is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(column));
    }

    private int ValidateDataArray(uint board, uint arrayOffset, uint maxUsedOffset,
        uint capacityOffset, int expectedCapacity, string name)
    {
        var array = Session.Memory.ReadUInt32(board + arrayOffset);
        var maxUsed = Session.Memory.ReadInt32(board + maxUsedOffset);
        var capacity = Session.Memory.ReadInt32(board + capacityOffset);
        if (array == 0 || capacity <= 0 || capacity > expectedCapacity || maxUsed < 0 || maxUsed > capacity)
            throw new TrainerException("ErrorRuntimeSignature",
                $"The {name} DataArray failed its runtime bounds check (maxUsed={maxUsed}, capacity={capacity}).");
        return maxUsed;
    }

    private uint RequireLawn()
    {
        var lawn = Session.Memory.ResolveLawn(Session.Profile);
        if (lawn == 0)
            throw new TrainerException("GameNeeded", "The LawnApp object is not available.");
        return lawn;
    }

    private void RequireBattle()
    {
        if (!Session.IsBattle)
            throw new TrainerException("BattleNeeded", "A battle must be active.");
    }

    private void RequireBattleOrSeedChooser()
    {
        if (Session.ReadGameUi() is not (2 or 3))
            throw new TrainerException("BattleNeeded", "A battle or seed chooser must be active.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        BeginShutdown();
        if (Session.Memory.IsAlive)
        {
            try
            {
                var lawn = Session.Memory.ResolveLawn(Session.Profile);
                if (lawn != 0)
                {
                    if (_originalFrameDuration.HasValue && _lastWrittenFrameDuration.HasValue &&
                        Session.Memory.ReadInt32(lawn + Session.Profile.FrameDuration) == _lastWrittenFrameDuration.Value)
                        Session.Memory.WriteInt32(lawn + Session.Profile.FrameDuration, _originalFrameDuration.Value);
                    if (_originalFreePlanting.HasValue && _lastWrittenFreePlanting.HasValue &&
                        Session.Memory.ReadByte(lawn + Session.Profile.FreePlanting) == _lastWrittenFreePlanting.Value)
                        Session.Memory.WriteByte(lawn + Session.Profile.FreePlanting, _originalFreePlanting.Value);
                }
            }
            catch { }
        }
        Session.Dispose();
    }
}
