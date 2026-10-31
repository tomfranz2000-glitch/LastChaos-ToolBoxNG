# Profession editor offline checks

This fixture reads tracked content seeds and shows the real WinForms editor off
screen. It uses no database, credentials, server or game client. The source browser,
named references, units, inactive fields, stable IDs, parent filters and the real
NPC-picker cancel path run against an in-memory draft. Captures cover normal and
minimum supported window sizes; browsing must leave its fingerprint unchanged.

From the Toolbox checkout:

```powershell
python tests/ProfessionEditorVerification/seed_snapshot.py <game-checkout> <snapshot.json>
dotnet run --project tests/ProfessionEditorVerification -c Release -- <game-checkout> <snapshot.json> <output-directory>
```

PNG files and `result.json` are written only to the supplied output directory.
These checks establish editor behavior/layout, not in-game gathering behavior or
world accessibility. Database transaction and engine-rule checks are separate.

The Infusions checks use the actual authored potion, skill and magic seed rows.
They cover the six station pages, derived and immutable effect identities,
unsupported lifecycle flags, costs/availability, safe model paths, exact native
hundredth units, JSON roundtrip and the content-recording/player-data boundary.
Station access checks cover named Artisan Rank choices, None/unrestricted access,
retired durable IDs, missing references and preserving IDs independently of order.

The construction extension passes **139 checks**, including editable one-time costs, separate availability controls, build-only rank copy, unbuilt plot fields, free builds, duplicate/material/quantity limits, and player-data exclusion. The Station page puts both availability switches and the build rank first; normal and minimum-size screenshots are captured.
