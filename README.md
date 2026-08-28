# DSDeaths

Community-maintained fork of [quidrex/DSDeaths](https://github.com/quidrex/DSDeaths).
Version 1.1.0 adds verified Elden Ring App Ver. 1.17 support and a read-only
address-finding utility for future game updates.

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

 Note that only the current patch as of the time of release works. Please open a ticket if there is a new patch and it stops working.

## Elden Ring support

Elden Ring uses Easy Anti-Cheat to detect and deny trying to read from the process memory. Use your favorite search engine to find out how to disable EAC to play offline.

The current Elden Ring address was verified on App Ver. 1.17.

Use Elden Ring support only while the game is offline and Easy Anti-Cheat is
disabled.

## How do I use it?

Just double click it. It writes the current death count into `DSDeaths.txt` in the current directory.

## Maintenance utility

`DSDeaths.AddressFinder` is a separate x64, read-only utility for locating and
validating the Elden Ring death-count address after a game update. It is meant
for maintainers, not for normal counter use. See
[`DSDeaths.AddressFinder/README.md`](DSDeaths.AddressFinder/README.md) before
running it.
