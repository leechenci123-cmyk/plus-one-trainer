# +1 修改器 1.0 Beta / Plus One Trainer 1.0 Beta

技术版本 / Technical version: `1.0.0-beta.4`

这是第四个公开测试版本，只支持 Steam GOTY 英文版 `1.2.0.1096`（x86）的精确白名单文件。请先使用“存档保险箱”备份整个存档目录。

This is the fourth public beta and supports only the exact allow-listed English Steam GOTY `1.2.0.1096` x86 executable. Back up the complete save directory with Save Vault before testing.

## Beta 4 钱包功能 / Beta 4 wallet feature

- 作弊页新增“金钱 +1000”；首次点击自动备份存档，随后读取当前档案钱包、验证范围后增加 1000，最高不超过游戏原生范围。
- 明确自动收集使用游戏原生飞行与计分流程：金币到达左下钱包并调用计分后才增加余额。
- 金钱字段只影响当前档案钱包，不修改冒险、小游戏或隐藏模式解锁进度。

- Adds a “Money +1,000” action that creates a save backup on first use, then validates and increments the active profile wallet within the native range.
- Clarifies that auto collect retains the native flight and scoring path: a coin is credited after reaching the wallet and being scored.
- The money field affects only the active profile wallet; it does not edit Adventure, mini-game, or hidden-mode unlock progress.

## Beta 3 连接修复 / Beta 3 attachment fix

- 验证白名单 Steam 包装父进程后，连接由它启动的真正 `popcapgame1.exe` 游戏子进程，不再把包装壳当作游戏本体。
- 实机只读验证已确认子进程时间戳 `0x4D02B058`、Lawn/Board/UI/scene 和关键补丁原字节。
- Beta 3 开放只读血条、速度、自动收集、Limbo 与带原字节校验的常规作弊供测试。
- 测试工坊、扶梯、夜晚屋顶、高级暂停和挑战规则继续禁用，直到内部函数签名完成实机验证。

- Verifies the allow-listed Steam wrapper parent, then attaches to the real `popcapgame1.exe` child it launched instead of treating the wrapper as the game image.
- A live read-only probe confirmed child timestamp `0x4D02B058`, Lawn/Board/UI/scene, and critical patch preimages.
- Beta 3 exposes read-only health bars, speed, auto collect, Limbo, and ordinary exact-preimage patches for testing.
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
