# DSDeaths.AddressFinder

Read-only maintenance utility for locating the Elden Ring cumulative death
counter after a game update. It is intentionally separate from the DSDeaths
runtime executable.

## Safety

- Use only while Elden Ring is offline and Easy Anti-Cheat is disabled.
- The tool opens `eldenring.exe` with `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`.
- It uses `VirtualQueryEx` and `ReadProcessMemory` only.
- It does not declare or call any process-memory write API.
- It is x64-only.

## Build

```powershell
dotnet build .\DSDeaths.AddressFinder\DSDeaths.AddressFinder.csproj -c Release
```

## Elden Ring 1.17 discovery

The known cumulative count measured on 1.16 is `33504` (`33503 -> 33504` was
observed after one death). Load the same character in 1.17, then run:

```powershell
.\DSDeaths.AddressFinder\bin\Release\net10.0-windows\DSDeaths.AddressFinder.exe --offline --known 33504
```

The tool performs an exact little-endian Int32 scan. After it finishes:

1. Die once in-game.
2. Enter the new cumulative count, normally `33505`.
3. Repeat only if multiple candidates remain.
4. Let the tool test legacy field offset `0x94`, search the main module for the
   structure pointer, calculate RVA candidates, and reproduce the existing
   DSDeaths traversal.

Do not put a reported RVA into `Program.cs` yet.

## Complete-restart validation

Completely exit Elden Ring and the Address Finder. Start Elden Ring 1.17 again,
load the same character, and run the exact validation command printed by the
discovery tool. Its form is:

```powershell
.\DSDeaths.AddressFinder\bin\Release\net10.0-windows\DSDeaths.AddressFinder.exe --offline --validate-rva 0xXXXXXXXX --offset 0x94 --expected 33505
```

Absolute addresses may change. The RVA must remain the same and `RESULT` must be
`MATCH`. Only then may the measured RVA and field offset be applied to DSDeaths.

## Output to retain

Retain or paste these lines into the investigation record:

- Elden Ring App Version
- known and final death counts
- initial and final candidate counts
- death address and structure base
- pointer storage, calculated RVA, and field offset
- DSDeaths-style validation result
- complete-restart validation result
