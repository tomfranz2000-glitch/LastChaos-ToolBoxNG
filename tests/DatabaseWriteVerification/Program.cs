using LastChaos_ToolBoxNG;
using MySqlConnector;
using System.Diagnostics;
using System.Text.Json;

// Fixed disposable endpoint and isolated database: never connect to the live 3306 stack.
const string Endpoint = "Server=127.0.0.1;Port=33316;User ID=root;Password=;Connection Timeout=4;Default Command Timeout=10;Pooling=true;";
const string TestDatabase = "toolbox_write_tests";
List<object> results = [];
int failures = 0;
using (var admin = new MySqlConnection(Endpoint))
{
    admin.Open();
    Exec(admin, $"CREATE DATABASE IF NOT EXISTS `{TestDatabase}` CHARACTER SET utf8mb4;");
}
using var db = new MySqlConnection(Endpoint + "Database=" + TestDatabase);
db.Open();
if (db.Database != TestDatabase || new MySqlConnectionStringBuilder(db.ConnectionString).Port != 33316) throw new Exception("Wrong verification target.");

void Run(string name, Action test)
{
    if (args.Length != 0 && !name.Contains(args[0], StringComparison.OrdinalIgnoreCase)) return;
    try { Reset(); test(); results.Add(new { name, passed = true }); Console.WriteLine("PASS: " + name); }
    catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL: " + name + ": " + e.Message); }
}
void Check(bool valid, string reason) { if (!valid) throw new Exception(reason); }
void Reject(string sql, string? expected = null)
{
    Exception? failure = null;
    try { DatabaseWriteBatch.Execute(db, sql); } catch (Exception e) { failure = e; }
    Check(failure != null, "The batch should have failed.");
    if (expected != null) Check(failure!.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), "Unexpected failure: " + failure!.Message);
}
void OriginalRows() => Check(Text(db, "SELECT GROUP_CONCAT(CONCAT(id,':',name) ORDER BY id SEPARATOR '|') FROM items") == "1:first|2:second", "Persistent records changed after rejected/failed batch.");
void Reset()
{
    Exec(db, "DROP TRIGGER IF EXISTS items_audit; DROP VIEW IF EXISTS item_view; DROP TABLE IF EXISTS items, old_engine, `semi;table`; DROP TEMPORARY TABLE IF EXISTS Temp_Test;");
    Exec(db, "SET SESSION sql_mode='STRICT_TRANS_TABLES,NO_ENGINE_SUBSTITUTION'; CREATE TABLE items (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, name VARCHAR(100) NOT NULL UNIQUE, cost SMALLINT NOT NULL DEFAULT 0) ENGINE=InnoDB; CREATE TABLE old_engine (id INT PRIMARY KEY, name VARCHAR(100)) ENGINE=MyISAM; CREATE TABLE `semi;table` (id INT PRIMARY KEY, name VARCHAR(100)) ENGINE=InnoDB;");
    Exec(db, "INSERT INTO items (id,name) VALUES (1,'first'),(2,'second'); INSERT INTO old_engine VALUES (1,'original');");
}

Run("successful batch commits every statement", () =>
{
    DatabaseWriteBatch.Execute(db, "START TRANSACTION; UPDATE items SET name='changed' WHERE id=1; INSERT INTO items (id,name) VALUES (3,'third'); COMMIT;");
    Check(Text(db, "SELECT GROUP_CONCAT(name ORDER BY id) FROM items") == "changed,second,third", "Batch did not commit completely.");
});
Run("duplicate key following delete rolls entire save back", () =>
{
    Reject("START TRANSACTION; DELETE FROM items WHERE id=1; INSERT INTO items (id,name) VALUES (3,'second'); COMMIT;", "Duplicate");
    OriginalRows();
});
Run("missing table is resolved before any persistent write", () =>
{
    Reject("DELETE FROM items WHERE id=1; INSERT INTO absent_table VALUES(3);", "doesn't exist");
    OriginalRows();
});
Run("MyISAM target refused before any persistent write", () =>
{
    Reject("DELETE FROM items WHERE id=1; UPDATE old_engine SET name='changed' WHERE id=1;", "not transactional");
    OriginalRows();
    Check(Text(db, "SELECT name FROM old_engine WHERE id=1") == "original", "MyISAM write escaped preflight.");
});
Run("quoted semicolons and escaped text are kept as data", () =>
{
    string value = "O'Brien; COMMIT; \\ river \"name\" café";
    string sql = "/* ordinary comment; */ UPDATE `items` SET name='" + MySqlHelper.EscapeString(value) + "' WHERE id=1; -- ignored ;\n UPDATE items SET name='it''s;second' WHERE id=2;";
    Check(DatabaseWriteBatch.Split(sql).Count == 2, "Literal text split into commands.");
    DatabaseWriteBatch.Execute(db, sql);
    Check(Text(db, "SELECT name FROM items WHERE id=1") == value, "Escaped text changed.");
    Check(Text(db, "SELECT name FROM items WHERE id=2") == "it's;second", "Doubled quote changed.");
});
Run("double-quoted values and semicolons in identifiers", () =>
{
    DatabaseWriteBatch.Execute(db, "INSERT INTO `semi;table` VALUES (1,\"kept;whole\"); UPDATE items SET name=\"semi;colon\" WHERE id=1;");
    Check(Text(db, "SELECT name FROM `semi;table`") == "kept;whole", "Quoted identifier or string split incorrectly.");
});
foreach (string forbidden in new[] { "COMMIT", "START TRANSACTION", "BEGIN", "TRUNCATE TABLE items", "ALTER TABLE items ADD COLUMN extra INT", "CREATE TABLE bad (id INT)", "DROP TABLE items", "SET autocommit=1", "LOCK TABLES items WRITE", "/*!40101 COMMIT */", "/*M! COMMIT */" })
{
    string captured = forbidden;
    Run("reject internal/implicit commit: " + captured, () =>
    {
        Reject("START TRANSACTION; DELETE FROM items WHERE id=1; " + captured + "; INSERT INTO items (id,name) VALUES (3,'third'); COMMIT;");
        OriginalRows();
    });
}
Run("unclosed literals and block comments rejected", () =>
{
    Reject("DELETE FROM items WHERE id=1; UPDATE items SET name='unterminated;", "Unterminated");
    Reject("DELETE FROM items WHERE id=1; /* unterminated", "Unterminated");
    OriginalRows();
});
Run("strict mode rejects out-of-range SMALLINT and rolls back", () =>
{
    Exec(db, "SET SESSION sql_mode='';");
    Reject("DELETE FROM items WHERE id=1; UPDATE items SET cost=32768 WHERE id=2;", "Out of range");
    OriginalRows();
    Check(Text(db, "SELECT cost FROM items WHERE id=2") == "0", "Price was silently clipped.");
    Check(Text(db, "SELECT @@SESSION.sql_mode").Contains("STRICT_ALL_TABLES"), "Strict session validation missing.");
});
foreach (string mode in new[] { "ANSI_QUOTES", "NO_BACKSLASH_ESCAPES" })
{
    string captured = mode;
    Run("refuse incompatible SQL mode " + captured, () =>
    {
        Exec(db, "SET SESSION sql_mode='" + captured + "'");
        Reject("DELETE FROM items WHERE id=1", "requires standard");
        OriginalRows();
    });
}
Run("view write refused", () =>
{
    Exec(db, "CREATE VIEW item_view AS SELECT * FROM items;");
    Reject("DELETE FROM item_view WHERE id=1;", "not transactional");
    OriginalRows();
});
Run("custom trigger target refused before trigger can change MyISAM", () =>
{
    Exec(db, "CREATE TRIGGER items_audit AFTER UPDATE ON items FOR EACH ROW UPDATE old_engine SET name=NEW.name WHERE id=1;");
    Reject("UPDATE items SET name='triggered' WHERE id=1", "custom triggers");
    OriginalRows();
    Check(Text(db, "SELECT name FROM old_engine") == "original", "Trigger wrote a nontransactional row.");
});
Run("successful temporary-table batch supports retry on same connection", () =>
{
    string sql = "START TRANSACTION; CREATE TEMPORARY TABLE Temp_Test (id INT) ENGINE=InnoDB; INSERT INTO Temp_Test VALUES(1); UPDATE items SET cost=cost+1 WHERE id IN (SELECT id FROM Temp_Test); DROP TEMPORARY TABLE Temp_Test; COMMIT;";
    DatabaseWriteBatch.Execute(db, sql);
    DatabaseWriteBatch.Execute(db, sql);
    Check(Text(db, "SELECT cost FROM items WHERE id=1") == "2", "Temporary table retry failed.");
});
Run("failed temporary-table save cleans scratch and retry succeeds", () =>
{
    string bad = "START TRANSACTION; CREATE TEMPORARY TABLE Temp_Test (id INT) ENGINE=InnoDB; INSERT INTO Temp_Test VALUES(1); DELETE FROM items WHERE id IN (SELECT id FROM Temp_Test); INSERT INTO items (id,name) VALUES (3,'second'); DROP TEMPORARY TABLE Temp_Test; COMMIT;";
    Reject(bad, "Duplicate");
    OriginalRows();
    Exception? remaining = null;
    try { Text(db, "SELECT COUNT(*) FROM Temp_Test"); } catch (MySqlException e) { remaining = e; }
    Check(remaining != null, "Failed transaction leaked Temp_Test on the same connection.");
    string retry = "CREATE TEMPORARY TABLE Temp_Test (id INT) ENGINE=InnoDB; INSERT INTO Temp_Test VALUES(1); UPDATE items SET cost=9 WHERE id IN (SELECT id FROM Temp_Test); DROP TEMPORARY TABLE Temp_Test;";
    DatabaseWriteBatch.Execute(db, retry);
    Check(Text(db, "SELECT cost FROM items WHERE id=1") == "9", "Retry after failed save did not commit.");
});
Run("last generated ID survives later non-insert statements", () =>
{
    long id = DatabaseWriteBatch.Execute(db, "INSERT INTO items (name) VALUES ('auto'); UPDATE items SET cost=5 WHERE name='auto';");
    Check(id > 2 && Text(db, "SELECT name FROM items WHERE id=" + id) == "auto", "LastInsertedId was lost.");
});
Run("connection loss during pending save rolls prior writes back", () =>
{
    using var blocker = new MySqlConnection(Endpoint + "Database=" + TestDatabase);
    using var writer = new MySqlConnection(Endpoint + "Database=" + TestDatabase);
    blocker.Open(); writer.Open();
    using var block = blocker.BeginTransaction();
    using (var hold = new MySqlCommand("UPDATE items SET cost=cost WHERE id=2", blocker, block)) hold.ExecuteNonQuery();
    long connectionId = writer.ServerThread;
    var pending = Task.Run(() =>
    {
        try { DatabaseWriteBatch.Execute(writer, "DELETE FROM items WHERE id=1; UPDATE items SET cost=6 WHERE id=2;"); return (Exception?)null; }
        catch (Exception e) { return e; }
    });
    bool waiting = false;
    Stopwatch time = Stopwatch.StartNew();
    while (time.ElapsedMilliseconds < 5000)
    {
        if (Text(db, "SELECT COALESCE(INFO,'') FROM information_schema.PROCESSLIST WHERE ID=" + connectionId).Contains("UPDATE items SET cost=6 WHERE id=2", StringComparison.Ordinal)) { waiting = true; break; }
        Thread.Sleep(50);
    }
    if (!waiting)
    {
        string state = Text(db, "SELECT CONCAT(COALESCE(STATE,''),' / ',COALESCE(INFO,'')) FROM information_schema.PROCESSLIST WHERE ID=" + connectionId);
        string taskState = pending.IsCompleted ? pending.GetAwaiter().GetResult()?.ToString() ?? "completed successfully" : pending.Status.ToString();
        block.Rollback();
        pending.GetAwaiter().GetResult();
        throw new Exception("Writer did not reach the controlled lock wait: " + state + " ; " + taskState);
    }
    Exec(db, "KILL " + connectionId);
    Check(pending.GetAwaiter().GetResult() != null, "Disconnected save unexpectedly succeeded.");
    block.Rollback();
    OriginalRows();
    Check(Text(db, "SELECT cost FROM items WHERE id=2") == "0", "Disconnected write persisted.");
});

string report = JsonSerializer.Serialize(new { database = TestDatabase, port = 33316, tests = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "verification.json"), report);
Console.WriteLine($"{results.Count - failures}/{results.Count} integration cases passed; report: {Path.Combine(AppContext.BaseDirectory, "verification.json")}");
return failures == 0 ? 0 : 1;

static void Exec(MySqlConnection db, string sql) { using var command = new MySqlCommand(sql, db); command.ExecuteNonQuery(); }
static string Text(MySqlConnection db, string sql) { using var command = new MySqlCommand(sql, db); return Convert.ToString(command.ExecuteScalar()) ?? ""; }
