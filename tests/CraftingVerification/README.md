# Crafting editor verification

Run from the toolbox root with the .NET 9 SDK, Windows Forms runtime, Git for Windows and Docker. The fixture uses only **127.0.0.1:33317**, with an ephemeral MariaDB container and test-only credentials. The executable deliberately has no option to select the live toolbox database.

```powershell
docker compose -f tests/CraftingVerification/compose.yml up -d --wait
dotnet run --project tests/CraftingVerification/CraftingVerification.csproj -c Release -- <game-checkout> <absolute-output-directory>
docker compose -f tests/CraftingVerification/compose.yml down
```

The runner recreates `ep4_data` and `ep4_db` inside that disposable container, applies the game's actual crafting migrations 0013–0017, and loads its eight authored crafting seed files. Item metadata is synthetic fixture data. Player tables contain sentinels that must remain unchanged. The normal assembled game runtime is used read-only for an emblem fixture; no game executable is launched.

Checks cover the full catalogue, Cooking, skills and mastery validation, manual associations, random output probabilities, Unicode/path restrictions, stat alias overflow, rank reordering and retirement, concurrent save rejection, mid-save rollback, item-deletion protection, and character-data preservation. The suite also runs the canonical seed exporter against the fixture and imports its output back for a round-trip comparison. Exports go under the supplied output directory, never into the real game checkout.

The final check opens the editor off-screen against fixture data, exercises its tabs and checks the profession binding. It verifies that Teaching Items lists existing books without typing an ID, searches by name, filters assigned/unassigned books, reveals an ID hidden by a filter, and gives an explicit empty-state message. It edits a manual association through the Learning grid and verifies that the browser updates and the normal Save action persists it to the fixture. Screenshots and `results.json` are written to the output directory. It does not invoke item-picker interaction, change live content, or test actual crafting in the game.
