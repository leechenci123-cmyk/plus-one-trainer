namespace PlusOneTrainer.Core;

/// <summary>
/// Owns the verified Advanced Pause patch. Version 1.0 intentionally ships with no guessed
/// Steam signature: the capability remains unavailable until a runtime capture is reviewed
/// and added to <see cref="VerifiedSignatures"/>.
/// </summary>
public sealed class AdvancedPauseController : IDisposable
{
    private static readonly IReadOnlyList<AdvancedPauseSignature> VerifiedSignatures = [];

    private readonly ProcessMemory? _memory;
    private readonly uint _patchAddress;
    private readonly byte[] _original;
    private readonly byte[] _enabled;
    private bool _ownsPatch;

    public bool IsSupported => _memory is not null;
    public string UnavailableReason { get; }

    public bool IsPaused
    {
        get
        {
            if (!_ownsPatch || _memory?.IsAlive != true)
                return false;
            try { return _memory.ReadBytes(_patchAddress, _enabled.Length).SequenceEqual(_enabled); }
            catch { return false; }
        }
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
        if (!_ownsPatch || _memory?.IsAlive != true)
            return;
        try { SetPaused(false); }
        catch { /* never overwrite a patch whose ownership can no longer be proven */ }
    }
}
