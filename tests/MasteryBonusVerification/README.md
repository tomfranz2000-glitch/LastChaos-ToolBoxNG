# Mastery bonus units

Run `dotnet run --project tests/MasteryBonusVerification/MasteryBonusVerification.csproj -- <evidence-directory>`.

This offline fixture verifies readable-unit conversions, legacy whole-point
semantics, exact JSON/raw-integer roundtrips, per-effect and combined caps across
retired recipes/resources/Artisan Ranks, alias-channel protection and cumulative
tier summaries. It constructs the actual three shared bonus grids and bulk-edit
dialog offscreen, edits their real bound controls and captures screenshots.
Application startup/database loading is not invoked; no live database, game
client, Git publication or normal Toolbox launch occurs.
