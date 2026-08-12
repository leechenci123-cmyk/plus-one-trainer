# +1 修改器 1.0 功能表 / Plus One Trainer 1.0 feature matrix

| 层级 / Level | 功能 / Feature | 1.0 行为 / 1.0 behavior |
|---|---|---|
| 1 | 高级暂停 / Advanced Pause | 目标行为已定义，但 Steam 1096 签名尚待实机验证；当前 fail closed，不写未知地址 / target behavior defined, but the Steam 1096 signature awaits live verification; currently fails closed |
| 1 | 游戏速度 / Game speed | 0.25×–10×，F4 在原速与上次速度间切换 / F4 toggles original and last speed |
| 1 | 自动收集 / Auto collect | 阳光、硬币、钻石、巧克力及礼物 / sun, coins, diamonds, chocolate, and gifts |
| 1 | 场上血条 / Health bars | 植物与跟随移动的僵尸血条；外部只读、默认关闭 / plant and moving-zombie bars; external read-only overlay, off by default |
| 2 | 作弊 / Cheats | 阳光、无冷却、免费种植、植物无敌、白天蘑菇唤醒 / sun, no cooldown, free planting, invincible plants, awake mushrooms |
| 2 | 隐藏模式 / Hidden modes | 显示原版 Limbo Page；不改用户档解锁字段 / reveals the original Limbo Page without editing unlock fields |
| 2 | 测试工坊 / Test workshop | 0–32 全僵尸目录、行列验证、僵王专路、独立场地扶梯 / all zombie IDs, cell validation, a dedicated boss path, standalone grid ladders |
| 2 | 存档保险箱 / Save vault | 整目录备份、每文件 SHA-256、恢复前快照 / whole-directory snapshots, per-file SHA-256, pre-restore snapshot |
| 3 | 高级设置 / Advanced settings | 界面与计算完成；候选版禁用应用，等待安全事务实机验证 / UI and calculations complete; Apply disabled pending a live-tested safe transaction |

## 模式说明 / Mode notes

- Day/Night/Fog/Roof Endless are original hidden Limbo entries. Pool Endless is already an official Survival entry.
- Vasebreaker and I, Zombie stages live on the normal Puzzle page.
- Night Roof Endless is explicitly labeled an experiment: version 1.0 applies the boss-night rooftop background to a running Roof Endless battle. It is not presented as an original hidden mode.
- Internal Limbo entries such as Ice Level, Squirrel, Intro, and Upsell are documented but not given unsafe one-click launch buttons.

## 测试对象护栏 / Test-object guardrails

- Zombie IDs must be integers `0–32`.
- Pool rows accept only types with a native water state; roof high ground rejects Zomboni/Bobsled Team.
- Bobsled Team remains visible in the catalog but forced placement is disabled until all four native team objects can be tracked safely.
- Dr. Zomboss is accepted only in the original final-boss battle, with no existing boss and ample zombie capacity.
- A grid ladder requires a living, unsquished Wall-nut (`3`), Tall-nut (`23`), or Pumpkin (`30`), no existing ladder, a non-water row, and fewer than 120 live grid items.

## 发布状态 / Release status

This repository currently builds **1.0 Beta** (`1.0.0-beta.1`). Features that touch live game state are implemented behind exact version, runtime, byte, capacity, and ownership checks, but they remain unverified on the user's running copy until the household Steam library is available. See [LIVE_TEST_CHECKLIST.md](LIVE_TEST_CHECKLIST.md).
