# DSDeaths

Community-maintained fork of [quidrex/DSDeaths](https://github.com/quidrex/DSDeaths).
Version 1.2.0-rc1 adds fail-closed Elden Ring signature resolution verified on
App Ver. 1.16 and 1.17, a persistent run offset, and expanded read-only
address-finding utilities for future game updates.

## Purpose

This is an automatic death counter for FromSoftware games. It keeps reading your current death count from RAM while the game is running and writes it to a file when it changes. A sample use case is displaying your death count on stream using a Text Source in OBS Studio reading from the created file.
The death count is not reset when you enter NG+.

DSDeaths opens game processes with read-only access. It does not write to game
memory or modify save files.

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

Choose either front end and double-click it:

- `DSDeaths.exe` is the original lightweight console interface.
- `DSDeaths.Live.exe` is the WPF interface for desktop and streaming use.

Both use the same monitoring core and write the current death count to
`DSDeaths.txt` next to the executable. They also share
`DSDeaths.settings.ini`, so the Elden Ring offset follows you between the two
interfaces. Run only one interface at a time; a shared instance lock prevents
both from writing the output file simultaneously.

Loading and character-selection screens may temporarily report `0`; DSDeaths
writes that value to `DSDeaths.txt` and resumes the active character's count
once it becomes available.

## DSDeaths Live

DSDeaths Live targets .NET Framework 4.8 and is intended for Windows 11 without
an additional application runtime installation. It provides:

- automatic detection of every game supported by the console version;
- Japanese and English UI, with Windows-language detection and English
  fallback;
- notification-area operation;
- the current displayed and raw death counts;
- status for the `DSDeaths.txt` OBS text output;
- an optional transparent `DSDeaths Live Overlay` window for OBS Window
  Capture.

For the overlay, add a Window Capture source in OBS, select
`DSDeaths Live Overlay`, and enable **Allow Transparency**. The overlay can be
dragged to a convenient position. To hide it, right-click the overlay and
choose **Hide OBS overlay**. This is an early overlay implementation and should
be verified with the intended OBS capture method before a public release.

GUI-only preferences are stored in `DSDeaths.Live.settings.ini`. The overlay
and GUI do not change the plain-number format of `DSDeaths.txt`. The Display
panel can adjust the overlay background opacity without fading the counter
text, and can choose the counter text color with the Windows color picker.
These choices are restored when DSDeaths Live starts again.

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

In DSDeaths Live, the same controls are available in the **Run offset** panel
only while Elden Ring is connected. The panel is disabled for every other game;
offset support has not been enabled for those titles.

## Maintenance utility

`DSDeaths.AddressFinder` is a separate x64, read-only utility for locating and
validating the Elden Ring death-count address after a game update. It is meant
for maintainers, not for normal counter use. See
[`DSDeaths.AddressFinder/README.md`](DSDeaths.AddressFinder/README.md) before
running it.
