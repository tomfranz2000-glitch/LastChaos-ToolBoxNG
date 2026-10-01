using MySqlConnector;
using System.Text;
using System.Text.RegularExpressions;

namespace LastChaos_ToolBoxNG;

// One editor operation owns one connection and one transaction. Legacy editor
// SQL may contain outer START/COMMIT markers, but cannot commit halfway through.
internal static class DatabaseWriteBatch
{
	private const string Identifier = @"(?:`(?:``|[^`])+`|[A-Za-z_][A-Za-z0-9_]*)";
	private const string QualifiedName = Identifier + @"(?:\s*\.\s*" + Identifier + @")?";
	private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
	internal sealed record Statement(string Sql, string Structure);
	private sealed record Table(string Schema, string Name)
	{
		public string Quoted => Quote(Schema) + "." + Quote(Name);
	}

	internal static long Execute(MySqlConnection connection, string sql)
	{
		List<Statement> statements = Split(sql);
		if (statements.Count > 0 && IsMarker(statements[0], "START TRANSACTION")) statements.RemoveAt(0);
		if (statements.Count > 0 && IsMarker(statements[^1], "COMMIT")) statements.RemoveAt(statements.Count - 1);
		if (statements.Count == 0) return 0;
		HashSet<Table> targets = [];
		HashSet<string> temporary = new(StringComparer.OrdinalIgnoreCase);
		foreach (Statement statement in statements)
		{
			Match create = Regex.Match(statement.Structure, @"^CREATE\s+TEMPORARY\s+TABLE\s+(?<table>" + Identifier + @")\s*\(", Options);
			if (create.Success)
			{
				string name = Unquote(create.Groups["table"].Value);
				if (!name.StartsWith("Temp_", StringComparison.OrdinalIgnoreCase)
					|| !temporary.Add(name)
					|| !Regex.IsMatch(statement.Structure, @"\)\s*ENGINE\s*=\s*InnoDB\s*$", Options))
					throw new InvalidOperationException("Editor scratch tables must be unique Temp_ tables using InnoDB.");
				continue;
			}
			Match drop = Regex.Match(statement.Structure, @"^DROP\s+TEMPORARY\s+TABLE\s+(?<table>" + Identifier + @")\s*$", Options);
			if (drop.Success && temporary.Remove(Unquote(drop.Groups["table"].Value))) continue;
			Match write = Regex.Match(statement.Structure,
				@"^(?:(?:INSERT|REPLACE)\s+INTO\s+(?<table>" + QualifiedName + @")(?=\s|\()"
				+ @"|UPDATE\s+(?<table>" + QualifiedName + @")\s+SET\b"
				+ @"|DELETE\s+FROM\s+(?<table>" + QualifiedName + @")(?=\s+WHERE\b|\s*$))", Options);
			if (!write.Success)
				throw new InvalidOperationException("Unsupported editor write statement. Schema changes and implicit commits are not allowed in a save.");
			Table table = ParseTable(write.Groups["table"].Value, connection.Database);
			if (!temporary.Contains(table.Name) || table.Schema != connection.Database) targets.Add(table);
		}
		if (temporary.Count != 0) throw new InvalidOperationException("Editor scratch tables must be dropped before the save ends.");

		// This setting belongs only to the tool connection. Never weaken global SQL
		// validation to make a legacy editor's invalid value appear to save.
		using (MySqlCommand mode = connection.CreateCommand())
		{
			mode.CommandText = "SELECT @@SESSION.sql_mode";
			string[] modes = Convert.ToString(mode.ExecuteScalar())!.Split(',');
			if (modes.Contains("NO_BACKSLASH_ESCAPES") || modes.Contains("ANSI_QUOTES"))
				throw new InvalidOperationException("This editor's SQL requires standard MariaDB string escaping (no ANSI_QUOTES/NO_BACKSLASH_ESCAPES).");
			mode.CommandText = "SET SESSION sql_mode = @mode";
			mode.Parameters.AddWithValue("@mode", string.Join(',', modes.Append("STRICT_ALL_TABLES").Distinct()));
			mode.ExecuteNonQuery();
		}

		using MySqlTransaction transaction = connection.BeginTransaction();
		List<string> createdScratchTables = [];
		try
		{
			// Resolve every persistent write target before the first modification.
			// The zero-row SELECT holds its metadata lock until transaction end.
			foreach (Table table in targets.OrderBy(t => t.Schema).ThenBy(t => t.Name))
			{
				using MySqlCommand check = new("SELECT 1 FROM " + table.Quoted + " LIMIT 0", connection, transaction);
				using (MySqlDataReader reader = check.ExecuteReader()) { }
				check.CommandText = "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@table AND TABLE_TYPE='BASE TABLE'";
				check.Parameters.AddWithValue("@db", table.Schema);
				check.Parameters.AddWithValue("@table", table.Name);
				if (!string.Equals(Convert.ToString(check.ExecuteScalar()), "InnoDB", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException($"{table.Schema}.{table.Name} is not transactional. Apply the game's 0009-toolbox-transactional-content migration before editing. No changes were written.");
				check.CommandText = "SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE EVENT_OBJECT_SCHEMA=@db AND EVENT_OBJECT_TABLE=@table";
				if (Convert.ToInt64(check.ExecuteScalar()) != 0)
					throw new InvalidOperationException($"{table.Schema}.{table.Name} has custom triggers; their write targets must be reviewed before atomic editing is enabled.");
			}
			long lastInsertedId = 0;
			foreach (Statement statement in statements)
			{
				using MySqlCommand command = new(statement.Sql, connection, transaction);
				command.ExecuteNonQuery();
				Match scratch = Regex.Match(statement.Structure, @"^CREATE\s+TEMPORARY\s+TABLE\s+(?<table>" + Identifier + @")\s*\(", Options);
				if (scratch.Success) createdScratchTables.Add(Unquote(scratch.Groups["table"].Value));
				if (command.LastInsertedId != 0) lastInsertedId = command.LastInsertedId;
			}
			transaction.Commit();
			return lastInsertedId;
		}
		catch
		{
			try { transaction.Rollback(); } catch (MySqlException) { /* Disconnected InnoDB transactions roll back at the server. */ }
			throw;
		}
		finally
		{
			// TEMPORARY table definitions outlive rollback, unlike their rows.
			// Remove them on both failure and success so a retry is independent.
			foreach (string table in createdScratchTables)
			{
				if (connection.State != System.Data.ConnectionState.Open) break;
				try
				{
					using MySqlCommand cleanup = new("DROP TEMPORARY TABLE IF EXISTS " + Quote(table), connection);
					cleanup.ExecuteNonQuery();
				}
				catch (MySqlException) { /* Closing/resetting this connection also removes its scratch tables. */ }
			}
		}
	}

	private static bool IsMarker(Statement statement, string marker) =>
		Regex.Replace(statement.Structure.Trim(), @"\s+", " ").Equals(marker, StringComparison.OrdinalIgnoreCase);
	private static string Quote(string value) => "`" + value.Replace("`", "``") + "`";
	private static string Unquote(string value) => value.StartsWith('`') ? value[1..^1].Replace("``", "`") : value;
	private static Table ParseTable(string text, string database)
	{
		string[] parts = Regex.Matches(text, Identifier).Select(m => Unquote(m.Value)).ToArray();
		return parts.Length == 2 ? new(parts[0], parts[1]) : new(database, parts[0]);
	}

	// Split only at real statement boundaries; semicolons and keywords inside
	// localized text must never become executable statements or table names.
	internal static List<Statement> Split(string sql)
	{
		List<Statement> result = [];
		StringBuilder command = new(), structure = new();
		char quote = '\0';
		bool lineComment = false, blockComment = false;
		void Flush()
		{
			string mask = structure.ToString().Trim();
			if (mask.Length != 0) result.Add(new(command.ToString().Trim(), mask));
			command.Clear(); structure.Clear();
		}
		for (int i = 0; i < sql.Length; i++)
		{
			char ch = sql[i], next = i + 1 < sql.Length ? sql[i + 1] : '\0';
			if (lineComment)
			{
				if (ch == '\n') { lineComment = false; command.Append('\n'); structure.Append('\n'); }
				continue;
			}
			if (blockComment)
			{
				if (ch == '*' && next == '/') { blockComment = false; i++; command.Append(' '); structure.Append(' '); }
				continue;
			}
			if (quote != '\0')
			{
				command.Append(ch); structure.Append(quote == '`' ? ch : ' ');
				if (ch == '\\' && quote != '`' && next != '\0') { command.Append(next); structure.Append(' '); i++; }
				else if (ch == quote && next == quote) { command.Append(next); structure.Append(quote == '`' ? next : ' '); i++; }
				else if (ch == quote) quote = '\0';
				continue;
			}
			if (ch == ';') { Flush(); continue; }
			if (ch == '#' || (ch == '-' && next == '-' && (i + 2 == sql.Length || char.IsWhiteSpace(sql[i + 2]))))
			{ lineComment = true; command.Append(' '); structure.Append(' '); continue; }
			if (ch == '/' && next == '*')
			{
				if (i + 2 < sql.Length && (sql[i + 2] == '!' || sql[i + 2] == 'M')) throw new InvalidOperationException("Executable SQL comments are not allowed in editor saves.");
				blockComment = true; i++; continue;
			}
			if (ch is '\'' or '"' or '`') quote = ch;
			command.Append(ch); structure.Append(quote != '\0' && quote != '`' ? ' ' : ch);
		}
		if (quote != '\0' || blockComment) throw new InvalidOperationException("Unterminated SQL text in editor save.");
		Flush();
		return result;
	}
}
