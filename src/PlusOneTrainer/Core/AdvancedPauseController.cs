namespace PlusOneTrainer.Core;

/// <summary>
/// Owns the verified two-site Steam 1096 Board-update patch. It fails closed if either
/// preimage or its surrounding control-flow bytes differ.
/// </summary>
public sealed class AdvancedPauseController : IDisposable
{
    private static readonly IReadOnlyList<AdvancedPauseSignature> VerifiedSignatures = [];

    private readonly ProcessMemory? _memory;
    private readonly uint _patchAddress;
    private readonly byte[] _original;
    private readonly byte[] _enabled;
    private bool _ownsPatch;
    private GameSession? _session;
    private static readonly MemoryPatch[] SteamPatches =
    [
        new(0x4192D4, [0xE9, 0x1D, 0, 0, 0, 0x90], [0x8B, 0x85, 0xA4, 0, 0, 0]),
        new(0x419309, [0xE9, 0x7D, 0x01, 0, 0, 0x90], [0x8B, 0x85, 0x90, 0x55, 0, 0])
    ];

    public bool IsSupported => _session is not null || _memory is not null;
    public string UnavailableReason { get; }

    public bool IsPaused
    {
        get
        {
            if (_session is not null)
                return _ownsPatch && _session.Memory.IsAlive &&
                    SteamPatches.All(p => _session.Memory.GetPatchState(p) == PatchState.Enabled);
            if (!_ownsPatch || _memory?.IsAlive != true)
                return false;
            try { return _memory.ReadBytes(_patchAddress, _enabled.Length).SequenceEqual(_enabled); }
            catch { return false; }
        }
    }

    public static AdvancedPauseController Detect(GameSession session)
    {
        var memory = session.Memory;
        var matched = SteamPatches.All(p => memory.GetPatchState(p) == PatchState.Original) &&
            memory.ReadBytes(0x4192F6, 8).SequenceEqual(new byte[] { 0x8B, 0x85, 0x6C, 0x57, 0, 0, 0x33, 0xFF }) &&
            memory.ReadBytes(0x4194B5, 7).SequenceEqual(new byte[] { 0x8B, 0x8C, 0x24, 0x14, 0x01, 0, 0 });
        return matched ? new AdvancedPauseController("") { _session = session } :
            new AdvancedPauseController("Steam Board update signature mismatch; no pause patch was applied.");
    }

    private AdvancedPauseController(string unavailableReason)
    {
        UnavailableReason = unavailableReason;
        _original = [];
        _enabled = [];
    }

    private AdvancedPauseController(ProcessMemory memory, uint patchAddress, byte[] original, byte[] enabled)
    {
        _memory = memory;
        _patchAddress = patchAddress;
        _original = original;
        _enabled = enabled;
        UnavailableReason = "";
    }

    public static AdvancedPauseController Detect(ProcessMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (VerifiedSignatures.Count == 0)
            return new AdvancedPauseController(
                "Steam 1096 Advanced Pause runtime signature has not been verified yet; no patch address will be guessed.");

        var module = memory.ReadBytes(memory.ImageBase, checked((int)memory.ImageSize));
        var resolved = ResolveUnique(module, memory.ImageBase, VerifiedSignatures);
        return resolved is null
            ? new AdvancedPauseController("No unique verified Advanced Pause runtime signature matched this process.")
            : new AdvancedPauseController(memory, resolved.Value.Address,
                resolved.Value.Signature.OriginalBytes, resolved.Value.Signature.EnabledBytes);
    }

    public static (uint Address, AdvancedPauseSignature Signature)? ResolveUnique(
        ReadOnlySpan<byte> module,
        uint imageBase,
        IEnumerable<AdvancedPauseSignature> signatures)
    {
        var candidates = new List<(uint Address, AdvancedPauseSignature Signature)>();
        foreach (var signature in signatures)
        {
            if (signature.OriginalBytes.Length == 0 ||
                signature.OriginalBytes.Length != signature.EnabledBytes.Length)
                throw new ArgumentException($"Signature {signature.Id} has invalid patch byte lengths.", nameof(signatures));
            foreach (var match in signature.SearchPattern.FindAll(module))
            {
                var patchOffset = match + signature.PatchOffset;
                if (patchOffset < 0 || patchOffset + signature.OriginalBytes.Length > module.Length)
                    continue;
                if (!module.Slice(patchOffset, signature.OriginalBytes.Length).SequenceEqual(signature.OriginalBytes))
                    continue;
                candidates.Add((checked(imageBase + (uint)patchOffset), signature));
            }
        }
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public void SetPaused(bool enabled)
    {
        if (_session is not null)
        {
            _session.Calls.RunGuarded(() => _session.SetPatchGroup(SteamPatches, enabled));
            _ownsPatch = enabled;
            return;
        }
        if (_memory is null)
            throw new TrainerException("ErrorAdvancedPauseUnavailable", UnavailableReason);
        if (!_memory.IsAlive)
            throw new TrainerException("ErrorGameClosed", "The game process is no longer available.");

        var current = _memory.ReadBytes(_patchAddress, _original.Length);
        if (enabled)
        {
            if (_ownsPatch && current.SequenceEqual(_enabled))
                return;
            if (!current.SequenceEqual(_original))
                throw new TrainerException("ErrorPatchBusy", "Advanced Pause bytes are not original; another tool may own them.");
            _memory.WriteCodeBytes(_patchAddress, _enabled);
            if (!_memory.ReadBytes(_patchAddress, _enabled.Length).SequenceEqual(_enabled))
                throw new TrainerException("ErrorPatchMismatch", "Advanced Pause patch read-back failed.");
            _ownsPatch = true;
            return;
        }

        if (!_ownsPatch)
            return;
        if (current.SequenceEqual(_enabled))
            _memory.WriteCodeBytes(_patchAddress, _original);
        else if (!current.SequenceEqual(_original))
            throw new TrainerException("ErrorPatchOwnership", "Advanced Pause bytes changed externally; restoration was refused.");
        _ownsPatch = false;
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            if (_ownsPatch && _session.Memory.IsAlive) SetPaused(false);
            return;
        }
        if (!_ownsPatch || _memory?.IsAlive != true)
            return;
        try { SetPaused(false); }
        catch { /* never overwrite a patch whose ownership can no longer be proven */ }
    }
}
