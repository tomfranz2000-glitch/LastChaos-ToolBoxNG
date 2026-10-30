using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace LastChaos_ToolBoxNG.ContentRecording;

public sealed record ContentCaptureOptions(string? ComposeFile = null);

/// <summary>A private, immutable copy of the reviewed authored tables. Both SQL
/// seeds and client binaries must read this instance before it is disposed.</summary>
public sealed class ContentDatabaseSnapshot : IDisposable, IAsyncDisposable
{
    private readonly string checkout, compose, rootConnectionString, prefix;
    private readonly List<string> ownedSchemas = [];
    private readonly HashSet<string> readerPools = new(StringComparer.Ordinal);
    private readonly string readerUser = "lc_record_" + Guid.NewGuid().ToString("N")[..20];
    private readonly string readerPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private bool readerCreated, disposed;
    public string DatabaseIdentity { get; private set; } = "";
    public string? CleanupWarning { get; private set; }
    public string StagingDirectory { get; }
    private string SeedDirectory => Path.Combine(StagingDirectory, "seeds");
    private ContentDatabaseSnapshot(string checkout, string connectionString, ContentCaptureOptions options)
    {
        this.checkout = Path.GetFullPath(checkout);
        compose = Path.GetFullPath(options.ComposeFile ?? Path.Combine(checkout, "x64-server", "compose", "docker-compose.yml"));
        var settings = new MySqlConnectionStringBuilder(connectionString);
        if (settings.Database != "ep4_data" || settings.UserID != "root" ||
            !(settings.Server.Equals("localhost", StringComparison.OrdinalIgnoreCase) || settings.Server == "127.0.0.1" || settings.Server == "::1"))
            throw new InvalidOperationException("Content recording currently supports the local Docker database using root and ep4_data. Remote databases and other database users require a separately supported exporter.");
        settings.AllowLoadLocalInfile = true;
        settings.AllowZeroDateTime = true;
        settings.Pooling = false;
        settings.DefaultCommandTimeout = 60;
        rootConnectionString = settings.ConnectionString;
        prefix = "lc_record_" + Guid.NewGuid().ToString("N") + "_";
        StagingDirectory = Path.Combine(Path.GetTempPath(), "lc-content-record-" + Guid.NewGuid().ToString("N"));
    }

    public static Task<ContentDatabaseSnapshot> CaptureAsync(string checkout, string connectionString,
        CancellationToken token = default, IProgress<string>? progress = null) =>
        CaptureAsync(checkout, connectionString, new ContentCaptureOptions(), token, progress);

    public static async Task<ContentDatabaseSnapshot> CaptureAsync(string checkout, string connectionString,
        ContentCaptureOptions options, CancellationToken token = default, IProgress<string>? progress = null)
    {
        ContentRecordingPolicy.ValidateCheckout(checkout);
        var snapshot = new ContentDatabaseSnapshot(checkout, connectionString, options);
        try { await snapshot.CaptureCoreAsync(token, progress); return snapshot; }
        catch (Exception error)
        {
            await snapshot.DisposeAsync();
            if (snapshot.CleanupWarning is { } warning) error.Data["ContentSnapshotCleanupWarning"] = warning;
            throw;
        }
    }

    private async Task CaptureCoreAsync(CancellationToken token, IProgress<string>? progress)
    {
        if (!File.Exists(compose) || !File.Exists(Path.Combine(checkout, "x64-server", "db", "export-seed.sh")))
            throw new InvalidOperationException("The selected checkout must contain the game seed exporter and Docker Compose file.");
        var capabilityCheck = ProcessInfo(BashPath());
        capabilityCheck.ArgumentList.Add(Path.Combine(checkout, "x64-server", "db", "export-seed.sh").Replace('\\', '/'));
        capabilityCheck.ArgumentList.Add("--capabilities");
        try
        {
            string capabilities = await Run(capabilityCheck, token);
            if (capabilities.Trim() != "lc-seed-export-v1 immutable-source-prefix explicit-empty-snapshot")
                throw new InvalidOperationException("Unsupported seed exporter.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new InvalidOperationException("Update the selected game checkout: its seed exporter does not support immutable content recording.", error); }
        progress?.Report("Checking that the Toolbox and game checkout use the same database…");
        await using var source = new MySqlConnection(rootConnectionString);
        await source.OpenAsync(token);
        DatabaseIdentity = Convert.ToString(await Scalar(source, "SELECT CONCAT(@@hostname, ':', @@server_id, ':', DATABASE())", token))!;
        var verify = ProcessInfo("docker");
        foreach (string arg in new[] { "compose", "-f", compose, "exec", "-T", "-e", "MYSQL_PWD", "mariadb", "mariadb", "--protocol=socket", "-uroot", "--batch", "--skip-column-names", "ep4_data", "-e", "SELECT CONCAT(@@hostname, ':', @@server_id, ':', DATABASE())" }) verify.ArgumentList.Add(arg);
        verify.Environment["MYSQL_PWD"] = new MySqlConnectionStringBuilder(rootConnectionString).Password;
        if ((await Run(verify, token)).Trim() != DatabaseIdentity)
            throw new InvalidOperationException("The selected checkout's Docker database is different from the database edited by this Toolbox. Nothing was recorded.");

        foreach (ContentTable table in ContentRecordingPolicy.UnseededEditorTables)
        {
            bool exists = Convert.ToInt32(await Scalar(source, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA='" + table.CanonicalSchema + "' AND TABLE_NAME='" + table.Table + "'", token)) != 0;
            if (exists && Convert.ToInt32(await Scalar(source, "SELECT EXISTS(SELECT 1 FROM " + Live(table) + " LIMIT 1)", token)) != 0)
                throw new InvalidOperationException("Saved content exists in " + table.Key + ", which has no reviewed seed or recording policy. Classify and seed this editor table, then update the Toolbox before recording. No files were changed.");
        }

        // No staging DDL occurs until both endpoints have passed the identity check.
        await using var writer = new MySqlConnection(rootConnectionString);
        await writer.OpenAsync(token);
        await Execute(writer, "SET SESSION sql_mode='NO_AUTO_VALUE_ON_ZERO'; SET SESSION lock_wait_timeout=10", token);
        var metadata = new Dictionary<ContentTable, TableMetadata>();
        foreach (ContentTable table in ContentRecordingPolicy.AllowlistedTables)
        {
            string engine = Convert.ToString(await Scalar(source, "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA='" + table.CanonicalSchema + "' AND TABLE_NAME='" + table.Table + "' AND TABLE_TYPE='BASE TABLE'", token)) ?? "";
            if (engine != "InnoDB" && engine != "MyISAM" && engine != "Aria")
                throw new InvalidOperationException("Cannot safely capture " + table.Key + ": missing table or unsupported storage engine " + engine + ".");
            string definition = await Definition(source, table, token);
            var columns = new List<string>();
            using (var cmd = new MySqlCommand("SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='" + table.CanonicalSchema + "' AND TABLE_NAME='" + table.Table + "' AND EXTRA NOT LIKE '%GENERATED%' ORDER BY ORDINAL_POSITION", source))
            await using (var r = await cmd.ExecuteReaderAsync(token))
                while (await r.ReadAsync(token)) columns.Add(r.GetString(0));
            metadata.Add(table, new TableMetadata(engine, definition, columns));
        }
        foreach (string schema in ContentRecordingPolicy.AllowlistedTables.Select(t => t.CanonicalSchema).Distinct())
        {
            string scratch = prefix + schema;
            if (Convert.ToInt32(await Scalar(writer, "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='" + scratch + "'", token)) != 0)
                throw new InvalidOperationException("The randomly generated staging schema already exists. Retry with a fresh snapshot.");
            // Track before sending CREATE: cancellation/network failure can be
            // reported after the server has successfully created the schema.
            ownedSchemas.Add(scratch);
            try { await Execute(writer, "CREATE DATABASE " + Q(scratch), token); }
            catch (MySqlException e) when (e.Number == 1007) { ownedSchemas.Remove(scratch); throw; }
        }
        foreach (ContentTable table in ContentRecordingPolicy.AllowlistedTables)
            await Execute(writer, "CREATE TABLE " + Scratch(table) + " LIKE " + Live(table), token);

        // Only legacy authored tables are locked. Player tables and InnoDB content
        // remain writable. A separate lock connection avoids LOCK TABLES committing
        // the cross-schema InnoDB consistent snapshot.
        await using var legacyLocks = new MySqlConnection(rootConnectionString);
        await legacyLocks.OpenAsync(token);
        ContentTable[] legacy = metadata.Where(pair => pair.Value.Engine != "InnoDB").Select(pair => pair.Key).ToArray();
        await Execute(legacyLocks, "SET SESSION lock_wait_timeout=10", token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        CancellationToken captureToken = deadline.Token;
        bool locked = false, transactionStarted = false;
        try
        {
            if (legacy.Length > 0)
            {
                await Execute(legacyLocks, "LOCK TABLES " + string.Join(",", legacy.Select(t => Live(t) + " READ")), captureToken);
                locked = true;
            }
            await Execute(source, "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ; START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY", captureToken);
            transactionStarted = true;
            // Legacy tables are copied first so the targeted read locks are brief.
            int index = 0;
            foreach (ContentTable table in legacy.Concat(metadata.Keys.Where(t => metadata[t].Engine == "InnoDB")))
            {
                progress?.Report("Capturing saved content " + ++index + "/" + metadata.Count + ": " + table.Key);
                if (await Definition(source, table, captureToken) != metadata[table].Definition)
                    throw new InvalidOperationException("The structure of " + table.Key + " changed during capture. Retry after schema maintenance finishes.");
                string columns = string.Join(",", metadata[table].Columns.Select(Q));
                using var select = new MySqlCommand("SELECT " + columns + " FROM " + Live(table), source);
                await using (var data = await select.ExecuteReaderAsync(CommandBehavior.SequentialAccess, captureToken))
                {
                    var copy = new MySqlBulkCopy(writer) { DestinationTableName = Scratch(table), BulkCopyTimeout = 60 };
                    for (int c = 0; c < metadata[table].Columns.Count; c++) copy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(c, metadata[table].Columns[c]));
                    MySqlBulkCopyResult result = await copy.WriteToServerAsync(data, captureToken);
                    if (result.Warnings.Count != 0) throw new InvalidOperationException("Database conversion warning while copying " + table.Key + ": " + string.Join("; ", result.Warnings.Select(w => w.Message)));
                }
                if (locked && index == legacy.Length)
                {
                    await Execute(legacyLocks, "UNLOCK TABLES", captureToken);
                    locked = false;
                }
            }
            await Execute(source, "COMMIT", captureToken);
            transactionStarted = false;
        }
        finally
        {
            if (transactionStarted) await Execute(source, "ROLLBACK", CancellationToken.None);
            if (locked) await Execute(legacyLocks, "UNLOCK TABLES", CancellationToken.None);
        }
        // Client serializers use a SELECT-only user with no grants on any live
        // schema. SQL rewriting alone is not a security or immutability boundary.
        using (var create = new MySqlCommand("CREATE USER '" + readerUser + "'@'%' IDENTIFIED BY @password", writer))
        {
            if (Convert.ToInt32(await Scalar(writer, "SELECT COUNT(*) FROM mysql.user WHERE User='" + readerUser + "' AND Host='%'", token)) != 0)
                throw new InvalidOperationException("The randomly generated snapshot reader already exists. Retry with a fresh snapshot.");
            create.Parameters.AddWithValue("@password", readerPassword);
            readerCreated = true;
            try { await create.ExecuteNonQueryAsync(token); }
            catch (MySqlException e) when (e.Number == 1396) { readerCreated = false; throw; }
        }
        foreach (string schema in ownedSchemas)
            await Execute(writer, "GRANT SELECT ON " + Q(schema) + ".* TO '" + readerUser + "'@'%'", token);
        Directory.CreateDirectory(SeedDirectory);
        foreach (ContentTable table in ContentRecordingPolicy.AllowlistedTables)
        {
            string target = Path.Combine(SeedDirectory, table.CanonicalSchema, table.Table + ".sql");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // The canonical exporter needs reviewed existing target paths, not
            // another copy of the old multi-megabyte payload it will replace.
            File.WriteAllText(target, "");
        }
        progress?.Report("Saved content snapshot is ready.");
    }

    public DataTable Query(string charset, string sql)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Regex.IsMatch(charset, "^[A-Za-z0-9_]+$")) throw new ArgumentException("Invalid database character set.", nameof(charset));
        string query = RewriteQuery(sql);
        var settings = new MySqlConnectionStringBuilder(rootConnectionString) {
            UserID = readerUser, Password = readerPassword, Database = prefix + "ep4_data", CharacterSet = charset, AllowLoadLocalInfile = false,
            Pooling = true, MaximumPoolSize = 4, ConnectionIdleTimeout = 5
        };
        lock (readerPools) readerPools.Add(settings.ConnectionString);
        using var connection = new MySqlConnection(settings.ConnectionString);
        connection.Open();
        using var command = new MySqlCommand(query, connection);
        using var adapter = new MySqlDataAdapter(command);
        var result = new DataTable(); adapter.Fill(result); return result;
    }

    // Preserve literals and comments byte-for-byte; only canonical schema tokens
    // followed by '.' are rewritten, including backtick-quoted identifiers.
    private string RewriteQuery(string sql)
    {
        var output = new StringBuilder();
        var structure = new StringBuilder();
        for (int i = 0; i < sql.Length;)
        {
            int start = i; char ch = sql[i++];
            if (ch is '\'' or '"')
            {
                bool closed = false;
                while (i < sql.Length) { if (sql[i] == '\\') { i += 2; continue; } if (sql[i++] == ch) { if (i < sql.Length && sql[i] == ch) { i++; continue; } closed = true; break; } }
                if (!closed || i > sql.Length) throw new InvalidOperationException("Malformed content query.");
                output.Append(sql[start..i]); structure.Append(" ? "); continue;
            }
            if (ch == '#' || (ch == '-' && i + 1 < sql.Length && sql[i] == '-' && char.IsWhiteSpace(sql[i + 1])))
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                output.Append(sql[start..i]); structure.Append(' '); continue;
            }
            if (ch == '/' && i < sql.Length && sql[i] == '*')
            {
                if (i + 1 < sql.Length && (sql[i + 1] == '!' || sql[i + 1] == 'M')) throw new InvalidOperationException("Executable SQL comments are not supported by content recording.");
                int end = sql.IndexOf("*/", i + 1, StringComparison.Ordinal);
                if (end < 0) throw new InvalidOperationException("Malformed content query comment.");
                i = end + 2; output.Append(sql[start..i]); structure.Append(' '); continue;
            }
            if (ch == '`' || char.IsAsciiLetter(ch) || ch == '_')
            {
                string identifier;
                if (ch == '`')
                {
                    var name = new StringBuilder(); bool closed = false;
                    while (i < sql.Length) { char c = sql[i++]; if (c == '`') { if (i < sql.Length && sql[i] == '`') { i++; name.Append('`'); continue; } closed = true; break; } name.Append(c); }
                    if (!closed) throw new InvalidOperationException("Malformed content identifier.");
                    identifier = name.ToString();
                }
                else { while (i < sql.Length && (char.IsAsciiLetterOrDigit(sql[i]) || sql[i] == '_')) i++; identifier = sql[start..i]; }
                int next = i; while (next < sql.Length && char.IsWhiteSpace(sql[next])) next++;
                bool canonical = ContentRecordingPolicy.AllowlistedTables.Any(t => t.CanonicalSchema.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                output.Append(canonical && next < sql.Length && sql[next] == '.' ? Q(prefix + identifier.ToLowerInvariant()) : sql[start..i]);
                structure.Append(ch == '`' ? " identifier " : identifier); continue;
            }
            output.Append(ch); structure.Append(ch);
        }
        string parsed = structure.ToString().Trim();
        if (!Regex.IsMatch(parsed, @"^\(*\s*SELECT\b", RegexOptions.IgnoreCase) || parsed.TrimEnd(';').Contains(';') || Regex.IsMatch(parsed, @"\b(INTO|FOR\s+UPDATE|LOCK\s+IN\s+SHARE)\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Content serializers may only read the immutable snapshot with a single SELECT query.");
        return output.ToString();
    }

    public IReadOnlyDictionary<string, string> DumpSeeds() => DumpSeedsAsync().GetAwaiter().GetResult();
    public async Task<IReadOnlyDictionary<string, string>> DumpSeedsAsync(CancellationToken token = default, IProgress<string>? progress = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        progress?.Report("Exporting the saved content snapshot to deterministic SQL files…");
        // One container process runs the unchanged canonical formatter locally;
        // spawning Docker for every table/query takes minutes even for tiny DBs.
        string remote = "/tmp/lc-record-" + Guid.NewGuid().ToString("N");
        if (!Regex.IsMatch(remote, "^/tmp/lc-record-[a-f0-9]{32}$"))
            throw new InvalidOperationException("Invalid owned container staging path.");
        bool created = false;
        try
        {
            await Run(Docker("exec", "-T", "mariadb", "mkdir", "--mode=700", "--", remote), token);
            created = true;
            await Run(Docker("cp", Path.Combine(checkout, "x64-server", "db", "export-seed.sh"), "mariadb:" + remote + "/export-seed.sh"), token);
            string layout = Path.Combine(StagingDirectory, "export-layout");
            foreach (ContentTable table in ContentRecordingPolicy.AllowlistedTables)
            {
                string target = Path.Combine(layout, table.CanonicalSchema, table.Table + ".sql");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, "");
            }
            await Run(Docker("cp", layout, "mariadb:" + remote + "/seeds"), token);
            var process = Docker("exec", "-T", "-e", "MARIADB_ROOT_PASSWORD", "-e", "LC_DB_CONN=socket", "-e", "LC_DB_SEEDS_DIR=" + remote + "/seeds", "-e", "LC_DB_SOURCE_PREFIX=" + prefix, "-e", "LC_DB_ALLOW_EMPTY=1", "mariadb", "bash", remote + "/export-seed.sh");
            foreach (ContentTable table in ContentRecordingPolicy.AllowlistedTables) process.ArgumentList.Add(table.Key);
            process.Environment["MARIADB_ROOT_PASSWORD"] = new MySqlConnectionStringBuilder(rootConnectionString).Password;
            int completed = 0;
            await Run(process, token, 600, line => {
                if (line.StartsWith("[export]   wrote", StringComparison.Ordinal))
                    progress?.Report("Exporting saved content " + Interlocked.Increment(ref completed) + "/" + ContentRecordingPolicy.AllowlistedTables.Count + ": " + line[18..].Trim());
            });
            await Run(Docker("cp", "mariadb:" + remote + "/seeds/.", SeedDirectory), token);
        }
        finally
        {
            // This is the exact, generated absolute /tmp child created above.
            // Pass it directly to rm; never interpolate user paths into a shell.
            if (created && Regex.IsMatch(remote, "^/tmp/lc-record-[a-f0-9]{32}$"))
                try { await Run(Docker("exec", "-T", "mariadb", "rm", "-rf", "--", remote), CancellationToken.None); }
                catch (Exception e) { CleanupWarning = "Container staging cleanup needs attention for " + remote + ": " + e.Message; }
        }
        return ContentRecordingPolicy.AllowlistedTables.ToDictionary(t => t.SeedRelativePath, t => Path.Combine(SeedDirectory, t.CanonicalSchema, t.Table + ".sql"), StringComparer.Ordinal);
    }

    private ProcessStartInfo Docker(params string[] arguments)
    {
        var process = ProcessInfo("docker");
        foreach (string arg in new[] { "compose", "-f", compose }.Concat(arguments)) process.ArgumentList.Add(arg);
        return process;
    }

    private ProcessStartInfo ProcessInfo(string executable) => new(executable) { WorkingDirectory = checkout, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    private static string BashPath()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        if (!File.Exists(path)) throw new InvalidOperationException("Install Git for Windows to run the game's canonical seed exporter.");
        return path;
    }
    private static async Task<string> Run(ProcessStartInfo info, CancellationToken token, int timeoutSeconds = 60, Action<string>? onOutput = null)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the database exporter.");
        async Task<string> ReadOutput()
        {
            var content = new StringBuilder();
            while (await process.StandardOutput.ReadLineAsync() is { } line) { content.AppendLine(line); onOutput?.Invoke(line); }
            return content.ToString();
        }
        Task<string> output = ReadOutput(); Task<string> error = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) { } await Task.WhenAll(output, error); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException("Database export failed: " + await error + "\n" + await output);
        return await output;
    }
    private static string Q(string identifier) => "`" + identifier.Replace("`", "``") + "`";
    private static string Live(ContentTable table) => Q(table.CanonicalSchema) + "." + Q(table.Table);
    private string Scratch(ContentTable table) => Q(prefix + table.CanonicalSchema) + "." + Q(table.Table);
    private sealed record TableMetadata(string Engine, string Definition, List<string> Columns);
    private static async Task<string> Definition(MySqlConnection connection, ContentTable table, CancellationToken token)
    {
        using var command = new MySqlCommand("SHOW CREATE TABLE " + Live(table), connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        // AUTO_INCREMENT advances on ordinary writes; it is not a schema change.
        return Regex.Replace(reader.GetString(1), @" AUTO_INCREMENT=\d+", "");
    }
    private static async Task<object?> Scalar(MySqlConnection connection, string sql, CancellationToken token) { using var command = new MySqlCommand(sql, connection); return await command.ExecuteScalarAsync(token); }
    private static async Task Execute(MySqlConnection connection, string sql, CancellationToken token) { using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(token); }

    public void Dispose() => Task.Run(async () => await DisposeAsync()).GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        var cleanupFailures = new List<string>();
        try
        {
            // Clear only this snapshot's credential-specific pools, never the
            // user's Toolbox connection pool or another recording operation.
            foreach (string pool in readerPools)
                try { using var readerConnection = new MySqlConnection(pool); await MySqlConnection.ClearPoolAsync(readerConnection); }
                catch (Exception e) { cleanupFailures.Add(e.Message); }
            if (readerCreated || ownedSchemas.Count != 0)
            {
            await using var connection = new MySqlConnection(rootConnectionString);
            await connection.OpenAsync();
            if (readerCreated)
                try { await Execute(connection, "DROP USER IF EXISTS '" + readerUser + "'@'%'", CancellationToken.None); }
                catch (Exception e) { cleanupFailures.Add(e.Message); }
            foreach (string schema in ownedSchemas)
            {
                if (!schema.StartsWith(prefix, StringComparison.Ordinal) || !Regex.IsMatch(prefix, "^lc_record_[a-f0-9]{32}_$"))
                    throw new InvalidOperationException("Refusing to remove an unowned staging schema.");
                try { await Execute(connection, "DROP DATABASE IF EXISTS " + Q(schema), CancellationToken.None); }
                catch (Exception e) { cleanupFailures.Add(schema + ": " + e.Message); }
            }
            }
        }
        catch (Exception e) { cleanupFailures.Add(e.Message); }
        finally
        {
            string staging = Path.GetFullPath(StagingDirectory);
            string temp = Path.GetFullPath(Path.GetTempPath());
            try { if (Directory.Exists(staging) && staging.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(staging).StartsWith("lc-content-record-", StringComparison.Ordinal)) Directory.Delete(staging, true); }
            catch (Exception e) { cleanupFailures.Add(e.Message); }
            if (cleanupFailures.Count > 0) CleanupWarning = string.Join("; ", new[] { CleanupWarning, "Temporary snapshot cleanup needs attention: " + string.Join("; ", cleanupFailures) }.Where(s => s is not null));
        }
    }
}
