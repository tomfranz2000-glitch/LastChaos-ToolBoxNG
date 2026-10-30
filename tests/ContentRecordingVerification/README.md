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
