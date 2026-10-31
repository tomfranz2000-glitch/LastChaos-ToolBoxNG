This fixture uses only disposable MariaDB containers. It never exports the real
game database or invokes the Toolbox recording button against it.

Run from the Toolbox checkout:

```powershell
docker compose -f tests/ContentDatabaseVerification/compose.yml up -d --wait
docker compose -f tests/ContentDatabaseVerification/identity-mismatch.yml up -d --wait
& "$env:USERPROFILE/.dotnet/dotnet.exe" run --project tests/ContentDatabaseVerification/ContentDatabaseVerification.csproj -- C:/path/to/game-checkout
```

The primary fixture listens only on 127.0.0.1:33319. The identity-mismatch
container exposes no port. Both use temporary in-memory database storage and the
test-only password from their Compose files. The program recreates the 132
approved content tables with synthetic data. Tests cover snapshot coherence,
legacy engines, player-data exclusion, blob/Unicode preservation, canonical
mixed-field normalization, repeatability, empty content, cancellation, and
cleanup. It prints a prepared synthetic game checkout for integrated Git tests.

After the full suite has prepared the fixture, optional `--targeted` verifies
reader-pool cleanup and all six explicit unseeded-editor guards without resetting
the main tables. `--compare-host` takes a read-only snapshot and compares all 132
batch-exported files with the original host Docker-per-query transport using a
frozen copy of the same canonical script (about three minutes).

After all dependent verification is finished:

```powershell
docker compose -f tests/ContentDatabaseVerification/compose.yml down -v
docker compose -f tests/ContentDatabaseVerification/identity-mismatch.yml down -v
```

Construction adds one authored table (`t_alchemy_facility_ingredient`), bringing the reviewed recording set to 124 before Artisan perks. A separate built-station secret sentinel in `ep4_db.t_alchemy_facility_unlock` must never appear in any of those exported files.

The Artisan nonstat-perk table is authored content; including it increases the
reviewed recording set to 125. Earned ranks remain player-owned and excluded.


The Transmutation extension raises the reviewed recording set to 130. All five
authored Crucible tables are included. Separate secret wallet, queue, receipt and
delivery sentinels in ep4_db cannot be read by the restricted snapshot account or
appear in any exported seed.

The Transmutation recording/export run passes 37 checks across 130 authored tables.

Accessory Display adds two authored configuration tables, bringing the reviewed
set to 132. Character construction, exact stored equipment and display receipts
remain protected player tables, including future unreviewed display tables.
