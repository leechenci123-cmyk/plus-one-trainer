# Plus One Trainer 1.0 Beta

[中文](README.md) · [Feature matrix](docs/FEATURES.md) · [Safety model](docs/SAFETY.md) · [References](docs/REFERENCES.md)

A convenience-first external utility for the classic single-player PC release of Plants vs. Zombies. Frequent actions stay on the home page; numeric cheats, test objects, and advanced challenge rules remain organized one or two levels deeper.

## 1.0 Beta highlights

- The Advanced Pause framework targets a frozen battle with planting, shoveling, seed selection, and Cob Cannon input. Its Steam 1096 runtime signature still needs live verification, so the current candidate safely disables the button, `F3`, and focus-loss pause instead of guessing an address.
- Game speed from `0.25×` to `10×` and auto-collection are implemented; every speed tier and pickup type remains on the live-test checklist.
- Toggleable read-only health bars for all plants and zombies, with zombie bars continuously following position. Coordinates and DPI scaling remain on the live-test checklist.
- Reveals the original Limbo Page for hidden Day/Night/Fog/Roof Endless modes and Limbo mini-games. Pool Endless remains on the normal Survival page.
- Night Roof is honestly labeled a scene-mixing experiment—not an alleged hidden original mode—and its entry point remains disabled in Beta 2.
- The spawn-count, wave-speed, full durability, and advanced-growth UI and calculations are complete. The candidate disables Apply until its background object transaction passes live verification.
- A test workshop catalogs zombie IDs `0–32`, including Yeti and both Gargantuars. All spawn and ladder buttons remain disabled in Beta 2 pending live verification of internal calls.
- A save vault performs whole-directory snapshots, per-file SHA-256 verification, and automatic pre-restore backups without editing profile fields.
- Simplified Chinese and English interface and READMEs, with bilingual summaries or English details in technical documents.

See the [feature matrix](docs/FEATURES.md) for exact guardrails.

## Supported build

Version 1.0 enables writes only for the allow-listed **English Steam GOTY 1.2.0.1096 (32-bit)** build:

```text
SHA-256  868F8E2BAB0D6A7EF8AFC4C5960C608ECCEF82BD086BD6E0C0E2670199A5CA45
Runtime PE timestamp  0x4D02B058 or official Steam wrapper 0x48ECEE74
```

Unknown versions stay read-only. Beta 2 accepts the known official Steam wrapper only when its disk hash, x86 architecture, fixed image base, and live game objects all pass validation; a filename or FileVersion match is not sufficient.

## Use

1. Download and extract `Plus-One-Trainer-1.0.0-beta.2-win-x86.zip` from GitHub Releases.
2. Start your legally obtained Steam copy, then run `PlusOneTrainer.exe`.
3. Wait for the green Attached status. Create a Save Vault snapshot before trying experimental features.
4. A normal trainer exit restores code patches still owned by this tool. If an internal call times out, the window waits for safe cleanup before exiting.

This is **1.0 Beta 2** (technical version `1.0.0-beta.2`). It fixes false rejection of official Steam wrapper timestamp `0x48ECEE74`. It must not be promoted to stable `1.0.0` until the [live-test checklist](docs/LIVE_TEST_CHECKLIST.md) passes. Test workshop, Night Roof, Advanced Pause, and challenge-rule writes intentionally remain disabled.

Administrator rights are not normally required. Security software may flag any external trainer for opening and writing another process; compare the release hash and inspect this source instead of adding broad antivirus exclusions.

## Safety boundary

- No game EXE, PAK, or Steam file modification; no DLL injection.
- No Steam/DRM bypass and no game or official asset redistribution.
- No network play, leaderboard, anti-cheat bypass, or achievement spoofing.
- Object creation uses a short auditable x86 thunk with write-then-execute memory protection, finite waits, and guarded restoration.
- Dr. Zomboss is allowed only in the native final-boss battle; pool/fog scenes, duplicate bosses, and low-capacity states are rejected.

Read the complete [safety model](docs/SAFETY.md).

## Build from source

Windows 10/11 and the .NET 8 SDK are required:

```powershell
./scripts/build.ps1 -Configuration Release
./scripts/test.ps1
./scripts/package.ps1 -Version 1.0.0-beta.2
```

The release is a self-contained single-file `win-x86` application.

## References and authorship

Advanced Pause is a long-established PvZ community term. No verifiable primary source identifies a sole inventor, so this project does not assign original authorship to an unverifiable screen name. Research consulted PVZ Wiki, AsmVsZombies, PvZ Toolkit, and PvZ-Portable. Permanent citations and license details are in [REFERENCES.md](docs/REFERENCES.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Copyright and non-affiliation

This is an unofficial, fan-made open-source utility. It is not affiliated with, sponsored by, authorized by, or endorsed by PopCap Games, Electronic Arts Inc., Valve Corporation, or Steam. Plants vs. Zombies, PopCap, EA, Steam, and related names and marks belong to their respective owners. This repository does not contain or distribute the game, executable, official artwork, music, fonts, or other game assets. Users must provide their own legally obtained compatible copy.

Original project code and explicitly identified original assets are licensed under [GPL-3.0-only](LICENSE). That license grants no rights to third-party game content or trademarks. The software is provided as-is; back up save data first.
