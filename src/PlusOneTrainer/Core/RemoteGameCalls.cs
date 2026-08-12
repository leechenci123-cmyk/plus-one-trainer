namespace PlusOneTrainer.Core;

public readonly record struct OwnedZombieToken(
    uint Address, uint Board, uint ArrayBlock, uint DataId, int Type, int Row, int FromWave);

public readonly record struct OwnedGridItemToken(
    uint Address, uint Board, uint ArrayBlock, uint DataId, int Row, int Column);

public sealed class RemoteGameCalls : IDisposable
{
    private readonly ProcessMemory _memory;
    private readonly GameVersionProfile _profile;
    private readonly object _gate = new();
    private PendingRemoteOperation? _pending;
    private bool _closing;

    public bool HasPendingOperation
    {
        get { lock (_gate) { ReapPending(); return _pending is not null; } }
    }

    public RemoteGameCalls(ProcessMemory memory, GameVersionProfile profile)
    {
        _memory = memory;
        _profile = profile;
    }

    public OwnedZombieToken PutZombie(int row, int column, int type)
    {
        if (type == 25)
            return PutBoss();

        uint board = 0;
        uint expectedAddress = 0;
        uint expectedBlock = 0;
        var expectedIndex = -1;
        var beforeSize = -1;
        var beforeMaxUsed = -1;
        var created = default(OwnedZombieToken);

        _ = ExecuteValidated([_profile.CallPutZombie], remote =>
        {
            board = RequireBoard();
            var challenge = RequireChallenge(board);
            var view = ReadZombieArray(board, requireFreeSlot: true);
            expectedBlock = view.Block;
            expectedIndex = view.FreeListHead;
            beforeSize = view.Size;
            beforeMaxUsed = view.MaxUsed;
            expectedAddress = AddressAt(view.Block, expectedIndex, _profile.ZombieStructSize);

            return new X86CodeBuilder()
                .Push((uint)column)
                .Push((uint)type)
                .MovEax((uint)row)
                .MovEcx(challenge)
                .Call(_memory.Rebase(_profile.CallPutZombie))
                .Ret()
                .Build(remote);
        }, () =>
        {
            if (_memory.ResolveBoard(_profile) != board)
                throw RuntimeFailure("Board changed while the zombie was being created.");
            var after = ReadZombieArray(board, requireFreeSlot: false);
            var expectedMaxUsed = expectedIndex == beforeMaxUsed ? beforeMaxUsed + 1 : beforeMaxUsed;
            if (after.Block != expectedBlock || after.Size != beforeSize + 1 || after.MaxUsed != expectedMaxUsed)
                throw RuntimeFailure("Zombie DataArray allocation did not match the expected single-slot transition.");
            ValidateZombie(expectedAddress, expectedBlock, expectedIndex, type, row);
            created = CaptureZombieToken(expectedAddress, board, expectedBlock);
            return expectedAddress;
        });
        return created;
    }

    public OwnedZombieToken PutBoss()
    {
        uint board = 0;
        var beforeSize = -1;
        var created = default(OwnedZombieToken);
        _ = ExecuteWithResult([_profile.CallPutZombieInRow], (remote, result) =>
        {
            board = RequireBoard();
            beforeSize = ReadZombieArray(board, requireFreeSlot: true).Size;
            return new X86CodeBuilder()
                .MovEax(board)
                .Push(0)
                .Push(25)
                .Call(_memory.Rebase(_profile.CallPutZombieInRow))
                .MovAbsoluteFromEax(result)
                .Ret()
                .Build(remote);
        }, address =>
        {
            if (_memory.ResolveBoard(_profile) != board)
                throw RuntimeFailure("Board changed while Dr. Zomboss was being created.");
            var view = ReadZombieArray(board, requireFreeSlot: false);
            if (view.Size != beforeSize + 1)
                throw RuntimeFailure("Dr. Zomboss allocation did not create exactly one zombie slot.");
            var index = ValidateObjectAddress(address, view, _profile.ZombieStructSize, "zombie");
            ValidateZombie(address, view.Block, index, 25, expectedRow: null);
            created = CaptureZombieToken(address, board, view.Block);
            return address;
        });
        return created;
    }

    public OwnedGridItemToken PutLadder(int row, int column)
    {
        uint board = 0;
        var beforeSize = -1;
        var created = default(OwnedGridItemToken);
        _ = ExecuteWithResult([_profile.CallPutLadder], (remote, result) =>
        {
            board = RequireBoard();
            beforeSize = ReadGridItemArray(board, requireFreeSlot: true).Size;
            return new X86CodeBuilder()
                .MovEdi((uint)row)
                .Push((uint)column)
                .MovEax(board)
                .Call(_memory.Rebase(_profile.CallPutLadder))
                .MovAbsoluteFromEax(result)
                .Ret()
                .Build(remote);
        }, address =>
        {
            if (_memory.ResolveBoard(_profile) != board)
                throw RuntimeFailure("Board changed while the ladder was being created.");
            var view = ReadGridItemArray(board, requireFreeSlot: false);
            if (view.Size != beforeSize + 1)
                throw RuntimeFailure("Ladder allocation did not create exactly one grid-item slot.");
            var index = ValidateObjectAddress(address, view, _profile.GridItemStructSize, "grid item");
            ValidateGridItem(address, view.Block, index, row, column);
            created = new OwnedGridItemToken(address, board, view.Block,
                _memory.ReadUInt32(address + _profile.GridItemDataId), row, column);
            return address;
        });
        return created;
    }

    public void RequestOwnedZombieRemoval(IEnumerable<OwnedZombieToken> tokens)
    {
        var items = tokens.Distinct().Where(x => x.Type != 25).ToArray();
        if (items.Length == 0)
            return;

        RunGuarded(() =>
        {
            var currentBoard = _memory.ResolveBoard(_profile);
            if (currentBoard == 0)
                return;
            var view = ReadZombieArray(currentBoard, requireFreeSlot: false);
            foreach (var token in items)
            {
                if (!MatchesOwnedZombie(token, currentBoard, view))
                    continue;
                _memory.WriteInt32(token.Address + _profile.ZombieStatus, 3);
            }
        });
    }

    public void DeleteOwnedGridItems(IEnumerable<OwnedGridItemToken> tokens)
    {
        var items = tokens.Distinct().ToArray();
        if (items.Length == 0)
            return;

        foreach (var batch in items.Chunk(80))
        {
            Execute([_profile.CallDeleteGridItem], remote =>
            {
                var code = new X86CodeBuilder();
                var currentBoard = _memory.ResolveBoard(_profile);
                if (currentBoard == 0)
                    return code.Ret().Build(remote);
                var view = ReadGridItemArray(currentBoard, requireFreeSlot: false);
                foreach (var token in batch)
                {
                    if (MatchesOwnedGridItem(token, currentBoard, view))
                        code.MovEsi(token.Address).Call(_memory.Rebase(_profile.CallDeleteGridItem));
                }
                return code.Ret().Build(remote);
            });
        }
    }

    public void SetBackground(int scene)
    {
        Execute([_profile.CallPickBackground], remote =>
        {
            var board = RequireBoard();
            return new X86CodeBuilder()
                .MovEsi(board)
                .MovDwordAtEsi(_profile.Scene, (uint)scene)
                .Call(_memory.Rebase(_profile.CallPickBackground))
                .Ret()
                .Build(remote);
        });
    }

    private void Execute(IEnumerable<uint> callTargets, Func<uint, byte[]> build) =>
        ExecuteCore(callTargets, (remote, _) => build(remote), captureResult: false, null);

    private uint ExecuteValidated(
        IEnumerable<uint> callTargets,
        Func<uint, byte[]> build,
        Func<uint> validate) =>
        ExecuteCore(callTargets, (remote, _) => build(remote), captureResult: false, _ => validate());

    private uint ExecuteWithResult(
        IEnumerable<uint> callTargets,
        Func<uint, uint, byte[]> build,
        Func<uint, uint> validate) =>
        ExecuteCore(callTargets, build, captureResult: true, validate);

    private uint ExecuteCore(
        IEnumerable<uint> callTargets,
        Func<uint, uint, byte[]> build,
        bool captureResult,
        Func<uint, uint>? validateResult)
    {
        lock (_gate)
        {
            ReapPending();
            ThrowIfClosing();
            if (_pending is not null)
                throw new TrainerException("ErrorRemoteBusy", "A previous remote game call is still running; new calls are blocked.");

            IntPtr remote = IntPtr.Zero;
            IntPtr resultBuffer = IntPtr.Zero;
            TemporaryPatchLease? guard = null;
            RemoteThreadLease? thread = null;
            Exception? operationError = null;
            uint result = 0;
            try
            {
                foreach (var target in callTargets.Distinct())
                    _memory.ValidateExecutableTarget(target);
                guard = _memory.AcquireTemporaryPatch(_profile.MainLoopGuard);
                WaitForWorldToStop();

                remote = _memory.AllocateCodeBuffer(1024);
                if (captureResult)
                    resultBuffer = _memory.AllocateCodeBuffer(4);
                var address = unchecked((uint)remote.ToInt32());
                var resultAddress = resultBuffer == IntPtr.Zero ? 0u : unchecked((uint)resultBuffer.ToInt32());
                var bytes = build(address, resultAddress);
                if (bytes.Length > 1024)
                    throw new InvalidOperationException("Remote operation exceeded its fixed code buffer.");
                _memory.WriteBytes(address, bytes);
                _memory.SealExecutable(remote, (uint)bytes.Length);
                thread = _memory.StartRemote(remote);
                if (!thread.Wait(3000))
                {
                    _pending = new PendingRemoteOperation(_memory, remote, resultBuffer, thread, guard);
                    remote = IntPtr.Zero;
                    resultBuffer = IntPtr.Zero;
                    thread = null;
                    guard = null;
                    throw new TrainerException("ErrorRemoteTimeout",
                        "The game call timed out. Its code page and main-loop guard remain owned until the thread exits.");
                }
                _ = thread.ExitCode;
                if (captureResult)
                    result = _memory.ReadUInt32(resultAddress);
                if (validateResult is not null)
                    result = validateResult(result);
            }
            catch (Exception ex)
            {
                operationError = ex;
            }
            finally
            {
                thread?.Dispose();
                _memory.FreeExecutable(remote);
                _memory.FreeExecutable(resultBuffer);
                if (guard is not null && _memory.IsAlive)
                {
                    try { guard.Restore(); }
                    catch (Exception cleanupError)
                    {
                        operationError = operationError is null
                            ? cleanupError
                            : new AggregateException(operationError, cleanupError);
                    }
                }
            }
            if (operationError is not null)
                throw operationError;
            return result;
        }
    }

    private void RunGuarded(Action action)
    {
        lock (_gate)
        {
            ReapPending();
            ThrowIfClosing();
            if (_pending is not null)
                throw new TrainerException("ErrorRemoteBusy", "A previous remote game call is still running; new calls are blocked.");

            TemporaryPatchLease? guard = null;
            Exception? operationError = null;
            try
            {
                guard = _memory.AcquireTemporaryPatch(_profile.MainLoopGuard);
                WaitForWorldToStop();
                action();
            }
            catch (Exception ex)
            {
                operationError = ex;
            }
            finally
            {
                if (guard is not null && _memory.IsAlive)
                {
                    try { guard.Restore(); }
                    catch (Exception cleanupError)
                    {
                        operationError = operationError is null
                            ? cleanupError
                            : new AggregateException(operationError, cleanupError);
                    }
                }
            }
            if (operationError is not null)
                throw operationError;
        }
    }

    private void WaitForWorldToStop()
    {
        var lawn = _memory.ResolveLawn(_profile);
        var frame = lawn == 0 ? 10 : Math.Clamp(_memory.ReadInt32(lawn + _profile.FrameDuration), 1, 100);
        Thread.Sleep(frame * 2);
    }

    private DataArrayView ReadZombieArray(uint board, bool requireFreeSlot) =>
        ReadDataArray(board, _profile.ZombieArray, _profile.ZombieCountMax, _profile.ZombieCapacity,
            _profile.ZombieFreeListHead, _profile.ZombieLiveCount, 1024, requireFreeSlot, "zombie");

    private DataArrayView ReadGridItemArray(uint board, bool requireFreeSlot) =>
        ReadDataArray(board, _profile.GridItemArray, _profile.GridItemCountMax, _profile.GridItemCapacity,
            _profile.GridItemFreeListHead, _profile.GridItemLiveCount, 128, requireFreeSlot, "grid item");

    private DataArrayView ReadDataArray(
        uint board, uint blockOffset, uint maxUsedOffset, uint capacityOffset,
        uint freeListOffset, uint sizeOffset, int hardCapacity, bool requireFreeSlot, string name)
    {
        var block = _memory.ReadUInt32(board + blockOffset);
        var maxUsed = _memory.ReadInt32(board + maxUsedOffset);
        var capacity = _memory.ReadInt32(board + capacityOffset);
        var freeListHead = _memory.ReadInt32(board + freeListOffset);
        var size = _memory.ReadInt32(board + sizeOffset);
        if (block == 0 || capacity is <= 0 || capacity > hardCapacity ||
            maxUsed < 0 || maxUsed > capacity || size < 0 || size > capacity ||
            freeListHead < 0 || freeListHead > maxUsed)
            throw RuntimeFailure($"The {name} DataArray failed its runtime bounds check.");
        if (requireFreeSlot && (size >= capacity || freeListHead >= capacity))
            throw RuntimeFailure($"The {name} DataArray has no safe free slot.");
        return new DataArrayView(board, block, maxUsed, capacity, freeListHead, size);
    }

    private void ValidateZombie(uint address, uint block, int expectedIndex, int expectedType, int? expectedRow)
    {
        var id = _memory.ReadUInt32(address + _profile.ZombieDataId);
        if ((id >> 16) == 0 || (id & 0xFFFF) != expectedIndex ||
            _memory.ReadBoolean(address + _profile.ZombieDead) ||
            _memory.ReadInt32(address + _profile.ZombieType) != expectedType ||
            (expectedRow.HasValue && _memory.ReadInt32(address + _profile.ZombieRow) != expectedRow.Value) ||
            address != AddressAt(block, expectedIndex, _profile.ZombieStructSize))
            throw RuntimeFailure("The created zombie failed its post-call identity check.");
    }

    private void ValidateGridItem(uint address, uint block, int expectedIndex, int row, int column)
    {
        var id = _memory.ReadUInt32(address + _profile.GridItemDataId);
        if ((id >> 16) == 0 || (id & 0xFFFF) != expectedIndex ||
            _memory.ReadBoolean(address + _profile.GridItemDead) ||
            _memory.ReadInt32(address + _profile.GridItemType) != 3 ||
            _memory.ReadInt32(address + _profile.GridItemRow) != row ||
            _memory.ReadInt32(address + _profile.GridItemColumn) != column ||
            address != AddressAt(block, expectedIndex, _profile.GridItemStructSize))
            throw RuntimeFailure("The created ladder failed its post-call identity check.");
    }

    private OwnedZombieToken CaptureZombieToken(uint address, uint board, uint block) =>
        new(address, board, block,
            _memory.ReadUInt32(address + _profile.ZombieDataId),
            _memory.ReadInt32(address + _profile.ZombieType),
            _memory.ReadInt32(address + _profile.ZombieRow),
            _memory.ReadInt32(address + _profile.ZombieFromWave));

    private int ValidateObjectAddress(uint address, DataArrayView view, uint stride, string name)
    {
        if (address < view.Block)
            throw RuntimeFailure($"The returned {name} pointer is outside its DataArray.");
        var delta = address - view.Block;
        if (delta % stride != 0)
            throw RuntimeFailure($"The returned {name} pointer is not slot-aligned.");
        var index = checked((int)(delta / stride));
        if (index < 0 || index >= view.MaxUsed || index >= view.Capacity)
            throw RuntimeFailure($"The returned {name} pointer is outside active DataArray bounds.");
        return index;
    }

    private bool MatchesOwnedZombie(OwnedZombieToken token, uint board, DataArrayView view)
    {
        try
        {
            var index = checked((int)(token.DataId & 0xFFFF));
            return token.Board == board && token.ArrayBlock == view.Block && index < view.MaxUsed &&
                   token.Address == AddressAt(view.Block, index, _profile.ZombieStructSize) &&
                   _memory.ReadUInt32(token.Address + _profile.ZombieDataId) == token.DataId &&
                   !_memory.ReadBoolean(token.Address + _profile.ZombieDead) &&
                   _memory.ReadInt32(token.Address + _profile.ZombieType) == token.Type &&
                   _memory.ReadInt32(token.Address + _profile.ZombieRow) == token.Row &&
                   _memory.ReadInt32(token.Address + _profile.ZombieFromWave) == token.FromWave;
        }
        catch { return false; }
    }

    private bool MatchesOwnedGridItem(OwnedGridItemToken token, uint board, DataArrayView view)
    {
        try
        {
            var index = checked((int)(token.DataId & 0xFFFF));
            return token.Board == board && token.ArrayBlock == view.Block && index < view.MaxUsed &&
                   token.Address == AddressAt(view.Block, index, _profile.GridItemStructSize) &&
                   _memory.ReadUInt32(token.Address + _profile.GridItemDataId) == token.DataId &&
                   !_memory.ReadBoolean(token.Address + _profile.GridItemDead) &&
                   _memory.ReadInt32(token.Address + _profile.GridItemType) == 3 &&
                   _memory.ReadInt32(token.Address + _profile.GridItemRow) == token.Row &&
                   _memory.ReadInt32(token.Address + _profile.GridItemColumn) == token.Column;
        }
        catch { return false; }
    }

    private static uint AddressAt(uint block, int index, uint stride)
    {
        var address = (ulong)block + (ulong)checked((uint)index) * stride;
        if (address > uint.MaxValue)
            throw RuntimeFailure("DataArray address calculation overflowed the 32-bit process range.");
        return (uint)address;
    }

    private uint RequireBoard()
    {
        var board = _memory.ResolveBoard(_profile);
        if (board == 0)
            throw new TrainerException("BattleNeeded", "A live Board object is required for this call.");
        return board;
    }

    private uint RequireChallenge(uint board)
    {
        var challenge = _memory.ReadUInt32(board + _profile.Challenge);
        if (challenge == 0)
            throw new TrainerException("BattleNeeded", "A live Challenge object is required for this call.");
        return challenge;
    }

    private static TrainerException RuntimeFailure(string message) =>
        new("ErrorRuntimeSignature", message);

    private void ThrowIfClosing()
    {
        if (_closing)
            throw new ObjectDisposedException(nameof(RemoteGameCalls), "The trainer is closing; new game calls are blocked.");
    }

    private void ReapPending()
    {
        if (_pending is null)
            return;
        if (!_pending.Memory.IsAlive)
        {
            _pending.AbandonAfterProcessExit();
            _pending = null;
            return;
        }
        if (!_pending.Thread.Wait(0))
            return;
        _pending.Complete();
        _pending = null;
    }

    public void BeginClose()
    {
        lock (_gate)
        {
            ReapPending();
            _closing = true;
        }
    }

    public void Dispose() => _ = TryDispose();

    public bool TryDispose()
    {
        BeginClose();
        PendingRemoteOperation? pending;
        lock (_gate)
        {
            ReapPending();
            pending = _pending;
        }
        if (pending is null)
            return true;

        if (!pending.Thread.Wait(1000))
            return false;
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending))
            {
                pending.Complete();
                _pending = null;
            }
        }
        return true;
    }

    public bool WaitForPending(uint milliseconds)
    {
        PendingRemoteOperation? pending;
        lock (_gate)
        {
            ReapPending();
            pending = _pending;
        }
        if (pending is null)
            return true;
        if (!pending.Thread.Wait(milliseconds))
            return false;
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending))
            {
                pending.Complete();
                _pending = null;
            }
        }
        return true;
    }

    private readonly record struct DataArrayView(
        uint Board, uint Block, int MaxUsed, int Capacity, int FreeListHead, int Size);

    private sealed class PendingRemoteOperation(
        ProcessMemory memory,
        IntPtr code,
        IntPtr resultBuffer,
        RemoteThreadLease thread,
        TemporaryPatchLease guard)
    {
        public ProcessMemory Memory { get; } = memory;
        public RemoteThreadLease Thread { get; } = thread;

        public void Complete()
        {
            guard.Restore();
            Thread.Dispose();
            Memory.FreeExecutable(code);
            Memory.FreeExecutable(resultBuffer);
        }

        public void AbandonAfterProcessExit()
        {
            Thread.Dispose();
            guard.Dispose();
        }
    }
}
