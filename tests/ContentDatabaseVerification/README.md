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
test-only password from their Compose files. The program recreates the 123
approved content tables with synthetic data. Tests cover snapshot coherence,
legacy engines, player-data exclusion, blob/Unicode preservation, canonical
mixed-field normalization, repeatability, empty content, cancellation, and
cleanup. It prints a prepared synthetic game checkout for integrated Git tests.

After the full suite has prepared the fixture, optional `--targeted` verifies
reader-pool cleanup and all six explicit unseeded-editor guards without resetting
the main tables. `--compare-host` takes a read-only snapshot and compares all 123
batch-exported files with the original host Docker-per-query transport using a
frozen copy of the same canonical script (about three minutes).

After all dependent verification is finished:

```powershell
docker compose -f tests/ContentDatabaseVerification/compose.yml down -v
docker compose -f tests/ContentDatabaseVerification/identity-mismatch.yml down -v
```
