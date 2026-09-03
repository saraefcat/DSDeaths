# DSDeaths

**English** | [日本語](README.ja.md)

Community-maintained fork of [quidrex/DSDeaths](https://github.com/quidrex/DSDeaths).
Version 1.2.0-rc1 adds fail-closed Elden Ring signature resolution verified on
App Ver. 1.16 and 1.17, a persistent run offset, and expanded read-only
address-finding utilities for future game updates.

## Purpose

This is an automatic death counter for FromSoftware games. It keeps reading your current death count from RAM while the game is running and writes it to a file when it changes. A sample use case is displaying your death count on stream using a Text Source in OBS Studio reading from the created file.
The death count is not reset when you enter NG+.

## Which games are supported?

 * DARK SOULS: Prepare To Die Edition
 * DARK SOULS II
 * DARK SOULS II: Scholar of the First Sin
 * DARK SOULS III
 * DARK SOULS: REMASTERED
 * Sekiro: Shadows Die Twice
 * Elden Ring (offline, disable EAC)

Most games still use patch-specific addresses. Elden Ring resolves its pointer
storage from a validated code signature instead of a fixed RVA. If a future
patch changes that signature, DSDeaths stops monitoring safely and reports the
failure instead of guessing an address.

## Elden Ring support

Elden Ring uses Easy Anti-Cheat to detect and deny trying to read from the process memory. Use your favorite search engine to find out how to disable EAC to play offline.

The Elden Ring signature was verified independently on App Ver. 1.16 and 1.17.
At startup, DSDeaths scans executable game memory and accepts the signature
only when exactly one match is found and it resolves inside `eldenring.exe`.
No known-version RVA fallback is used.

Use Elden Ring support only while the game is offline and Easy Anti-Cheat is
disabled.

## How do I use it?

Just double click it. It writes the current death count into `DSDeaths.txt` in the current directory.

Loading and character-selection screens may temporarily report `0`; DSDeaths
writes that value to `DSDeaths.txt` and resumes the active character's count
once it becomes available.

## Elden Ring run offset

Elden Ring stores one cumulative death count across New Game cycles. DSDeaths
can subtract a persistent zero baseline so a new run starts at `0` without
changing game memory.

While Elden Ring is connected, use these console keys:

- `Z`: use the current raw cumulative count as zero and enable the offset.
- `E`: enter an exact non-negative zero-baseline value and enable the offset.
- `O`: toggle the offset on or off without deleting its value.
- `H`: show the controls and current offset status.

The value and ON/OFF state are stored in `DSDeaths.settings.ini` next to the
executable and survive application restarts. `DSDeaths.txt` remains a plain
number. If the active character's raw count is below the saved baseline, the
output is clamped to `0` and DSDeaths prints a warning; toggle the offset off or
set a suitable baseline for that character.

## Maintenance utility

`DSDeaths.AddressFinder` is a separate x64, read-only utility for locating and
validating the Elden Ring death-count address after a game update. It is meant
for maintainers, not for normal counter use. See
[`DSDeaths.AddressFinder/README.md`](DSDeaths.AddressFinder/README.md) before
running it.
