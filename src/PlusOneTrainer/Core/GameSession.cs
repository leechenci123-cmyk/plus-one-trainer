using System.Diagnostics;

namespace PlusOneTrainer.Core;

public enum AttachmentState
{
    NotRunning,
    Unsupported,
    Attached
}

public sealed record AttachmentResult(AttachmentState State, GameSession? Session, string Details);

public sealed class GameSession : IDisposable
{
    private const uint RuntimeTimeDateStamp1096 = 0x4D02B058;
    private const uint SteamWrapperTimeDateStamp1096 = 0x48ECEE74;
    private readonly Dictionary<uint, MemoryPatch> _ownedPatches = [];
    private readonly object _patchGate = new();
    private bool _disposed;

    public ProcessMemory Memory { get; }
    public GameVersionProfile Profile { get; }
    public RemoteGameCalls Calls { get; }
    public AdvancedPauseController AdvancedPause { get; }
    public IntPtr GameWindow => Memory.Process.MainWindowHandle;
    public string ExecutablePath { get; }
    public uint RuntimeTimeDateStamp { get; }
    public bool IsSteamWrapperRuntime => RuntimeTimeDateStamp == SteamWrapperTimeDateStamp1096;
    public bool SupportsRemoteCalls => false;

    private GameSession(ProcessMemory memory, GameVersionProfile profile, string executablePath, uint runtimeTimeDateStamp)
    {
        Memory = memory;
        Profile = profile;
        ExecutablePath = executablePath;
        RuntimeTimeDateStamp = runtimeTimeDateStamp;
        Calls = new RemoteGameCalls(memory, profile);
        AdvancedPause = AdvancedPauseController.Detect(memory);
    }

    public static AttachmentResult TryAttach()
    {
        var candidates = Process.GetProcessesByName("PlantsVsZombies");
        if (candidates.Length == 0)
            return new AttachmentResult(AttachmentState.NotRunning, null, "PlantsVsZombies.exe was not found.");

        foreach (var process in candidates.OrderByDescending(x => x.StartTime))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var hash = ProcessMemory.ComputeSha256(path);
                if (!hash.Equals(GameVersionProfile.SupportedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    process.Dispose();
                    return new AttachmentResult(AttachmentState.Unsupported, null, $"SHA-256: {hash}");
                }

                var profile = new GameVersionProfile();
                var memory = ProcessMemory.Open(process);
                try
                {
                    var peOffset = memory.ReadUInt32(memory.ImageBase + 0x3C);
                    var machine = BitConverter.ToUInt16(memory.ReadBytes(memory.ImageBase + peOffset + 4, 2));
                    var runtimeStamp = memory.ReadUInt32(memory.ImageBase + peOffset + 8);
                    var optionalMagic = BitConverter.ToUInt16(memory.ReadBytes(memory.ImageBase + peOffset + 24, 2));
                    if (machine != 0x014C || optionalMagic != 0x010B)
                    {
                        memory.Dispose();
                        return new AttachmentResult(AttachmentState.Unsupported, null,
                            $"Runtime architecture: machine=0x{machine:X4}, optional=0x{optionalMagic:X4}");
                    }
                    if (!IsSupportedRuntimeStamp(runtimeStamp))
                    {
                        memory.Dispose();
                        return new AttachmentResult(AttachmentState.Unsupported, null,
                            $"Runtime PE timestamp: 0x{runtimeStamp:X8}");
                    }
                    if (runtimeStamp == SteamWrapperTimeDateStamp1096 &&
                        memory.ImageBase != GameVersionProfile.PreferredImageBase)
                    {
                        memory.Dispose();
                        return new AttachmentResult(AttachmentState.Unsupported, null,
                            $"Steam wrapper image base: 0x{memory.ImageBase:X8}");
                    }

                    var lawn = memory.ResolveLawn(profile);
                    if (lawn == 0)
                    {
                        memory.Dispose();
                        return new AttachmentResult(AttachmentState.Unsupported, null,
                            "The runtime image is present but its LawnApp pointer is unavailable.");
                    }

                    var gameUi = memory.ReadInt32(lawn + profile.GameUi);
                    var gameMode = memory.ReadInt32(lawn + profile.GameMode);
                    if (gameUi is < 0 or > 4 || gameMode is < 0 or > 80)
                    {
                        memory.Dispose();
                        return new AttachmentResult(AttachmentState.Unsupported, null,
                            $"Runtime object sanity check failed (UI={gameUi}, mode={gameMode}).");
                    }

                    var board = memory.ResolveBoard(profile);
                    if (board != 0)
                    {
                        var scene = memory.ReadInt32(board + profile.Scene);
                        if (scene is < 0 or > 5)
                        {
                            memory.Dispose();
                            return new AttachmentResult(AttachmentState.Unsupported, null,
                                $"Runtime Board sanity check failed (scene={scene}).");
                        }
                    }

                    return new AttachmentResult(AttachmentState.Attached,
                        new GameSession(memory, profile, path, runtimeStamp),
                        runtimeStamp == SteamWrapperTimeDateStamp1096
                            ? profile.DisplayName + " · Steam wrapper verified"
                            : profile.DisplayName);
                }
                catch
                {
                    memory.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                process.Dispose();
                return new AttachmentResult(AttachmentState.Unsupported, null, ex.Message);
            }
        }

        return new AttachmentResult(AttachmentState.NotRunning, null, "No readable game process was found.");
    }

    public static bool IsSupportedRuntimeStamp(uint stamp) =>
        stamp is RuntimeTimeDateStamp1096 or SteamWrapperTimeDateStamp1096;

    public bool IsBattle => ReadGameUi() == 3;

    public int ReadGameUi()
    {
        var lawn = Memory.ResolveLawn(Profile);
        return lawn == 0 ? 0 : Memory.ReadInt32(lawn + Profile.GameUi);
    }

    public int ReadGameMode()
    {
        var lawn = Memory.ResolveLawn(Profile);
        return lawn == 0 ? -1 : Memory.ReadInt32(lawn + Profile.GameMode);
    }

    public uint RequireBoard()
    {
        var board = Memory.ResolveBoard(Profile);
        if (board == 0)
            throw new TrainerException("BattleNeeded", "A live Board object is required.");
        return board;
    }

    public int RowCount
    {
        get
        {
            var board = RequireBoard();
            var scene = Memory.ReadInt32(board + Profile.Scene);
            return scene is 2 or 3 ? 6 : 5;
        }
    }

    public int Scene => Memory.ReadInt32(RequireBoard() + Profile.Scene);

    public void SetPatch(MemoryPatch patch, bool enabled)
    {
        SetPatchGroup([patch], enabled);
    }

    public void SetPatchGroup(IEnumerable<MemoryPatch> patches, bool enabled)
    {
        var group = patches.GroupBy(x => x.Address).Select(x => x.First()).ToArray();
        if (group.Length == 0)
            return;

        lock (_patchGate)
        {
            var states = group.ToDictionary(x => x.Address, Memory.GetPatchState);
            var ownership = new Dictionary<uint, MemoryPatch>(_ownedPatches);

            foreach (var patch in group)
            {
                var state = states[patch.Address];
                var owned = _ownedPatches.ContainsKey(patch.Address);
                if (state == PatchState.Unexpected)
                    throw new TrainerException("ErrorPatchMismatch", $"Unexpected bytes at 0x{Memory.Rebase(patch.Address):X8}.");
                if (enabled && !owned && state == PatchState.Enabled)
                    throw new TrainerException("ErrorPatchBusy", $"Patch 0x{Memory.Rebase(patch.Address):X8} is already enabled by another tool.");
            }

            try
            {
                foreach (var patch in group)
                {
                    var owned = _ownedPatches.ContainsKey(patch.Address);
                    if (enabled)
                    {
                        if (Memory.GetPatchState(patch) == PatchState.Original)
                            Memory.TransitionPatch(patch, true);
                        _ownedPatches[patch.Address] = patch;
                    }
                    else if (owned)
                    {
                        if (Memory.GetPatchState(patch) == PatchState.Enabled)
                            Memory.TransitionPatch(patch, false);
                        _ownedPatches.Remove(patch.Address);
                    }
                }
            }
            catch
            {
                foreach (var patch in group.Reverse())
                {
                    try
                    {
                        var originalState = states[patch.Address];
                        var currentState = Memory.GetPatchState(patch);
                        if (originalState == PatchState.Original && currentState == PatchState.Enabled)
                            Memory.TransitionPatch(patch, false);
                        else if (originalState == PatchState.Enabled && currentState == PatchState.Original)
                            Memory.TransitionPatch(patch, true);
                    }
                    catch { }
                }
                _ownedPatches.Clear();
                foreach (var pair in ownership)
                    _ownedPatches[pair.Key] = pair.Value;
                throw;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Calls.BeginClose();
        if (!Calls.TryDispose())
            return;
        if (Memory.IsAlive)
        {
            lock (_patchGate)
            {
                foreach (var patch in _ownedPatches.Values.Reverse().ToArray())
                {
                    try
                    {
                        if (Memory.GetPatchState(patch) == PatchState.Enabled)
                            Memory.TransitionPatch(patch, false);
                    }
                    catch { /* never overwrite bytes no longer owned by this trainer */ }
                }
                _ownedPatches.Clear();
            }
        }
        AdvancedPause.Dispose();
        Memory.Dispose();
        _disposed = true;
    }
}
