using System.Diagnostics;
using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.ContentRecording;
using MySqlConnector;

static class Program
{
    const string FixtureConnection = "Server=127.0.0.1;Port=33319;User ID=root;Password=content-recording-fixture-only;Database=ep4_data;Pooling=false";
    static int checks;
    static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS " + label); }
    static string Git(string checkout, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = checkout, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) throw new TimeoutException("Fixture Git timed out.");
        if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }
    static async Task Execute(string sql) { await using var c = new MySqlConnection(FixtureConnection); await c.OpenAsync(); await new MySqlCommand(sql, c).ExecuteNonQueryAsync(); }
    static async Task<string> Scalar(string sql) { await using var c = new MySqlConnection(FixtureConnection); await c.OpenAsync(); return Convert.ToString(await new MySqlCommand(sql, c).ExecuteScalarAsync())!; }
    [STAThread] static int Main(string[] args)
    {
        using var host = new LastChaos_ToolBoxNG.Main();
        // Main construction installs WinForms' synchronization context. This is
        // an offscreen service test, so run its async work without a UI message loop.
        return Task.Run(() => Run(args, host)).GetAwaiter().GetResult();
    }
    static async Task<int> Run(string[] args, LastChaos_ToolBoxNG.Main host)
    {
        if (args.Length != 3) throw new ArgumentException("Usage: ContentRecordingVerification <game-checkout> <fixture-compose.yml> <evidence-directory>. Requires the prepared ContentDatabaseVerification disposable fixture on 33319.");
        string game = Path.GetFullPath(args[0]), evidence = Path.GetFullPath(args[2]); Directory.CreateDirectory(evidence);
        string temporary = Path.Combine(Path.GetTempPath(), "lc-record-integration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var options = new ContentCaptureOptions(Path.GetFullPath(args[1]));
        // This executable cannot receive alternate database credentials. It only mutates the isolated fixture.
        Check(await Scalar("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='ep4_data' AND TABLE_NAME='t_item' AND COLUMN_NAME='payload'") == "1", "isolated synthetic fixture schema is present");
        try {
            foreach (var table in ContentRecordingPolicy.AllowlistedTables) {
                string target = Path.Combine(temporary, table.SeedRelativePath); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(game, table.SeedRelativePath), target);
            }
            File.Copy(Path.Combine(game, "x64-server/db/export-seed.sh"), Path.Combine(temporary, "x64-server/db/export-seed.sh"));
            await using (var initial = await ContentDatabaseSnapshot.CaptureAsync(temporary, FixtureConnection, options))
                foreach (var file in await initial.DumpSeedsAsync()) File.Copy(file.Value, Path.Combine(temporary, file.Key), true);
            Git(temporary, "init", "-b", "fixture"); Git(temporary, "config", "user.name", "Recording fixture"); Git(temporary, "config", "user.email", "recording@example.invalid");
            Git(temporary, "config", "core.autocrlf", "false"); Git(temporary, "config", "core.hooksPath", ".git/hooks");
            Git(temporary, "add", "."); Git(temporary, "commit", "-m", "Fixture baseline");
            File.WriteAllText(Path.Combine(temporary, "unrelated.txt"), "staged draft"); Git(temporary, "add", "unrelated.txt");
            File.AppendAllText(Path.Combine(temporary, "unrelated.txt"), "\nunstaged draft");
            string originalStatus = Git(temporary, "status", "--porcelain=v1");
            string player = await Scalar("SELECT GROUP_CONCAT(secret ORDER BY id) FROM ep4_db.t_characters");
            host.pSettings.WorkLocale = "usa"; host.pSettings.NationSupported = ["USA"];
            host.pSettings.ClientPath = Path.Combine(temporary, "source-client");
            string[] transmutationTables=["t_transmutation_settings","t_transmutation_facility","t_transmutation_facility_ingredient","t_transmutation_offer","t_transmutation_item_rule"];
            foreach(string table in transmutationTables)await Execute("UPDATE ep4_data."+table+" SET amount=amount+1");
            string before = Git(temporary, "rev-parse", "HEAD").Trim();
            var recorded = await ContentRecordingService.RecordAsync(host, temporary, FixtureConnection, options);
            Check(recorded.Changed && recorded.Files == 5 && recorded.Commit != before, "real service creates one local commit for all five saved Transmutation table changes");
            Check(Git(temporary, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim().Split('\n').Select(s=>s.Trim()).SequenceEqual(transmutationTables.Select(table=>"x64-server/db/seeds/ep4_data/"+table+".sql").Order(StringComparer.Ordinal)), "commit contains exactly all five authored Crucible seeds");
            Check(Git(temporary, "status", "--porcelain=v1") == originalStatus && File.ReadAllText(Path.Combine(temporary, "unrelated.txt")).EndsWith("unstaged draft"), "service preserves unrelated staged and unstaged work");
            var noChange = await ContentRecordingService.RecordAsync(host, temporary, FixtureConnection, options);
            Check(!noChange.Changed && Git(temporary, "rev-parse", "HEAD").Trim() == recorded.Commit, "second click with unchanged saved content creates no commit");
            await Execute("DELETE FROM ep4_data.t_transmutation_item_rule");
            var emptied = await ContentRecordingService.RecordAsync(host, temporary, FixtureConnection, options);
            Check(emptied.Changed && emptied.Files == 1 && !File.ReadAllText(Path.Combine(temporary, "x64-server/db/seeds/ep4_data/t_transmutation_item_rule.sql")).Contains("INSERT INTO"), "service records deletion of the final row");
            string stableHead = Git(temporary, "rev-parse", "HEAD"), stableStatus = Git(temporary, "status", "--porcelain=v1");
            string itemBefore = await Scalar("SELECT amount FROM ep4_data.t_item WHERE id=1");
            await Execute("UPDATE ep4_data.t_item SET amount=amount+1");
            try { await ContentRecordingService.RecordAsync(host, temporary, FixtureConnection, options); throw new Exception("Client failure unexpectedly succeeded"); }
            catch (MySqlException) { Check(true, "required client export failure is propagated"); }
            Check(Git(temporary, "rev-parse", "HEAD") == stableHead && Git(temporary, "status", "--porcelain=v1") == stableStatus, "client export failure publishes no partial seed or commit");
            await Execute("UPDATE ep4_data.t_item SET amount=" + itemBefore.Replace(',', '.'));
            await Execute("UPDATE ep4_data.t_crafting_settings SET amount=amount+1");
            string hook = Path.Combine(temporary, ".git/hooks/pre-commit"); File.WriteAllText(hook, "#!/bin/sh\nprintf 'fixture rejects commit\\n' >&2\nexit 1\n");
            try { await ContentRecordingService.RecordAsync(host, temporary, FixtureConnection, options); throw new Exception("Hook failure unexpectedly succeeded"); }
            catch (InvalidOperationException e) when (e.Message.Contains("fixture rejects commit")) { Check(true, "real commit hook failure is reported"); }
            Check(Git(temporary, "rev-parse", "HEAD") == stableHead && Git(temporary, "status", "--porcelain=v1") == stableStatus, "commit failure restores output files and preserves user index");
            File.Delete(hook);
            var retry = await ContentRecordingService.RecordAsync(host, temporary, FixtureConnection, options);
            Check(retry.Changed && retry.Files == 1, "retry records the saved database change after Git error is fixed");
            Check(await Scalar("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME LIKE 'lc_record_%'") == "0", "all success and failure paths remove temporary snapshot schemas");
            Check(await Scalar("SELECT GROUP_CONCAT(secret ORDER BY id) FROM ep4_db.t_characters") == player, "player fixture sentinels remain unchanged");
            File.WriteAllText(Path.Combine(evidence, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks, liveDatabaseChanged = false, isolatedPort = 33319 }));
            Console.WriteLine($"PASS {checks} full-service checks on isolated 33319 and temporary Git checkout");
            return 0;
        } finally {
            string resolved = Path.GetFullPath(temporary);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("lc-record-integration-")) throw new InvalidOperationException("Unsafe fixture cleanup path.");
            foreach (string file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, true);
        }
    }
}
