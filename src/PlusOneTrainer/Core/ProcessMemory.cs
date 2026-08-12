using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace PlusOneTrainer.Core;

public sealed class ProcessMemory : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public Process Process { get; }
    public uint ImageBase { get; }
    public uint ImageSize { get; }

    private ProcessMemory(Process process, IntPtr handle)
    {
        Process = process;
        _handle = handle;
        ImageBase = unchecked((uint)process.MainModule!.BaseAddress.ToInt32());
        ImageSize = checked((uint)process.MainModule.ModuleMemorySize);
    }

    public static ProcessMemory Open(Process process)
    {
        var access = NativeMethods.ProcessAccess.QueryInformation |
                     NativeMethods.ProcessAccess.VmRead |
                     NativeMethods.ProcessAccess.VmWrite |
                     NativeMethods.ProcessAccess.VmOperation |
                     NativeMethods.ProcessAccess.CreateThread |
                     NativeMethods.ProcessAccess.Synchronize;
        var handle = NativeMethods.OpenProcess(access, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return new ProcessMemory(process, handle);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public uint Rebase(uint preferredAddress) =>
        ImageBase + (preferredAddress - GameVersionProfile.PreferredImageBase);

    public bool IsInsideImage(uint address, uint length = 1)
    {
        var end = (ulong)address + length;
        return address >= ImageBase && end <= (ulong)ImageBase + ImageSize;
    }

    public void ValidateExecutableTarget(uint preferredAddress)
    {
        var address = Rebase(preferredAddress);
        if (!IsInsideImage(address, 8))
            throw new TrainerException("ErrorRuntimeSignature", $"Call target 0x{address:X8} is outside the runtime image.");
        var head = ReadBytes(address, 8);
        if (head.All(x => x is 0x00 or 0xCC or 0xFF) || head[0] is 0xC2 or 0xC3)
            throw new TrainerException("ErrorRuntimeSignature", $"Call target 0x{address:X8} failed its executable-code sanity check.");
    }

    public byte[] ReadBytes(uint address, int length)
    {
        ThrowIfClosed();
        var bytes = new byte[length];
        if (!NativeMethods.ReadProcessMemory(_handle, new IntPtr(address), bytes, length, out var read) || read.ToInt64() != length)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return bytes;
    }

    public byte ReadByte(uint address) => ReadBytes(address, 1)[0];
    public bool ReadBoolean(uint address) => ReadByte(address) != 0;
    public int ReadInt32(uint address) => BitConverter.ToInt32(ReadBytes(address, 4));
    public uint ReadUInt32(uint address) => BitConverter.ToUInt32(ReadBytes(address, 4));
    public float ReadSingle(uint address) => BitConverter.ToSingle(ReadBytes(address, 4));

    public uint ResolvePointer(uint baseAddress, params uint[] offsets)
    {
        var current = ReadUInt32(baseAddress);
        for (var i = 0; i < offsets.Length - 1; i++)
        {
            if (current == 0)
                return 0;
            current = ReadUInt32(current + offsets[i]);
        }
        return offsets.Length == 0 ? current : current + offsets[^1];
    }

    public uint ResolveLawn(GameVersionProfile profile) => ReadUInt32(Rebase(profile.LawnPointer));

    public uint ResolveBoard(GameVersionProfile profile)
    {
        var lawn = ResolveLawn(profile);
        return lawn == 0 ? 0 : ReadUInt32(lawn + profile.Board);
    }

    public void WriteBytes(uint address, byte[] bytes)
    {
        ThrowIfClosed();
        if (!NativeMethods.WriteProcessMemory(_handle, new IntPtr(address), bytes, bytes.Length, out var written) || written.ToInt64() != bytes.Length)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    public void WriteByte(uint address, byte value) => WriteBytes(address, [value]);
    public void WriteInt32(uint address, int value) => WriteBytes(address, BitConverter.GetBytes(value));

    public void WriteCodeBytes(uint address, byte[] bytes)
    {
        WriteBytes(address, bytes);
        if (!NativeMethods.FlushInstructionCache(_handle, new IntPtr(address), checked((uint)bytes.Length)))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    public PatchState GetPatchState(MemoryPatch patch)
    {
        ValidatePatch(patch);
        var address = Rebase(patch.Address);
        var current = ReadBytes(address, patch.Original.Length);
        if (current.SequenceEqual(patch.Original))
            return PatchState.Original;
        if (current.SequenceEqual(patch.Enabled))
            return PatchState.Enabled;
        return PatchState.Unexpected;
    }

    public bool TransitionPatch(MemoryPatch patch, bool enabled)
    {
        var state = GetPatchState(patch);
        var target = enabled ? PatchState.Enabled : PatchState.Original;
        if (state == target)
            return false;
        var expected = enabled ? PatchState.Original : PatchState.Enabled;
        if (state != expected)
        {
            var address = Rebase(patch.Address);
            throw new TrainerException("ErrorPatchMismatch", $"Patch precondition failed at 0x{address:X8}.");
        }

        var source = enabled ? patch.Original : patch.Enabled;
        var destination = enabled ? patch.Enabled : patch.Original;
        var rebased = Rebase(patch.Address);
        try
        {
            WriteCodeBytes(rebased, destination);
            if (GetPatchState(patch) != target)
                throw new TrainerException("ErrorPatchMismatch", $"Patch read-back failed at 0x{rebased:X8}.");
            return true;
        }
        catch (Exception transitionError)
        {
            Exception? rollbackError = null;
            try
            {
                var current = ReadBytes(rebased, destination.Length);
                if (current.SequenceEqual(destination))
                    WriteCodeBytes(rebased, source);
            }
            catch (Exception ex)
            {
                rollbackError = ex;
            }

            if (rollbackError is not null)
                throw new AggregateException(transitionError, rollbackError);
            throw;
        }
    }

    public TemporaryPatchLease AcquireTemporaryPatch(MemoryPatch patch)
    {
        var state = GetPatchState(patch);
        if (state == PatchState.Enabled)
            throw new TrainerException("ErrorPatchBusy", "The temporary patch is already active and is not owned by this trainer.");
        if (state != PatchState.Original)
            throw new TrainerException("ErrorPatchMismatch", "The temporary patch has unexpected runtime bytes.");
        TransitionPatch(patch, true);
        return new TemporaryPatchLease(this, patch);
    }

    public IntPtr AllocateCodeBuffer(uint size)
    {
        var address = NativeMethods.VirtualAllocEx(_handle, IntPtr.Zero, size,
            NativeMethods.AllocationType.Commit | NativeMethods.AllocationType.Reserve,
            NativeMethods.MemoryProtection.ReadWrite);
        if (address == IntPtr.Zero)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return address;
    }

    public void SealExecutable(IntPtr address, uint size)
    {
        if (!NativeMethods.VirtualProtectEx(_handle, address, size,
                NativeMethods.MemoryProtection.ExecuteRead, out _))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        if (!NativeMethods.FlushInstructionCache(_handle, address, size))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    public void FreeExecutable(IntPtr address)
    {
        if (address != IntPtr.Zero)
            NativeMethods.VirtualFreeEx(_handle, address, 0, NativeMethods.FreeType.Release);
    }

    public RemoteThreadLease StartRemote(IntPtr address)
    {
        var thread = NativeMethods.CreateRemoteThread(_handle, IntPtr.Zero, 0, address, IntPtr.Zero, 0, out _);
        if (thread == IntPtr.Zero)
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return new RemoteThreadLease(thread);
    }

    public bool IsAlive
    {
        get
        {
            try { return !Process.HasExited; }
            catch { return false; }
        }
    }

    private void ThrowIfClosed()
    {
        if (_disposed || !IsAlive)
            throw new TrainerException("ErrorGameClosed", "The game process is no longer available.");
    }

    private static void ValidatePatch(MemoryPatch patch)
    {
        if (patch.Original.Length == 0 || patch.Original.Length != patch.Enabled.Length)
            throw new ArgumentException("Patch byte arrays must be non-empty and have equal lengths.", nameof(patch));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        NativeMethods.CloseHandle(_handle);
        Process.Dispose();
    }
}

public sealed class RemoteThreadLease : IDisposable
{
    private IntPtr _handle;

    internal RemoteThreadLease(IntPtr handle) => _handle = handle;

    public bool Wait(uint timeoutMilliseconds)
    {
        if (_handle == IntPtr.Zero)
            return true;
        var result = NativeMethods.WaitForSingleObject(_handle, timeoutMilliseconds);
        if (result == NativeMethods.WaitObject0)
            return true;
        if (result == NativeMethods.WaitTimeout)
            return false;
        throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    public uint ExitCode
    {
        get
        {
            if (_handle == IntPtr.Zero || !NativeMethods.GetExitCodeThread(_handle, out var code))
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            return code;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;
        NativeMethods.CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }
}

public enum PatchState
{
    Original,
    Enabled,
    Unexpected
}

public sealed class TemporaryPatchLease : IDisposable
{
    private ProcessMemory? _memory;
    private readonly MemoryPatch _patch;

    internal TemporaryPatchLease(ProcessMemory memory, MemoryPatch patch)
    {
        _memory = memory;
        _patch = patch;
    }

    public void Restore()
    {
        var memory = _memory;
        if (memory is null)
            return;
        if (!memory.IsAlive)
        {
            _memory = null;
            return;
        }

        var state = memory.GetPatchState(_patch);
        if (state == PatchState.Enabled)
            memory.TransitionPatch(_patch, false);
        else if (state == PatchState.Unexpected)
            throw new TrainerException("ErrorPatchOwnership", "The temporary patch changed externally; restoration was refused.");
        _memory = null;
    }

    public void Dispose()
    {
        try { Restore(); }
        catch { /* callers that need the error invoke Restore explicitly */ }
    }
}
