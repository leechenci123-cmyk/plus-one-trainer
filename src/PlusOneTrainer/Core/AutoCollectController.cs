namespace PlusOneTrainer.Core;

/// <summary>Collect on the game's own update thread, including native wallet accounting.</summary>
public sealed class AutoCollectController(GameSession session) : IDisposable
{
    private IntPtr _code;
    private MemoryPatch? _patch;
    private bool _enabled;

    public void SetEnabled(bool enabled)
    {
        if (enabled == _enabled) return;
        session.Calls.RunGuarded(() =>
        {
            if (enabled && _patch is null)
            {
                var memory = session.Memory;
                byte[] original = [0x80, 0x7B, 0x50, 0x00, 0x75, 0x09];
                if (!memory.ReadBytes(0x4352EE, 6).SequenceEqual(original) ||
                    !memory.ReadBytes(0x435DC0, 8).SequenceEqual(new byte[] { 0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x6A, 0xFF }))
                    throw new TrainerException("ErrorRuntimeSignature", "Native coin collection signature mismatch; restart the game after closing other trainers.");
                _code = memory.AllocateCodeBuffer(512);
                var address = unchecked((uint)_code.ToInt32());
                var code = new X86CodeBuilder().PreserveRegisters()
                    .Bytes(0x80, 0x7B, 0x38, 0).JumpIfNotEqual("done")
                    .Bytes(0x80, 0x7B, 0x50, 0).JumpIfNotEqual("done")
                    .Bytes(0x8B, 0x43, 0x58) // eax = type
                    .Bytes(0x83, 0xF8, 1).JumpIfBelow("done")
                    .Bytes(0x83, 0xF8, 6).JumpIfBelow("collect").JumpIfEqual("collect")
                    .Bytes(0x83, 0xF8, 17).JumpIfEqual("collect")
                    .Bytes(0x83, 0xF8, 23).JumpIfEqual("collect")
                    .Jump("done") // Level-completion awards remain manual.
                    .Label("collect")
                    .Bytes(0x8B, 0xCB).Call(0x435DC0)
                    .Label("done").RestoreRegisters()
                    .Bytes(0x80, 0x7B, 0x50, 0).JumpIfNotEqual("collected")
                    .Jump(0x4352F4).Label("collected").Jump(0x4352FD).Build(address);
                memory.WriteBytes(address, code);
                memory.SealExecutable(_code, (uint)code.Length);
                _patch = new MemoryPatch(0x4352EE,
                    new X86CodeBuilder().Jump(address).Bytes(0x90).Build(0x4352EE), original);
            }
            if (_patch is not null) session.SetPatch(_patch, enabled);
            _enabled = enabled;
        });
    }

    public void Dispose()
    {
        if (!session.Memory.IsAlive) return;
        SetEnabled(false);
        // Reclaim only while the main thread is known to be outside the hook.
        if (_code != IntPtr.Zero)
            session.Calls.RunGuarded(() =>
            {
                session.Memory.FreeExecutable(_code);
                _code = IntPtr.Zero;
                _patch = null;
            });
    }
}
