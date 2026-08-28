# Elden Ring 1.17 investigation record

Status: address discovery and complete-restart validation passed; DSDeaths runtime validation pending.

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
- DSDeaths runtime validation: pending

The 1.17 RVA candidate is `0x4060` bytes above the 1.16 RVA. This is a
plausibility observation only, not independent validation.

An RVA must not be added to `Program.cs` until both Address Finder validation
and complete-restart validation pass.
