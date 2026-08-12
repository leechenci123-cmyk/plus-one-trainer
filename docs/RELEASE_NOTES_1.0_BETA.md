# +1 修改器 1.0 Beta / Plus One Trainer 1.0 Beta

技术版本 / Technical version: `1.0.0-beta.2`

这是第二个公开测试版本，只支持 Steam GOTY 英文版 `1.2.0.1096`（x86）的精确白名单文件。请先使用“存档保险箱”备份整个存档目录。

This is the second public beta and supports only the exact allow-listed English Steam GOTY `1.2.0.1096` x86 executable. Back up the complete save directory with Save Vault before testing.

## Beta 2 兼容性修复 / Beta 2 compatibility fix

- 识别同一白名单正版 EXE 在 Steam DRM 包装进程中显示的运行时间戳 `0x48ECEE74`，不再把它直接误判为其他游戏版本。
- 仍要求固定 SHA-256、x86、映像基址、Lawn/Board 指针以及 UI/模式/场景值域全部通过。
- Beta 2 优先开放只读血条、速度、自动收集、Limbo 与带原字节校验的常规作弊。
- 测试工坊、扶梯、夜晚屋顶、高级暂停和挑战规则继续禁用，直到内部函数签名完成实机验证。

- Recognizes runtime timestamp `0x48ECEE74` exposed by the official Steam DRM wrapper for the same allow-listed executable.
- Exact SHA-256, x86 architecture, image base, Lawn/Board pointers, and UI/mode/scene ranges must still pass.
- Beta 2 prioritizes read-only health bars, speed, auto collect, Limbo, and ordinary exact-preimage patches.
- Test workshop, ladder placement, Night Roof, Advanced Pause, and challenge rules remain disabled pending live signature verification.

## 本版可测试 / Available for testing

- 中英文手绘风格界面 / bilingual hand-drawn UI
- 存档整目录备份、SHA-256 校验与恢复前快照 / whole-save snapshots, SHA-256 verification, and pre-restore backup
- 可选植物与跟随僵尸血条，只读覆盖层 / optional plant and moving-zombie read-only health overlay
- 游戏速度、自动收集与分层作弊功能 / game speed, auto collect, and organized cheats
- 一周目后显示 Limbo Page / Limbo Page reveal after the active profile completes Adventure once

## 本版有意禁用 / Intentionally disabled

- 高级暂停：Steam 1096 唯一运行时签名尚待实机确认 / Advanced Pause: unique Steam 1096 runtime signature awaits live verification
- 高难度规则写入：安全对象事务尚待实机确认 / challenge-rule writes: safe object transaction awaits live verification
- 测试工坊、扶梯与夜晚屋顶：Steam 包装运行时的内部调用尚待实机确认 / workshop, ladders, and Night Roof: internal calls await live verification on the Steam wrapper runtime

## 反馈 / Feedback

提交问题时请附上 Windows 版本、游戏版本、操作步骤，以及是否使用其他修改器。不要上传游戏 EXE、存档或任何个人文件；如确有需要，先对内容脱敏。

When filing an issue, include Windows version, game version, reproduction steps, and whether another trainer was active. Do not upload the game executable, saves, or personal files; redact any diagnostic material first.
