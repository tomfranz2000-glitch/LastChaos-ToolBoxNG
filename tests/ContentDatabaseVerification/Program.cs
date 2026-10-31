using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using LastChaos_ToolBoxNG.ContentRecording;
using MySqlConnector;

string checkout = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass the game checkout path.");
string compose = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "compose.yml"));
const string connectionString = "Server=127.0.0.1;Port=33319;User ID=root;Password=content-recording-fixture-only;Database=ep4_data;Allow Zero Datetime=true;Pooling=false";
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception("FAILED: " + message); Console.WriteLine("PASS " + message); checks++; }
async Task Exec(string sql) { await using var c = new MySqlConnection(connectionString); await c.OpenAsync(); using var cmd = new MySqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
async Task<int> CountScratch() { await using var c = new MySqlConnection(connectionString); await c.OpenAsync(); using var cmd = new MySqlCommand("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME LIKE 'lc_record_%'", c); return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
string genericColumns = "id INT PRIMARY KEY, value LONGTEXT CHARACTER SET utf8mb4, payload LONGBLOB, amount DECIMAL(20,8), empty_value VARCHAR(100) NULL, zero_date DATETIME DEFAULT '0000-00-00 00:00:00', latin_text VARCHAR(100) CHARACTER SET latin1 DEFAULT ''";
if (args.Contains("--compare-host"))
{
    await using var snapshot = await ContentDatabaseSnapshot.CaptureAsync(checkout,connectionString,new ContentCaptureOptions(compose));
    var batch = await snapshot.DumpSeedsAsync();
    var hashes = batch.ToDictionary(p=>p.Key,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Value))));
    string reference = Path.Combine(snapshot.StagingDirectory,"host-reference");
    foreach(ContentTable table in ContentRecordingPolicy.AllowlistedTables) { string target = Path.Combine(reference,table.CanonicalSchema,table.Table+".sql"); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target,""); }
    string frozen = Path.Combine(snapshot.StagingDirectory,"frozen-export-seed.sh");
    File.Copy(Path.Combine(checkout,"x64-server","db","export-seed.sh"),frozen);
    using var database = snapshot.Query("utf8","SELECT DATABASE()");
    string prefix = ((string)database.Rows[0][0])[..^"ep4_data".Length];
    var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Git","bin","bash.exe")) { WorkingDirectory=checkout,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
    info.ArgumentList.Add(frozen.Replace('\\','/'));
    foreach(ContentTable table in ContentRecordingPolicy.AllowlistedTables) info.ArgumentList.Add(table.Key);
    info.Environment["LC_DB_CONN"]="docker";info.Environment["LC_DB_COMPOSE_FILE"]=compose.Replace('\\','/');info.Environment["LC_DB_SEEDS_DIR"]=reference.Replace('\\','/');info.Environment["LC_DB_SOURCE_PREFIX"]=prefix;info.Environment["LC_DB_ALLOW_EMPTY"]="1";info.Environment["MARIADB_ROOT_PASSWORD"]="content-recording-fixture-only";
    var timer=Stopwatch.StartNew(); using var process=Process.Start(info)!;
    Task<string> output=process.StandardOutput.ReadToEndAsync();Task<string> error=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();string text=await output;string errors=await error;
    Check(process.ExitCode==0,"frozen canonical host exporter succeeds: "+errors);
    bool identical = true;
    foreach(ContentTable table in ContentRecordingPolicy.AllowlistedTables)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(reference,table.CanonicalSchema,table.Table+".sql"));
        identical &= hashes[table.SeedRelativePath] == Convert.ToHexString(SHA256.HashData(bytes));
    }
    Check(identical,"batch and host canonical transports produce identical bytes for all130 tables");
    Console.WriteLine($"TIMING host Docker-per-query full130 export: {timer.Elapsed.TotalSeconds:F2}s");
    return;
}
if (args.Contains("--targeted"))
{
    await using (var snapshot = await ContentDatabaseSnapshot.CaptureAsync(checkout,connectionString,new ContentCaptureOptions(compose)))
    {
        foreach(string charset in new[]{"utf8","utf8mb4"})
        {
            using var one = snapshot.Query(charset,"SELECT id FROM ep4_data.t_item LIMIT 1");
            using var two = snapshot.Query(charset,"SELECT id FROM ep4_data.t_item LIMIT 1");
            Check(one.Rows.Count==two.Rows.Count,"pooled immutable reads work for charset "+charset);
        }
        try { snapshot.Query("utf8","SELECT * FROM ep4_db.t_characters");throw new Exception("Player access accepted"); }
        catch(MySqlException) { Check(true,"pooled reader still cannot access live player tables"); }
    }
    Check(await CountScratch()==0,"pooled capture disposal removes scratch schemas");
    await using(var verify = new MySqlConnection(connectionString))
    {
        await verify.OpenAsync();
        using var users = new MySqlCommand("SELECT COUNT(*) FROM mysql.user WHERE User LIKE 'lc_record_%'",verify);
        Check(Convert.ToInt32(await users.ExecuteScalarAsync())==0,"snapshot reader users are removed after clearing pools");
        using var sessions = new MySqlCommand("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER LIKE 'lc_record_%'",verify);
        Check(Convert.ToInt32(await sessions.ExecuteScalarAsync())==0,"all exact snapshot-reader pools are closed on disposal");
    }
    foreach(ContentTable table in ContentRecordingPolicy.UnseededEditorTables)
    {
        await using(var guardConnection = new MySqlConnection(connectionString))
        {
            await guardConnection.OpenAsync();
            using var exists = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='ep4_data' AND TABLE_NAME=@name",guardConnection);
            exists.Parameters.AddWithValue("@name",table.Table);
            if(Convert.ToInt32(await exists.ExecuteScalarAsync())!=0) throw new Exception("Guard test requires absent synthetic optional table "+table.Table);
        }
        await Exec($"CREATE TABLE ep4_data.`{table.Table}`(id INT);INSERT INTO ep4_data.`{table.Table}` VALUES(1)");
        try {
            try { await ContentDatabaseSnapshot.CaptureAsync(checkout,connectionString,new ContentCaptureOptions(compose));throw new Exception("Unreviewed nonempty content accepted"); }
            catch(InvalidOperationException e) { Check(e.Message.Contains(table.Key)&&e.Message.Contains("no reviewed seed"),"nonempty unseeded editor table is reported: "+table.Key); }
        } finally { await Exec($"DROP TABLE ep4_data.`{table.Table}`"); }
    }
    Check(await CountScratch()==0,"unseeded-editor guard rejects before allocating scratch copies");
    Console.WriteLine($"{checks} targeted database recording verification checks passed.");
    return;
}
await using (var setup = new MySqlConnection(new MySqlConnectionStringBuilder(connectionString) { Database = "" }.ConnectionString))
{
    await setup.OpenAsync();
    foreach (string schema in new[] { "ep4_data", "ep4_db", "ep4_db_auth", "ep4_post" })
        using (var cmd = new MySqlCommand("CREATE DATABASE IF NOT EXISTS `" + schema + "` CHARACTER SET utf8mb4", setup)) await cmd.ExecuteNonQueryAsync();
}
Check(ContentRecordingPolicy.AllowlistedTables.Count == 130, "all 130 explicitly reviewed authored tables are covered");
ContentRecordingPolicy.ValidateCheckout(checkout);
foreach (ContentTable t in ContentRecordingPolicy.AllowlistedTables)
{
    string extra = t.Key switch {
        "ep4_db_auth/t_ct_limit" => ", a_sell INT DEFAULT 98765",
        "ep4_db/t_castle" => ", a_last_war_time INT DEFAULT 98765,a_state INT DEFAULT 98765,a_next_war_time INT DEFAULT 98765,a_owner_guild_index INT DEFAULT 98765,a_owner_guild_name TEXT,a_owner_char_index INT DEFAULT 98765,a_owner_char_name TEXT,a_tax_guild_index INT DEFAULT 98765,a_tax_item INT DEFAULT 98765,a_tax_produce INT DEFAULT 98765",
        "ep4_db/t_syndicate_jewel_point" => ",a_jewel_point INT DEFAULT 98765,a_user_count INT DEFAULT 98765,a_king_charIndex INT DEFAULT 98765",
        "ep4_db/t_lcball" => ",a_item_max INT DEFAULT 99,a_item_remain INT DEFAULT 42",
        "ep4_db/t_cashitemdate" => ",a_mempos DATETIME,a_charslot0 DATETIME,a_charslot1 DATETIME,a_stashext DATETIME,a_mempos_new INT DEFAULT 98765,a_charslot0_new INT DEFAULT 98765,a_charslot1_new INT DEFAULT 98765,a_stashext_new INT DEFAULT 98765",
        _ => ""
    };
    string engine = t.Table == "t_achievement" ? "MyISAM" : t.Table == "t_achievement_stage" ? "Aria" : "InnoDB";
    await Exec($"DROP TABLE IF EXISTS `{t.CanonicalSchema}`.`{t.Table}`; CREATE TABLE `{t.CanonicalSchema}`.`{t.Table}` ({genericColumns}{extra}) ENGINE={engine}; INSERT INTO `{t.CanonicalSchema}`.`{t.Table}` (id,value,payload,amount,empty_value) VALUES(1,'original',X'00FF5C272200E282AC',123456789012.12345678,NULL)");
}
await Exec("CREATE TABLE IF NOT EXISTS ep4_db.t_characters(id INT, secret TEXT); INSERT INTO ep4_db.t_characters VALUES(1,'CHARACTER-SECRET-DO-NOT-EXPORT'); CREATE TABLE IF NOT EXISTS ep4_db.t_alchemy_work_order(id INT, secret TEXT); INSERT INTO ep4_db.t_alchemy_work_order VALUES(1,'BREWING-JOB-SECRET-DO-NOT-EXPORT'); CREATE TABLE IF NOT EXISTS ep4_db.t_alchemy_facility_unlock(id INT,secret TEXT); INSERT INTO ep4_db.t_alchemy_facility_unlock VALUES(1,'BUILT-STATION-SECRET-DO-NOT-EXPORT'); CREATE TABLE IF NOT EXISTS ep4_db_auth.t_users(id INT,password TEXT); INSERT INTO ep4_db_auth.t_users VALUES(1,'CREDENTIAL-SECRET-DO-NOT-EXPORT'); UPDATE ep4_db.t_castle SET a_owner_char_name='CHARACTER-NAME-DO-NOT-EXPORT',a_owner_guild_name='PLAYER-GUILD-DO-NOT-EXPORT'; UPDATE ep4_data.t_item SET latin_text=_latin1 0x80E9FF");
foreach(string table in new[]{"t_transmutation_wallet","t_transmutation_work_order","t_transmutation_receipt","t_transmutation_delivery"}) {
    Check(ContentRecordingPolicy.IsPlayerOwnedProfessionTable(table),"explicit player-table boundary protects "+table);
    await Exec("CREATE TABLE IF NOT EXISTS ep4_db.`"+table+"`(id INT,secret TEXT);INSERT INTO ep4_db.`"+table+"` VALUES(1,'TRANSMUTATION-PLAYER-SECRET-DO-NOT-EXPORT')");
}
Check(await CountScratch() == 0, "fixture starts without recording scratch schemas");
// Identity mismatch must fail before allocating any scratch schemas.
try { await ContentDatabaseSnapshot.CaptureAsync(checkout, connectionString, new ContentCaptureOptions(Path.Combine(Path.GetDirectoryName(compose)!,"identity-mismatch.yml"))); throw new Exception("Identity mismatch accepted"); }
catch (InvalidOperationException e) { Check(e.Message.Contains("different") || e.Message.Contains("export failed"), "wrong Compose database is rejected before copying"); }
Check(await CountScratch() == 0, "identity failure leaves no scratch schemas");

var options = new ContentCaptureOptions(compose);
string rawText = "herb'\\line\n鱼 🐟 ),( ep4_data.t_characters";
await using (var c = new MySqlConnection(connectionString)) { await c.OpenAsync(); using var cmd = new MySqlCommand("UPDATE ep4_data.t_item SET value=@v", c); cmd.Parameters.AddWithValue("@v", rawText); await cmd.ExecuteNonQueryAsync(); }
var progress = new CallbackProgress(message => {
    if (message.Contains("Capturing saved content 1/"))
        Exec("START TRANSACTION; UPDATE ep4_data.t_item SET amount=2; UPDATE ep4_data.t_crafting_recipe SET amount=2; COMMIT").GetAwaiter().GetResult();
});
string staged;
await using (var snapshot = await ContentDatabaseSnapshot.CaptureAsync(checkout, connectionString, options, progress: progress))
{
    staged = snapshot.StagingDirectory;
    using var item = snapshot.Query("utf8mb4", "SELECT value,payload,amount,empty_value FROM `ep4_data`.`t_item`");
    Check((string)item.Rows[0]["value"] == rawText, "Unicode, quotes and slash bytes survive streaming capture");
    Check(Convert.ToHexString((byte[])item.Rows[0]["payload"]) == "00FF5C272200E282AC", "binary bytes survive capture exactly");
    using var legacyText = snapshot.Query("utf8mb4", "SELECT HEX(latin_text),CAST(zero_date AS CHAR) FROM ep4_data.t_item");
    Check((string)legacyText.Rows[0][0] == "80E9FF", "legacy latin1 column retains exact stored bytes");
    Check((string)legacyText.Rows[0][1] == "0000-00-00 00:00:00", "legacy zero date is preserved without substitution");
    Check((decimal)item.Rows[0]["amount"] == 123456789012.12345678m, "concurrent edit after snapshot does not alter captured InnoDB value");
    using var other = snapshot.Query("utf8mb4", "SELECT amount FROM ep4_data.t_crafting_recipe");
    Check((decimal)other.Rows[0][0] == (decimal)item.Rows[0]["amount"], "cross-table transaction is captured coherently");
    using var unqualified = snapshot.Query("utf8", "SELECT 'ep4_data.t_characters' AS literal FROM t_item");
    Check((string)unqualified.Rows[0][0] == "ep4_data.t_characters", "SQL rewriting preserves literals and unqualified snapshot reads");
    try { snapshot.Query("utf8", "SELECT * FROM ep4_db.t_characters"); throw new Exception("Player data read accepted"); } catch (MySqlException) { Check(true,"player table cannot be read through immutable query connection"); }
    try { snapshot.Query("utf8", "SELECT 1; DELETE FROM t_item"); throw new Exception("Write accepted"); } catch (InvalidOperationException) { Check(true,"query rejects extra statements"); }
    try { snapshot.Query("utf8", "UPDATE t_item SET amount=3"); throw new Exception("Write accepted"); } catch (InvalidOperationException) { Check(true,"query rejects mutation"); }
    var timer = Stopwatch.StartNew();
    IReadOnlyDictionary<string,string> exports = await snapshot.DumpSeedsAsync();
    Console.WriteLine($"TIMING batch canonical 130-table export: {timer.Elapsed.TotalSeconds:F2}s, {exports.Values.Sum(p=>new FileInfo(p).Length)} bytes");
    Check(exports.Count == 130, "canonical exporter produces every approved seed");
    Check(new[]{"t_transmutation_settings","t_transmutation_facility","t_transmutation_facility_ingredient","t_transmutation_offer","t_transmutation_item_rule"}.All(table=>exports.ContainsKey("x64-server/db/seeds/ep4_data/"+table+".sql")),"all five authored Crucible tables are exported");
    foreach(string table in new[]{"t_transmutation_wallet","t_transmutation_work_order","t_transmutation_receipt","t_transmutation_delivery"}) {try{snapshot.Query("utf8","SELECT * FROM ep4_db."+table);throw new Exception("Player data accepted");}catch(MySqlException){Check(true,"restricted reader cannot access "+table);}}
    Check(exports.ContainsKey("x64-server/db/seeds/ep4_data/t_crafting_master_rank_perk.sql"), "canonical export includes authored Artisan perk assignments without player rank state");
    string all = string.Join("\n",exports.Values.Select(File.ReadAllText));
    Check(!all.Contains("TRANSMUTATION-PLAYER-SECRET")&&!all.Contains("BUILT-STATION-SECRET") && !all.Contains("BREWING-JOB-SECRET") && !all.Contains("CHARACTER-SECRET") && !all.Contains("CREDENTIAL-SECRET") && !all.Contains("CHARACTER-NAME") && !all.Contains("PLAYER-GUILD"), "no player credentials, character rows or mixed-table player names enter seeds");
    Check(!all.Contains("98765"), "all five mixed-table runtime fields are normalized");
    Check(File.ReadAllText(exports["x64-server/db/seeds/ep4_db/t_lcball.sql"]).Contains(",99,99)"), "live redemption counter is reset to authored maximum");
    Check(!all.Contains("USE `lc_record_"), "seed headers retain canonical schema names");
    var hashes = exports.ToDictionary(p=>p.Key,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Value))));
    await snapshot.DumpSeedsAsync();
    Check(exports.All(p=>hashes[p.Key] == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Value)))), "repeated export of immutable snapshot is byte-identical");
}
Check(await CountScratch() == 0 && !Directory.Exists(staged), "successful disposal removes all owned scratch schemas and temporary output");
await Exec("DELETE FROM ep4_data.t_crafting_recipe");
await using (var empty = await ContentDatabaseSnapshot.CaptureAsync(checkout, connectionString, options))
{
    var exports = await empty.DumpSeedsAsync();
    string sql = File.ReadAllText(exports["x64-server/db/seeds/ep4_data/t_crafting_recipe.sql"]);
    Check(sql.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length == 4 && sql.Contains("TRUNCATE TABLE `t_crafting_recipe`;"), "deletion of final content row records canonical explicit empty seed");
}
using (var cancellation = new CancellationTokenSource())
{
    var cancelProgress = new CallbackProgress(message => { if (message.Contains("Capturing saved content 1/")) cancellation.Cancel(); });
    try { await ContentDatabaseSnapshot.CaptureAsync(checkout, connectionString, options,cancellation.Token,cancelProgress); throw new Exception("Canceled capture succeeded"); } catch(OperationCanceledException) { Check(true,"cancellation aborts capture"); }
}
Check(await CountScratch() == 0, "canceled capture cleans owned schemas");

string prepared = Path.Combine(checkout,".local","validation","content-recording","database","prepared-game-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(prepared,"x64-server","db"));
Directory.CreateDirectory(Path.Combine(prepared,"x64-server","compose"));
File.Copy(Path.Combine(checkout,"x64-server","db","export-seed.sh"),Path.Combine(prepared,"x64-server","db","export-seed.sh"));
File.Copy(compose,Path.Combine(prepared,"x64-server","compose","docker-compose.yml"));
await using(var preparedSnapshot = await ContentDatabaseSnapshot.CaptureAsync(checkout,connectionString,options))
{
    var exports = await preparedSnapshot.DumpSeedsAsync();
    foreach(var pair in exports) { string target=Path.Combine(prepared,pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(pair.Value,target); }
}
string unreviewed = Path.Combine(prepared,"x64-server","db","seeds","ep4_db","t_characters.sql");
File.WriteAllText(unreviewed,"player table must never become captureable");
try { ContentRecordingPolicy.ValidateCheckout(prepared); throw new Exception("Unreviewed table accepted"); }
catch(InvalidOperationException e) { Check(e.Message.Contains("unreviewed"),"unknown/new player seed cannot silently expand the recording allowlist"); }
File.Delete(unreviewed);
string preparedScript = Path.Combine(prepared,"x64-server","db","export-seed.sh");
string goodScript = File.ReadAllText(preparedScript);
File.WriteAllText(preparedScript,"#!/bin/bash\nprintf 'old-exporter\\n'\n");
try { await ContentDatabaseSnapshot.CaptureAsync(prepared,connectionString); throw new Exception("Old exporter accepted"); }
catch(InvalidOperationException e) { Check(e.Message.Contains("Update the selected game checkout"),"old checkout without immutable exporter capability fails before capture"); }
finally { File.WriteAllText(preparedScript,goodScript); }
Check(await CountScratch() == 0,"capability/policy failures leave no scratch schemas");
Console.WriteLine("PREPARED_CHECKOUT="+prepared);
Console.WriteLine($"{checks} database recording verification checks passed.");

sealed class CallbackProgress(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }
