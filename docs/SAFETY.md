# Safety model / 安全模型

Plus One Trainer is intentionally narrow. It targets an offline, single-player game process and does not alter the game installation.

## Write gate

All memory writes require both:

1. the exact allow-listed executable SHA-256:
   `868F8E2BAB0D6A7EF8AFC4C5960C608ECCEF82BD086BD6E0C0E2670199A5CA45`; and
2. a `popcapgame1.exe` child whose parent is that verified wrapper, with x86 timestamp `0x4D02B058`, fixed image base, and runtime object checks.

This matters because the Steam file on disk is DRM-wrapped and has a different disk timestamp. A matching filename or `FileVersion` is not enough. Every persistent code patch also verifies its original bytes before changing or restoring them.

## Process technique

- The application is a 32-bit external WPF program.
- Ordinary values use `ReadProcessMemory` / `WriteProcessMemory`.
- Object creation uses a short, auditable x86 code thunk that calls the game's existing functions. The page is allocated read/write, written, changed to execute/read, instruction-cache flushed, executed with a finite timeout, and released.
- The tool does not inject a DLL, modify `PlantsVsZombies.exe`, replace assets, or bypass Steam/DRM.
- The main-loop guard is restored in a `finally` path, and normal application exit restores enabled byte patches.
- Advanced Pause has no verified Steam 1096 runtime signature in this release candidate. Its button, hotkey, and focus-loss automation are disabled; no guessed `mStepMode` offset or AvZ 1051 address is used.
- Health bars use bulk `ReadProcessMemory` snapshots only. The transparent overlay is click-through, defaults off, and hides outside a foreground battle; it never writes health values.
- Challenge-rule background writes are disabled in this candidate until their per-object identity transaction passes a live Steam 1096 test.
- Beta 3 keeps all internal-function calls disabled, including the test workshop and Night Roof. Recognizing the verified game child never enables those calls by itself.
- If a remote call exceeds its timeout, the code/data pages and guard are retained, new calls are blocked, and application shutdown waits for the game thread to finish before cleanup.

## Save data

- Version 1.0 never edits profile fields.
- Backups copy the complete detected `userdata` directory.
- Each backup contains a JSON manifest with a SHA-256 hash for every file.
- Restore is blocked while the game is running and creates a new `before-restore` snapshot first.

## Scope

No online play, anti-cheat bypass, achievements spoofing, DRM bypass, executable redistribution, official artwork, or game data is included. Users must own a compatible copy.

## 中文摘要

- 只有磁盘完整 SHA-256、运行时 32 位 PE 时间戳、对象结构与补丁原字节全部符合时才允许写入。
- 补丁只恢复本工具亲自启用、且当前字节仍等于本工具值的内容；不会接管其他修改器的补丁。
- 高级暂停签名尚未实机确认，所以候选版完全禁用，不会用旧版本地址猜测。
- 远程调用超时时不会释放仍在执行的代码；关闭修改器会等待安全清理。
- 存档恢复前自动再备份一次，清单路径必须保持在保险箱与存档根目录内。
