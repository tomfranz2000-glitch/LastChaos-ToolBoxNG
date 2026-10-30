# Content recording UI verification

Run from the Toolbox checkout with .NET 9 on Windows:

```powershell
& "$env:USERPROFILE/.dotnet/dotnet.exe" run --project tests/ContentRecordingUiVerification -c Release -- . artifacts/content-recording-ui
```

Creates the real main-window controls offscreen, loads a temporary copy of the
old settings format, verifies checkout persistence and close-during-recording
behaviour, and captures normal/minimum layouts. It does not run `Main_Load`,
connect to a database, invoke recording, launch the game or modify real settings.
