# Changelog

## [Unreleased]

- Added a persistent Elden Ring death-count offset for starting a new run at
  zero without changing the cumulative count stored by the game.
- Added runtime controls to set the current count as the zero baseline, edit
  the baseline, toggle it on or off, and display its current state.
- Added Address Finder signature research that scans for a compact direct
  death-count getter and reports every module-wide match and resolved target.

## [1.1.0] - 2026-08-28

- Updated the Elden Ring pointer-storage RVA from `0x03D5DF38` to
  `0x03D61F98` for App Ver. 1.17. The field offset remains `0x94`.
- Added `DSDeaths.AddressFinder`, a separate x64 read-only maintenance utility
  for exact count scanning, pointer discovery, and complete-restart RVA
  validation.
- Replaced the one-off `33504` launcher with a generic launcher that prompts
  for any known non-negative cumulative death count.
- Verified the Elden Ring counter after a complete game restart, repeated
  deaths, grace interaction, fast travel, same-character reload, DSDeaths
  restart, the no-character-loaded screen, and character switching.
- Kept all other game definitions and the normal DSDeaths output behavior
  unchanged.

Elden Ring support must only be used offline with Easy Anti-Cheat disabled.

## [1.0.0] - 2024-11-14

- Last release from the upstream `quidrex/DSDeaths` repository before this
  maintained fork.
