# Client content recording verification

Run from the toolbox checkout:

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" run --project tests/ContentClientVerification/ContentClientVerification.csproj
```

This offline fixture exercises the real binary writers with synthetic snapshot
queries. No live database, application window or game process is opened. It checks
shipping item/string/shop/skill layouts, dependency fanout, locale/schema filtering,
staged-only writes, settings preservation and failures. The binary contracts are
the current game client's `Common/header/def_item.h`, `ShopData.cpp`, `Skill.cpp`
and `Secure/FileSecure.cpp`. The ordinary exporter's generic filename convention
remains separate from the shipping filenames selected by content recording.

The test leaves its isolated temporary directory printed on completion for audit.
