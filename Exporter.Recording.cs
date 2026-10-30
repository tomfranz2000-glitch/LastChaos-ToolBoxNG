using LastChaos_ToolBoxNG.ContentRecording;

namespace LastChaos_ToolBoxNG;

public partial class Exporter
{
    private readonly RecordingExportContext? recording;
    private HashSet<int>? fortuneItemIds;
    private bool IsRecording => recording != null;
    private string ExportClientRoot => recording?.OutputRoot ?? pMain.pSettings.ClientPath;
    private string InputClientRoot => recording?.InputRoot ?? pMain.pSettings.ClientPath;
    private string ExportDatabase => recording == null ? pMain.pSettings.DBData : "ep4_data";
    private int ExportLogVerbosity => recording?.LogVerbosity ?? pMain.pSettings.LogVerbose;
    private bool ExportToLocalFolder => recording == null && rbExportToLocalFolder.Checked;

    // This instance has no controls, handle, Load event or preference persistence.
    internal Exporter(Main host, RecordingExportContext context)
    {
        pMain = host;
        recording = context;
    }

    private DataTable? QueryForExport(string charset, string sql, bool log = true)
    {
        if (recording == null) return pMain.QuerySelect(charset, sql, log);
        sql = RecordingQuery(sql);
        // ItemAll asks the same existence question once per item. Read this small
        // immutable relation once instead of opening thousands of DB queries.
        Match fortune = Regex.Match(sql, @"^SELECT COUNT\(\*\) FROM ep4_data\.t_fortune_data WHERE a_item_idx=(\d+) ", RegexOptions.IgnoreCase);
        if (fortune.Success)
        {
            if (fortuneItemIds == null)
            {
                using DataTable rows = recording.Query("utf8", "SELECT DISTINCT a_item_idx FROM ep4_data.t_fortune_data;")
                    ?? throw new InvalidOperationException("The content snapshot returned no fortune data.");
                fortuneItemIds = rows.Rows.Cast<DataRow>().Select(row => Convert.ToInt32(row["a_item_idx"])).ToHashSet();
            }
            var count = new DataTable();
            count.Columns.Add("COUNT(*)", typeof(int));
            count.Rows.Add(fortuneItemIds.Contains(int.Parse(fortune.Groups[1].Value, CultureInfo.InvariantCulture)) ? 1 : 0);
            return count;
        }
        return recording.Query(charset, sql)
            ?? throw new InvalidOperationException("The content snapshot returned no result for a required client export query.");
    }

    private void LogExport(LogTypes type, string message, bool printStackFrame = true)
    {
        if (recording == null) pMain.Logger(type, message, printStackFrame);
        else if (type == LogTypes.Error) throw new InvalidOperationException(message);
        else recording.Progress?.Report(message);
    }

    private string ExportDataFilePath(string directory, string fileName) =>
        Path.Combine(directory, IsRecording ? fileName : fileName + ".lod");

    // The normal exporter retains its existing selectable legacy layout. Recording
    // targets the shipping G_USA client (WorldData.cpp and Common/header/def_*).
    private static string RecordingQuery(string sql)
    {
        if (sql.Contains(".t_item WHERE", StringComparison.OrdinalIgnoreCase) && !sql.Contains("a_need_sskill2", StringComparison.Ordinal) && sql.Contains("a_need_sskill_level,", StringComparison.Ordinal))
            sql = sql.Replace("a_need_sskill_level,", "a_need_sskill_level, a_need_sskill2, a_need_sskill_level2,", StringComparison.Ordinal);
        if (sql.Contains(".t_skill WHERE", StringComparison.OrdinalIgnoreCase) && !sql.Contains("a_fireRange2", StringComparison.Ordinal) && sql.Contains("a_fireRange, ", StringComparison.Ordinal))
            sql = sql.Replace("a_fireRange, ", "a_fireRange, a_fireRange2, ", StringComparison.Ordinal);
        if (sql.Contains(".t_shop ORDER", StringComparison.OrdinalIgnoreCase) && !sql.Contains("a_name,", StringComparison.Ordinal))
            sql = sql.Replace("a_keeper_idx, a_sell_rate", "a_keeper_idx, a_name, a_sell_rate", StringComparison.Ordinal);
        if (sql.Contains(".t_attack_pet ORDER", StringComparison.OrdinalIgnoreCase) && !sql.Contains("a_name,", StringComparison.Ordinal))
            sql = sql.Replace("a_index, a_type", "a_index, a_name, a_type", StringComparison.Ordinal);
        if (sql.Contains(".t_rareoption WHERE", StringComparison.OrdinalIgnoreCase) && !sql.Contains("a_type,", StringComparison.Ordinal))
            sql = sql.Replace("a_grade, a_attack", "a_grade, a_type, a_attack", StringComparison.Ordinal);
        if (sql.Contains(".t_catalog WHERE", StringComparison.OrdinalIgnoreCase))
            sql = sql.Replace("a_icon FROM", "a_icon, a_texture_id, a_texture_row, a_texture_col FROM", StringComparison.Ordinal)
                .Replace("ORDER BY a_dummy DESC, a_ctid ASC", "ORDER BY a_ctid ASC", StringComparison.Ordinal);
        if (sql.Contains(".t_title ORDER", StringComparison.OrdinalIgnoreCase))
            sql = sql.Replace("a_item_index FROM", "a_option_value0, a_option_value1, a_option_value2, a_option_value3, a_option_value4, a_item_index FROM", StringComparison.Ordinal);
        return sql;
    }

    private string InputSmcPath(string relative)
    {
        if (!IsRecording) return InputClientRoot + "\\" + relative;
        string full = Path.GetFullPath(Path.Combine(InputClientRoot, relative));
        if (!full.Equals(InputClientRoot, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(InputClientRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An item SMC path leaves the configured client asset directory.");
        return full;
    }

    internal async Task GenerateRecordedFileAsync(ContentClientExport plan)
    {
        ExportTasks.Clear();
        if (plan.StringType != null) ExportStringsAsync(plan.StringType, plan.Nation!);
        else
        {
            MethodInfo method = typeof(Exporter).GetMethod($"Export{plan.Writer}Async", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"No client writer exists for {plan.Writer}.");
            object[] arguments = plan.Writer is "SHOPS" or "CATALOGS" ? [plan.FileName, plan.Nation!] : [plan.FileName];
#if EXPORT_LACARETTES_FILES_FOR_EACH_LANGUAGE
            if (plan.Writer == "LACARETTES") arguments = [plan.FileName, plan.Nation!];
#endif
            method.Invoke(this, arguments);
        }
        await Task.WhenAll(ExportTasks).ConfigureAwait(false);
        if (!File.Exists(Path.Combine(recording!.OutputRoot, plan.RelativeClientPath)))
            throw new InvalidOperationException($"Client writer did not produce {plan.RelativeClientPath}; no content was recorded.");
    }
}

internal sealed record RecordingExportContext(string InputRoot, string OutputRoot,
    Func<string, string, DataTable> Query, int LogVerbosity, IProgress<string>? Progress);
