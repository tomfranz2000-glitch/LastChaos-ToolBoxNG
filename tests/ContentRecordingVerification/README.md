# Full content recording service verification

First prepare the isolated MariaDB fixture using
[`../ContentDatabaseVerification/README.md`](../ContentDatabaseVerification/README.md).
Keep that test database running. Then run from the Toolbox checkout:

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" run --project tests/ContentRecordingVerification -c Release -- C:/path/to/game tests/ContentDatabaseVerification/compose.yml artifacts/content-recording-integration
```

The test uses a fixed **33319** port and disposable fixture credentials. It
cannot accept a live database connection. It copies seeds/exporter into its own
temporary Git checkout and drives the actual recording service through a saved
change, repeated no-op, final-row deletion, client export failure, commit hook
failure and successful retry. It checks unrelated staged/worktree preservation,
snapshot cleanup and unchanged player sentinels. No game or Toolbox main window
is launched. The temporary checkout is deleted afterwards.

The first saved-change commit specifically exercises the new authored Artisan
perk table, including its subsequent unchanged/no-op click.


The Transmutation extension changes all five new authored tables and verifies one
recording click creates exactly one commit containing their five seeds. It then
checks no-op recording, deletion of the final equipment exception, client-export
failure, Git-hook rollback/retry, unrelated staged/unstaged preservation and player
data exclusion. Only the disposable 33319 endpoint and a temporary Git checkout are
used.

The Transmutation recording-service run passes all 13 success/failure/cleanup cases.
