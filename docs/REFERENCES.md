# 参考与致谢 / References and acknowledgements

`+1 修改器 / Plus One Trainer` 的界面与应用代码在本仓库中实现。兼容性研究、术语核对和风险边界参考了下列公开资料。

The user interface and application code are implemented in this repository. Public sources below were consulted for compatibility research, terminology, and safety boundaries.

## 高级暂停 / Advanced Pause

- [PVZ Wiki：高级暂停](https://wiki.pvz1.com/doku.php?id=%E6%94%BB%E7%95%A5%3A%E9%AB%98%E7%BA%A7%E6%9A%82%E5%81%9C) defines the community term as pausing game content without the ordinary pause overlay while plant interaction remains possible.
- [AsmVsZombies documentation](https://github.com/vector-wlc/AsmVsZombies/blob/c42676c269b5b482a1eb9203a5b979e9d8a2a5c7/inc/avz_game_controllor.h#L117-L137) documents a public Advanced Pause interface and its behavior.
- [AvZ tutorial](https://www.pvz1.com/avz/basic/advance_pause.html) provides community usage examples.

“高级暂停 / Advanced Pause”是 PVZ 社区长期使用的技术名称。目前没有找到能可靠证明单一首创者的一手资料，因此本项目不作“原作者”归属。若你掌握可核验的一手来源，欢迎提交 Issue。

“Advanced Pause” is a long-established PvZ community term and technique. We found no verifiable primary source identifying a sole inventor, so this project does not assign original authorship. Reliable primary evidence is welcome in an issue.

## Steam 1.2.0.1096 compatibility

The Steam-specific address profile and external-call ABI were cross-checked against [PvZ Toolkit at commit `99fcdec`](https://github.com/lmintlcx/pvztoolkit/tree/99fcdecf53f80bd02d784eee243fa5b12a9e59c1), including:

- [runtime version detection](https://github.com/lmintlcx/pvztoolkit/blob/99fcdecf53f80bd02d784eee243fa5b12a9e59c1/src/pvz.cpp#L149-L197);
- [the 1.2.0.1096 data profile](https://github.com/lmintlcx/pvztoolkit/blob/99fcdecf53f80bd02d784eee243fa5b12a9e59c1/src/data.cpp#L1623-L1801);
- [zombie and ladder call conventions](https://github.com/lmintlcx/pvztoolkit/blob/99fcdecf53f80bd02d784eee243fa5b12a9e59c1/src/pvz.cpp#L1083-L1281);
- [bounded remote-code execution and cleanup](https://github.com/lmintlcx/pvztoolkit/blob/99fcdecf53f80bd02d784eee243fa5b12a9e59c1/src/code.cpp#L112-L145).

Valve documents that the [Steam DRM wrapper modifies an application executable](https://partner.steamgames.com/doc/features/drm), which is why this tool validates the unpacked running image in addition to the disk hash.

## Modes and terminology

- [PvZ-Portable ChallengeScreen mode table](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Widget/ChallengeScreen.cpp#L46-L110) was used to cross-check Limbo Page membership and internal entries.
- [PVZ Wiki: Survival Endless introduction](https://wiki.pvz1.com/doku.php?id=%E6%8A%80%E6%9C%AF%3A%E7%94%9F%E5%AD%98%E6%97%A0%E5%B0%BD%E5%85%A5%E9%97%A8) documents the community convention that one seed selection is 20 waves / 2 flags.
- PvZ-Portable code independently shows [10 waves per flag](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Board.cpp#L556-L568) and [20 waves per Endless seed selection](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Board.cpp#L9374-L9383).

## Read-only health overlay

- Plant position and current/maximum health layout was cross-checked against the public [AvZ plant structure](https://github.com/vector-wlc/AsmVsZombies/blob/c42676c269b5b482a1eb9203a5b979e9d8a2a5c7/inc/avz_pvz_struct.h#L3039-L3158) and [PvZ-Portable Plant fields](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Plant.h#L1156-L1248).
- Zombie position and body/helmet/shield durability layout was cross-checked against the public [AvZ zombie structure](https://github.com/vector-wlc/AsmVsZombies/blob/c42676c269b5b482a1eb9203a5b979e9d8a2a5c7/inc/avz_pvz_struct.h#L3308-L3382).
- The overlay reads validated DataArray snapshots and renders them in a separate transparent window. These sources document structure; they do not constitute live validation of Steam 1096 screen-coordinate alignment.

## Test-workshop guardrails

- Zombie IDs were cross-checked against the [PvZ Toolkit name table](https://github.com/lmintlcx/pvztoolkit/blob/99fcdecf53f80bd02d784eee243fa5b12a9e59c1/src/window.cpp#L61-L96) and [PvZ-Portable enum](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/ConstEnums.h#L1340-L1378).
- Water/roof behavior was checked against [Board zombie placement](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Board.cpp#L2522-L2673) and [zombie terrain capabilities](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Zombie.cpp#L8277-L8296).
- Dr. Zomboss singleton, row, resource, and capacity hazards were cross-checked against [boss initialization](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Zombie.cpp#L685-L704), [boss attack-row logic](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Zombie.cpp#L9792-L9858), and [GetBossZombie](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Board.cpp#L9205-L9215).
- Standalone ladder validation follows the original [Ladder Zombie target filter](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Zombie.cpp#L6343-L6398), while retaining an eight-slot reserve below the [128 GridItem capacity](https://github.com/wszqkzqk/PvZ-Portable/blob/b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6/src/Lawn/Board.cpp#L72-L84).

These citations do not imply endorsement by their authors. Their licenses and this project’s reuse obligations are summarized in [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md).
