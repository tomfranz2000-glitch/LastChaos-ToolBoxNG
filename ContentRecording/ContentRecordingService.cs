namespace LastChaos_ToolBoxNG.ContentRecording;

/// <summary>Generate everything in staging before publishing any file to the user's checkout.</summary>
public static class ContentRecordingService
{
    public static Task<ContentRecordResult> RecordAsync(Main host, IProgress<string>? progress = null)
    {
        string checkout = Path.GetFullPath(host.pSettings.ContentCheckout);
        string connectionString = new MySqlConnectionStringBuilder {
            Server = host.pSettings.DBHost, Database = host.pSettings.DBData,
            UserID = host.pSettings.DBUsername, Password = host.pSettings.DBPassword,
            CharacterSet = "utf8mb4", AllowUserVariables = false
        }.ConnectionString;
        return RecordAsync(host, checkout, connectionString, new ContentCaptureOptions(), progress);
    }

    // Explicit endpoints also let the integration fixture use a disposable database and checkout.
    public static async Task<ContentRecordResult> RecordAsync(Main host, string checkout, string connectionString,
        ContentCaptureOptions captureOptions, IProgress<string>? progress = null)
    {
        progress?.Report("Checking the game checkout…");
        var checkoutToken = await Task.Run(() => ContentGitRecorder.CaptureCheckout(checkout));
        progress?.Report("Taking a consistent snapshot of saved content…");
        var snapshot = await ContentDatabaseSnapshot.CaptureAsync(checkout, connectionString, captureOptions, progress: progress);
        ContentRecordResult? result = null;
        try {
            progress?.Report("Exporting content seeds…");
            var seeds = await snapshot.DumpSeedsAsync(progress: progress);
            var changed = await Task.Run(() => ContentGitRecorder.FindChangedFiles(checkout, seeds));
            if (changed.Count == 0) {
                await Task.Run(() => ContentGitRecorder.AssertCheckout(checkout, checkoutToken));
                result = new(false, "", 0);
            } else {
                var tables = changed.Select(path => Path.GetFileName(Path.GetDirectoryName(path)) + "." + Path.GetFileNameWithoutExtension(path)).ToArray();
                progress?.Report($"Generating client data for {changed.Count} changed content tables…");
                string clientStaging = Path.Combine(snapshot.StagingDirectory, "client-output");
                var clientFiles = await ContentClientExports.GenerateAsync(host, snapshot.Query, tables, clientStaging, progress);
                var changedClientFiles = await Task.Run(() => ContentGitRecorder.FindChangedFiles(checkout, clientFiles));
                var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string path in changed) outputs.Add(path, seeds[path]);
                foreach (string path in changedClientFiles) outputs.Add(path, clientFiles[path]);
                string message = $"Record content: {changed.Count} tables, {changedClientFiles.Count} client files";
                progress?.Report("Recording the local content commit…");
                result = await Task.Run(() => ContentGitRecorder.Record(checkout, outputs, message, progress, checkoutToken));
            }
        } finally {
            try { await snapshot.DisposeAsync(); }
            catch (Exception cleanup) {
                string warning = "Temporary content snapshot cleanup needs attention: " + cleanup.Message;
                SafeReport(progress, warning);
                // A completed commit stays completed. A cleanup failure must not suggest retrying it.
                if (result is not null) result = result with { Warning = JoinWarnings(result.Warning, warning) };
            }
            if (!string.IsNullOrEmpty(snapshot.CleanupWarning)) {
                SafeReport(progress, snapshot.CleanupWarning);
                if (result is not null) result = result with { Warning = JoinWarnings(result.Warning, snapshot.CleanupWarning) };
            }
        }
        return result!;
    }

    private static string JoinWarnings(string? existing, string warning) =>
        string.IsNullOrEmpty(existing) ? warning : existing + "\n" + warning;

    private static void SafeReport(IProgress<string>? progress, string message)
    { try { progress?.Report(message); } catch { /* Reporting must not hide the completed commit or original failure. */ } }
}
