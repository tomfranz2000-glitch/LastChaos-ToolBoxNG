# Profession catalogue verification

This backend fixture uses only the fixed disposable endpoint **127.0.0.1:33318**.
It never reads Toolbox connection settings or connects to the game database.
Docker and the .NET 9 Windows SDK/runtime are required.

```powershell
docker compose -f tests/ProfessionsVerification/compose.yml up -d --wait
dotnet run --project tests/ProfessionsVerification/ProfessionsVerification.csproj -c Release -- <game-checkout> <output-directory>
docker compose -f tests/ProfessionsVerification/compose.yml down
```

The setup applies actual profession DDL through 0043, projecting content sections
of coordinated historical migrations when unrelated baseline schemas are not
needed. It loads all 29 authored seed tables and synthesizes item/NPC/zone metadata
for their references. It adds inventory, XP, tier and Artisan Rank sentinels.
No schema or player data is modified outside this temporary container.

Checks cover:

- Exact schema-to-editor metadata coverage for all 18 added tables.
- Full 29-table load/save, source+mastery creation and retirement, stable IDs and
  profession ownership, disabled source availability and Wave display-only outputs.
- Per-profession skill/timing/tool/loot limits, source references, tier definitions,
  geometry imports, polygon topology/centimetre quantization and world-validation notes.
- Concurrent changes in new tables, export locks, unique NPC-binding swaps and
  reordered children.
- A late real SQL constraint failure after parent edits/child rewrites, proving
  rollback also restores temporary unique-key values and all player sentinels.
- Quoted/Unicode names, FLOAT/DOUBLE coordinate preservation and a second local
  save immediately after fractional coordinate editing.

The repository reads spatial columns through `CAST(column AS DOUBLE)` so the
MariaDB text protocol cannot silently truncate FLOAT values to six significant
figures. Fingerprints and drafts normalize to the real FLOAT/DOUBLE representation
before validation/write; they do not depend on a fallible reload after COMMIT.
The polygon validator separately mirrors the game's existing text-to-centimetre
coordinate interpretation. Offline shape validity does not prove a body contains
water or that herb positions are walkable; the deployed world still needs checking.

The existing `tests/CraftingVerification` suite shares the actual 29-table setup at
its own fixed 33317 endpoint. It verifies legacy recipe/teaching/Artisan/tier behavior,
canonical export/import and an offscreen editor Save against fixture data. The
`tests/ProfessionEditorVerification` and `tests/ProfessionHelpersVerification`
suites cover new page bindings/maps/previews and native helper parity separately.

The complete backend run passed **74 checks**; its `result.json` records pass/fail, table scope and the executed count. The adapted CraftingVerification run passed **22/22**, including the normal offscreen Save path and current-cell selection regressions for rank/resource details.

Construction coverage includes the real 0043 schema, separate build/brewing switches, plot text/model paths, free builds, material references, and a late cost-insert failure. Existing built-station ownership and operation receipts remain byte-equivalent through successful and failed content saves.
