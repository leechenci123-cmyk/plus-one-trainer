# Third-party notices

Plus One Trainer is distributed under **GPL-3.0-only**.

## PvZ Toolkit

- Project: <https://github.com/lmintlcx/pvztoolkit>
- Commit consulted: `99fcdecf53f80bd02d784eee243fa5b12a9e59c1`
- License: GPL-3.0
- Use in this project: the Steam 1.2.0.1096 compatibility profile, memory-field research, patch byte pairs, and external call conventions were adapted and reimplemented in C#. The upstream binary and source tree are not bundled.

## AsmVsZombies (AvZ)

- Project: <https://github.com/vector-wlc/AsmVsZombies>
- Commit consulted: `c42676c269b5b482a1eb9203a5b979e9d8a2a5c7`
- License: GPL-3.0
- Use in this project: public documentation and behavior of Advanced Pause, enum/terminology cross-checking, and read-only plant/zombie position and durability field research for the health overlay. AvZ’s 1.0 address profile and injector are not bundled or used against Steam.

## PvZ-Portable

- Project: <https://github.com/wszqkzqk/PvZ-Portable>
- Commit consulted: `b4f1ba08ab9eed8788bbb3f0d5f75c75d9c06fa6`
- License: LGPL-3.0-or-later
- Use in this project: behavioral cross-checking for game modes, terrain constraints, object capacities, and special-object hazards. No PvZ-Portable binary, game asset, or source file is bundled.

## PVZ Wiki

- Site: <https://wiki.pvz1.com/>
- License stated by the site: CC BY-SA 4.0 unless otherwise noted
- Use in this project: terminology and factual research. Documentation here is independently worded; no long passage is reproduced.

## OpenAI-generated original artwork

The sprout mechanic was generated specifically for this project using OpenAI image generation and then locally processed. It does not intentionally reproduce any official Plants vs. Zombies character or asset. See `docs/ARTWORK.md` for the prompt record.

## No-license repositories

Repositories without an explicit license—including historical trainer repositories sometimes mentioned by upstream projects—were not copied into this project.

## Microsoft .NET 8 and Windows Desktop Runtime

The self-contained Windows release includes Microsoft .NET 8 and Windows Desktop Runtime components. Their license and third-party notices are copied into every binary package as `DOTNET-RUNTIME-LICENSE.txt`, `DOTNET-RUNTIME-THIRD-PARTY-NOTICES.txt`, and `WINDOWS-DESKTOP-RUNTIME-LICENSE.txt`.
