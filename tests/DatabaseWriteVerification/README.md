# Database write integration verification

This small console executable links the production `DatabaseWriteBatch.cs` directly. It exercises successful commits and failures against a disposable MariaDB instance: duplicate-key rollback after a deletion, missing targets, MyISAM refusal, SQL quoting, forbidden transaction/schema statements, strict numeric limits, custom triggers, temporary-table cleanup and retries, generated IDs, and a connection killed during a partially executed transaction.

The endpoint is deliberately fixed to **127.0.0.1:33316**, with root and an empty password, and the only writable database is **toolbox_write_tests**. It does not read ToolBox settings, use the normal database port, or access `ep4_data`. Every case resets only its own fixture tables. The program must not be pointed at a live database.

From the repository root, start an empty disposable fixture if port 33316 is not already serving the test container:

```powershell
docker run --detach --name toolbox-write-verification --publish 127.0.0.1:33316:3306 --env MARIADB_ALLOW_EMPTY_ROOT_PASSWORD=1 mariadb:11.4.12
docker exec toolbox-write-verification healthcheck.sh --connect --innodb_initialized
```

Wait until the health check succeeds, then run with the .NET 9 SDK:

```powershell
dotnet run --project tests/DatabaseWriteVerification/DatabaseWriteVerification.csproj -c Release
```

For the local SDK installed during the desktop build, replace `dotnet` with `& "$env:USERPROFILE\.dotnet\dotnet.exe"`.

A successful run prints **27/27 integration cases passed** and returns exit code 0. The JSON report is written beside the executable under `bin/Release/net9.0/verification.json`. An optional final argument filters case names; omit it for the complete run.

If this command created the fixture container, remove it after verification:

```powershell
docker rm --force toolbox-write-verification
```

No application windows are opened. The main ToolBox project excludes the test source tree from its compile items.
