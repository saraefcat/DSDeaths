# DSDeaths.AddressFinder

Read-only maintenance utility for locating the Elden Ring cumulative death
counter after a game update. It is intentionally separate from the DSDeaths
runtime executable.

## Safety

- Live-process discovery, validation, and research modes must be used only while
  Elden Ring is offline and Easy Anti-Cheat is disabled.
- The offline executable compatibility check reads files only and does not
  require the game to be running.
- The tool opens `eldenring.exe` with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`.
- It uses `VirtualQueryEx` and `ReadProcessMemory` only.
- It does not declare or call any process-memory write API.
- It is x64-only.

## Check saved game versions without starting them

Run `Check-EldenRing-Backups.cmd` and enter the parent folder that contains the
backups. The tool recursively finds every `eldenring.exe`, calculates its
SHA-256 hash, scans executable PE sections, and requires exactly one focused
death-count signature whose pointer target remains inside the image.

The same check can be run directly against either one executable or a folder:

```powershell
.\DSDeaths.AddressFinder.exe --check-exe "D:\Elden Ring backups"
```

`PASS` is strong offline compatibility evidence, but it does not replace one
in-game read test before release. The executable is never started or modified.

## Build

```powershell
dotnet build .\DSDeaths.AddressFinder\DSDeaths.AddressFinder.csproj -c Release
```

## Discovery

Load a character whose exact current cumulative death count is known, then run
`Run-AddressFinder.cmd`. The executable asks for the current count. It can also
be started directly:

```powershell
.\DSDeaths.AddressFinder\bin\Release\net10.0-windows\DSDeaths.AddressFinder.exe --offline
```

To supply the count on the command line instead, use `--known` with any
non-negative decimal Int32 value. For example:

```powershell
.\DSDeaths.AddressFinder\bin\Release\net10.0-windows\DSDeaths.AddressFinder.exe --offline --known 123
```

The tool performs an exact little-endian Int32 scan. After it finishes:

1. Die once in-game.
2. Enter the new exact cumulative count. It must be greater than the previous
   count, but it does not need to increase by exactly one.
3. Repeat only if multiple candidates remain.
4. Let the tool test legacy field offset `0x94`, search the main module for the
   structure pointer, calculate RVA candidates, and reproduce the existing
   DSDeaths traversal.

Do not put a reported RVA into `Program.cs` yet.

## Complete-restart validation

Completely exit Elden Ring and the Address Finder. Start Elden Ring again,
load the same character, and run the exact validation command printed by the
discovery tool. Its form is:

```powershell
.\DSDeaths.AddressFinder\bin\Release\net10.0-windows\DSDeaths.AddressFinder.exe --offline --validate-rva 0xXXXXXXXX --offset 0x94 --expected 124
```

Here, `124` is only an example. Use the character's exact current cumulative
death count. Alternatively, omit `--expected` and enter the count when prompted.

Absolute addresses may change. The RVA must remain the same and `RESULT` must be
`MATCH`. Only then may the measured RVA and field offset be applied to DSDeaths.

## Signature research

`Analyze-RVA.cmd` is for researching a patch-independent signature. It first
validates a known RVA and expected death count, then scans only executable
regions of `eldenring.exe` for x64 RIP-relative instructions that resolve to
the pointer-storage address. It writes `DSDeaths.SignatureResearch.txt` by
default.

The report also scans the complete executable module for the compact direct
death-count getter shape observed in Elden Ring 1.17. The focused section
reports every match, the pointer-storage address resolved by each match, and
whether that address matches the validated target. A single focused match is
useful evidence, but it still must be compared with another game version
before it is used by DSDeaths at runtime.

The same research must be run against at least two game versions with their
independently validated RVAs. For the current investigation, use:

- Elden Ring 1.16: RVA `0x03D5DF38`
- Elden Ring 1.17: RVA `0x03D61F98`

Each candidate includes its instruction RVA, exact context bytes, and a pattern
where only the target `disp32` is replaced by `??`. These are research outputs,
not production signatures. Do not add a pattern to DSDeaths until it is unique
in each tested module and resolves the validated pointer chain after a complete
game restart.

## Output to retain

Retain or paste these lines into the investigation record:

- Elden Ring App Version
- known and final death counts
- initial and final candidate counts
- death address and structure base
- pointer storage, calculated RVA, and field offset
- DSDeaths-style validation result
- complete-restart validation result
- signature-research reports from each compared game version
