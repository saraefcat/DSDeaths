# DSDeaths

**English** | [日本語](README.ja.md)

Community-maintained fork of [quidrex/DSDeaths](https://github.com/quidrex/DSDeaths).

Version 1.2.0-rc1 adds:

- fail-closed Elden Ring signature resolution verified on App Ver. 1.16 and 1.17;
- a persistent run offset;
- read-only address-finding utilities for future game updates.

## Purpose

DSDeaths is an automatic death counter for FromSoftware games.

- It reads the current death count from RAM while a supported game is running.
- It writes changes to `DSDeaths.txt`.
- OBS Studio can display that file through a Text Source.
- It supports cumulative death counts that remain across New Game cycles.
- It opens game processes with read-only access.
- It does not write to game memory or modify save files.

## Supported games

- DARK SOULS: Prepare To Die Edition
- DARK SOULS II
- DARK SOULS II: Scholar of the First Sin
- DARK SOULS III
- DARK SOULS: REMASTERED
- Sekiro: Shadows Die Twice
- Elden Ring (offline with EAC disabled only)

Address resolution:

- Supported games other than Elden Ring use patch-specific addresses.
- Elden Ring resolves its pointer storage from a validated code signature.
- If the signature changes, DSDeaths stops monitoring and reports an error instead of guessing an address.

## Elden Ring support

Requirements:

- Start the game offline.
- Disable Easy Anti-Cheat (EAC).

EAC detects and blocks process-memory access. Meet both requirements before
using DSDeaths with Elden Ring.

Signature safety checks:

- The signature was verified independently on App Ver. 1.16 and 1.17.
- DSDeaths scans executable `eldenring.exe` memory at startup.
- It accepts exactly one match only when the resolved target is inside `eldenring.exe`.
- It does not fall back to a known-version RVA.

## Usage

Choose one interface and double-click its executable:

- `DSDeaths.exe`: original lightweight console interface
- `DSDeaths.Live.exe`: WPF interface for desktop and streaming use

Shared behavior:

- The current count is written to `DSDeaths.txt` next to the executable.
- Elden Ring offset settings are shared through `DSDeaths.settings.ini`.
- A shared instance lock prevents the console and GUI interfaces from running together.
- Loading and character-selection screens may temporarily output `0`.
- The active character's count returns when DSDeaths can read it again.

## DSDeaths Live

DSDeaths Live targets .NET Framework 4.8. Windows 11 normally requires no
additional application runtime installation.

Main features:

- automatic detection of every game supported by the console version;
- Japanese and English UI based on the Windows language, with English fallback;
- notification-area operation;
- displayed and raw death counts;
- `DSDeaths.txt` OBS output status;
- buttons to open the output folder or copy its full path;
- a transparent `DSDeaths Live Overlay` window;
- diagnostic-detail copying and a size-limited `DSDeaths.Live.log`.

### OBS overlay

1. Add a Window Capture source in OBS.
2. Select `DSDeaths Live Overlay`.
3. Enable **Allow Transparency**.

Controls:

- Move: drag the overlay.
- Hide: right-click it and select **Hide OBS overlay**.

The overlay is still under development. Verify it with the intended OBS
capture method before a public release.

### Overlay settings

Preferences are stored in `DSDeaths.Live.settings.ini` and restored after restart.

- Appearance: background opacity, text color, font, font size, and text shadow
- Size: 50% to 200% proportional scaling
- Position: save, lock, and reset
- Visibility: border, `DEATHS` label, and always-on-top behavior
- DPI: Per-Monitor V2 support for mixed-scaling multi-monitor layouts

The GUI and overlay do not change the plain-number format of `DSDeaths.txt`.
OBS text-output status appears below the Elden Ring run-offset panel.

### Status and diagnostic log

- Hover over the bottom status line to see its full message.
- **Copy details** copies the version, game, state, output path, and log path.
- The log intentionally omits death counts and per-death history and never accesses save files.
- Rotation occurs at 1 MiB and retains at most the current and previous log.
- No routine log maintenance is required.
- After closing the app, both log files can be deleted and are recreated when needed.

### Close button

Choose **Close button behavior** in the separate Application panel:

- minimize to the notification area;
- exit immediately.

**Exit** in the notification-area menu always closes the application.

## Elden Ring run offset

Elden Ring retains one cumulative death count across New Game cycles. The run
offset subtracts a saved zero baseline so a new run can display from `0`.
It does not change game memory.

Console keys available while connected to Elden Ring:

- `Z`: use the current cumulative count as zero and enable the offset.
- `E`: enter a non-negative baseline and enable the offset.
- `O`: toggle the offset without deleting the saved value.
- `H`: show the controls and current state.

Storage and output:

- The baseline and ON/OFF state are stored in `DSDeaths.settings.ini`.
- Settings survive application restarts.
- `DSDeaths.txt` contains only the adjusted number.
- If the raw count is below the baseline, output is clamped to `0` and a warning is shown.

If a warning appears, set a suitable baseline for that character or disable the offset.

In DSDeaths Live, the **Run offset** panel is available only while connected to
Elden Ring. It is disabled for other games, which do not currently use the offset.

## Maintenance utility

`DSDeaths.AddressFinder` is a read-only x64 utility for researching the Elden
Ring death-count address after a game update.

- Audience: maintainers rather than normal counter users
- Before use: read [`DSDeaths.AddressFinder/README.md`](DSDeaths.AddressFinder/README.md)
- `Scan-EldenRing-Executables.cmd` / `--check-exe`: scan without starting the game
- Input: one saved `eldenring.exe` or a backup folder
