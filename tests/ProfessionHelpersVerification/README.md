# Profession spatial, balance and profile helpers

Run from a shell prepared by the game checkout's `tools/scripts/Enter-ClangClEnv.ps1`:

```powershell
dotnet run --project tests/ProfessionHelpersVerification -c Release -- <game-checkout> <output-directory>
```

This offline fixture validates polygon/map transforms, compiles the real production
C++ gathering headers and compares integer yield/chance/passive values and exact
random tap/hold schedules against the editor port. It invokes the actual game
`ImportSurfaceProfile.py` on a synthetic LOD0 distance bundle, checks embedded source
hash/FNV version/count metadata and rejects malformed results. No database, game
client or live item/NPC data is read or written. In-game placement/accessibility and
visual parity remain separate user checks.
