# Elden Ring 1.17 investigation record

Status: Elden Ring 1.17 address discovery, complete-restart validation, and DSDeaths runtime validation passed.

## Known-good baseline

- Elden Ring App Version: 1.16
- Death count before test: 33503
- Death count after one observed death: 33504
- Existing RVA: `0x3D5DF38`
- Existing field offset: `0x94`

`33504` is the initial exact value for the 1.17 memory scan.

## Existing implementation

DSDeaths starts at the main module base and, for each configured offset, adds
the offset and reads eight bytes. For Elden Ring it therefore evaluates the
equivalent of `[[eldenring.exe + RVA] + fieldOffset]`, then casts the final
64-bit read to Int32. Address Finder reproduces this exact final validation.

## Reference implementations reviewed

- `quidrex/DSDeaths`: source of truth for pointer traversal and current 1.16
  values.
- `fosterbarnes/DSDeaths`: useful packaging and OBS output changes, but it keeps
  the same fixed Elden Ring RVA and does not solve patch independence.
- `JustNem0/DSDeaths`: useful monitor/responsibility separation, but it also
  keeps the same fixed RVA.
- `hwoyZ/DSDeathsCounter`: confirms x64 `uintptr_t`, read-only
  `ReadProcessMemory`, and explicit handle cleanup; it also uses the same fixed
  RVA.

No code or address was copied from these references. Their Elden Ring address
values are not accepted as evidence for 1.17.

## 1.17 measured results

- Elden Ring App Version: 1.17
- Initial known death count: 33504
- Initial candidate count: 270
- Final death count: 33505
- Final candidate count: 1
- Death address: `0x00007FF3AF1801F4`
- Structure base: `0x00007FF3AF180160`
- Pointer storage: `0x00007FF69FB41F98`
- New RVA: `0x03D61F98`
- New field offset: `0x94`
- Readable committed regions: 6399
- Readable committed bytes: 12081.8 MiB
- Partial/skipped chunks: 2 (`ERROR_PARTIAL_COPY`, Win32 error 299)
- DSDeaths-style expected/read: 33505 / 33505
- DSDeaths-style validation: PASS
- Complete-restart module base: `0x00007FF69BDE0000`
- Complete-restart pointer: `0x00007FF46AE40160`
- Complete-restart death address: `0x00007FF46AE401F4`
- Complete-restart expected/read: 33505 / 33505
- Complete game restart validation: PASS (exit code 0)
- DSDeaths startup count: PASS (33505)
- DSDeaths one-death increment: PASS (33505 -> 33506)
- DSDeaths process/architecture detection: PASS (`eldenring`, 64-bit)
- `DSDeaths.txt` validation: PASS
- Grace no-change validation: PASS
- Fast Travel no-change validation: PASS
- Same-character reload validation: PASS
- DSDeaths restart validation: PASS
- Repeated death increment validation: PASS
- Load/character-select screen behavior: count changes to 0 while no character is loaded
- Alternate-character behavior: changes to that character's stored count; one death increments it by 1
- DSDeaths runtime validation: PASS

The alternate character's absolute baseline count was not recorded, so only
the character switch and `N -> N + 1` behavior were verified for that character.

The 1.17 RVA candidate is `0x4060` bytes above the 1.16 RVA. This is a
plausibility observation only, not independent validation.

An RVA must not be added to `Program.cs` until both Address Finder validation
and complete-restart validation pass.

## 1.17 signature research

Address Finder commit `7cac6fc` scanned the complete executable module for the
compact getter identified from all RIP-relative references to the validated
pointer storage.

- Known RVA: `0x03D61F98`
- Expected/read: 33503 / 33503
- Query failures: 0
- Skipped chunks: 0
- References to validated pointer storage: 735
- Focused direct getter matches in the complete executable module: 1
- Getter instruction RVA: `0x00256020`
- Getter-resolved pointer storage: `0x00007FF69FB41F98`
- Resolved target versus known pointer storage: MATCH
- Focused pattern:
  `48 8B 05 ?? ?? ?? ?? 48 85 C0 74 07 8B 80 94 00 00 00 C3 C3`
- Report SHA-256:
  `B0CF8DD080E63F0CB60A7CF464E2A9C35B4C608EE6282BD3F7E56B00E49279B8`

The focused pattern is unique and resolves correctly in 1.17. At this stage,
production use remained blocked until the same semantic getter was checked
against 1.16 (or another independently validated game version).

## 1.16 signature comparison

The same Address Finder build and focused pattern were then run against the
independently validated 1.16 pointer-storage RVA.

- Module size: `0x5E01800`
- Known RVA: `0x03D5DF38`
- Expected/read: 33503 / 33503
- Query failures: 0
- Skipped chunks: 0
- References to validated pointer storage: 732
- Focused direct getter matches in the complete executable module: 1
- Getter instruction RVA: `0x00256050`
- Getter-resolved pointer storage: `0x00007FF6F3BBDF38`
- Resolved target versus known pointer storage: MATCH
- Focused pattern:
  `48 8B 05 ?? ?? ?? ?? 48 85 C0 74 07 8B 80 94 00 00 00 C3 C3`
- Report SHA-256:
  `D3D0B831C97EB2EDA855B7493FF52D0DCB3789F4D94F79763B0614DE42DDED40`

The pattern is therefore identical and unique in both tested versions. Only
the wildcarded RIP-relative displacement differs. This evidence is sufficient
to replace the Elden Ring fixed RVA with a fail-closed runtime signature
resolver. A final real-game test of the resolver build remains required before
merge or release.

## Dynamic resolver real-game validation

The runtime signature resolver build from commit `ae64f3e` was tested with
Elden Ring 1.16 and 1.17, offline with EAC disabled.

- Start Elden Ring 1.16, then start DSDeaths: PASS
- 1.16 resolver output matches the expected getter RVA, pointer-storage RVA,
  and field offset: PASS
- 1.16 initial raw count: 33503
- 1.16 one-death increment: PASS (33503 -> 33504)
- Keep DSDeaths running, completely exit 1.16, then start 1.17: PASS
- DSDeaths process-exit detection and automatic attachment to 1.17: PASS
- 1.17 initial raw count after attachment: 33504
- 1.17 one-death increment: PASS (33504 -> 33505)

The same DSDeaths process successfully resolved and monitored both game
versions across separate Elden Ring processes. Dynamic resolution and process
reattachment are validated. At this point, persistent offset controls still
required their final real-game acceptance test before merge or release.

## Persistent offset real-game validation

The persistent run-offset controls in the same `ae64f3e` runtime build passed
their final Elden Ring 1.17 acceptance test.

- Set the current raw count as zero with `Z`: PASS
- Display and `DSDeaths.txt` change to 0: PASS
- One-death increment with the offset enabled: PASS (0 -> 1)
- Toggle the offset OFF with `O` and display the raw cumulative count: PASS
- Toggle the offset ON again and restore the adjusted count: PASS
- Restart DSDeaths and retain the saved baseline and enabled state: PASS
- Resume from the same adjusted count after restart: PASS

All requested dynamic-resolution and persistent-offset behavior has now passed
unit, build, and real-game validation. The feature branch is ready for final
review and release-candidate packaging; it has not yet been merged or released.
