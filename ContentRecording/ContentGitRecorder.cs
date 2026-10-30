using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LastChaos_ToolBoxNG.ContentRecording;

public sealed record ContentRecordResult(bool Changed, string Commit, int Files, string? Warning = null);
public sealed record ContentCheckoutToken(string Head, string Branch);

/// <summary>Publishes generated, reviewed content without borrowing the user's staging area.</summary>
public static class ContentGitRecorder
{
    private sealed record GitResult(int ExitCode, string Output, string Error);
    private sealed record Candidate(string Relative, string Source, string Destination, string? OldHash, string NewHash,
        string? OldMode, byte[]? OldBytes, FileAttributes Attributes, DateTime LastWriteUtc, byte[] NewBytes);
    private sealed record LfsLock(string Path, string Id);
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static void ValidateCheckout(string checkout) => CaptureCheckout(checkout);

    public static ContentCheckoutToken CaptureCheckout(string checkout)
    {
        checkout = ValidateRoot(checkout);
        string branch = Git(checkout, "symbolic-ref", "--quiet", "--short", "HEAD").Output.Trim();
        if (branch.Length == 0) throw new InvalidOperationException("Select a named Git branch before recording content.");
        string head = Git(checkout, "rev-parse", "--verify", "HEAD").Output.Trim();
        foreach (string state in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" })
        {
            string path = Git(checkout, "rev-parse", "--path-format=absolute", "--git-path", state).Output.Trim();
            if (File.Exists(path) || Directory.Exists(path))
                throw new InvalidOperationException("Finish the current Git merge, rebase or cherry-pick before recording content.");
        }
        if (Git(checkout, "ls-files", "--unmerged", "-z").Output.Length != 0)
            throw new InvalidOperationException("Resolve Git conflicts before recording content.");
        // Git gives its own useful missing-name/email error, before any generated file is published.
        Git(checkout, "var", "GIT_AUTHOR_IDENT");
        Git(checkout, "var", "GIT_COMMITTER_IDENT");
        return new(head, branch);
    }

    public static void AssertCheckout(string checkout, ContentCheckoutToken expected)
    {
        if (CaptureCheckout(checkout) != expected)
            throw new InvalidOperationException("The selected checkout's branch or commit changed while content was being generated. Retry recording on the intended branch.");
    }

    /// <summary>Compares Git-cleaned bytes (including LFS/line-ending filters), without staging files.</summary>
    public static IReadOnlyList<string> FindChangedFiles(string checkout, IReadOnlyDictionary<string, string> stagedFiles)
    {
        checkout = ValidateRoot(checkout);
        var changed = new List<string>();
        foreach (var file in ValidateFiles(checkout, stagedFiles))
            if (HeadHash(checkout, file.Key) != FilterHash(checkout, file.Key, File.ReadAllBytes(file.Value))) changed.Add(file.Key);
        return changed;
    }

    public static ContentRecordResult Record(string checkout, IReadOnlyDictionary<string, string> stagedFiles, string message,
        IProgress<string>? progress = null, ContentCheckoutToken? expectedCheckout = null)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Contains('\0')) throw new ArgumentException("A commit description is required.", nameof(message));
        checkout = ValidateRoot(checkout);
        string gitDirectory = Git(checkout, "rev-parse", "--absolute-git-dir").Output.Trim();
        FileStream operationLock;
        try { operationLock = new FileStream(Path.Combine(gitDirectory, "toolbox-content-recording.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Another Toolbox is recording this checkout. Wait for it to finish.", ex); }
        using (operationLock)
        {
            var original = CaptureCheckout(checkout);
            if (expectedCheckout is not null && original != expectedCheckout) AssertCheckout(checkout, expectedCheckout);
            var candidates = Prepare(checkout, ValidateFiles(checkout, stagedFiles));
            if (candidates.Count == 0) { AssertCheckout(checkout, original); return new(false, "", 0); }
            string marker = "Toolbox-Record-Id: " + Guid.NewGuid().ToString("N");
            string pathspec = Path.Combine(gitDirectory, "toolbox-content-paths-" + Guid.NewGuid().ToString("N"));
            var acquiredLocks = new List<LfsLock>();
            var published = new List<Candidate>();
            var cleanupWarnings = new List<string>();
            bool commitStarted = false;
            bool indexMayHaveChanged = false;
            string? landedCommit = null;
            ContentRecordResult? result = null;
            try
            {
                AcquireLfsLocks(checkout, candidates, acquiredLocks, progress);
                AssertCheckout(checkout, original);
                File.WriteAllBytes(pathspec, Utf8.GetBytes(string.Concat(candidates.Select(c => c.Relative + "\0"))));
                // Validate the full batch before replacing its first file.
                foreach (var candidate in candidates) CheckUnchanged(checkout, candidate);
                foreach (var candidate in candidates)
                {
                    CheckUnchanged(checkout, candidate);
                    published.Add(candidate);
                    Publish(candidate.Destination, candidate.NewBytes, candidate.Attributes);
                }
                AssertCheckout(checkout, original);
                // --only commits the supplied worktree paths and leaves other staged changes alone.
                // New files need an intent-to-add entry so --only recognizes them.
                var newFiles = candidates.Where(c => c.OldHash is null).ToArray();
                if (newFiles.Length != 0)
                {
                    string newSpec = pathspec + ".new";
                    try
                    {
                        File.WriteAllBytes(newSpec, Utf8.GetBytes(string.Concat(newFiles.Select(c => c.Relative + "\0"))));
                        indexMayHaveChanged = true;
                        Git(checkout, "add", "--intent-to-add", "--pathspec-from-file=" + newSpec, "--pathspec-file-nul");
                    }
                    finally { File.Delete(newSpec); }
                }
                AssertCheckout(checkout, original);
                progress?.Report($"Committing {candidates.Count} content files locally…");
                AssertCheckout(checkout, original);
                commitStarted = true;
                indexMayHaveChanged = true;
                Git(checkout, "commit", "--only", "--message", message.Trim() + "\n\n" + marker,
                    "--pathspec-from-file=" + pathspec, "--pathspec-file-nul");
                landedCommit = FindRecordedCommit(checkout, original, marker, candidates);
                if (landedCommit is null) throw new InvalidOperationException("Git returned without the expected content commit. Inspect the selected checkout before retrying.");
                result = RecordedResult(checkout, original, landedCommit, candidates);
            }
            catch (Exception error)
            {
                // A commit may succeed before a hook/timeout/error is observed. Never undo or misreport it.
                if (commitStarted) landedCommit = FindRecordedCommit(checkout, original, marker, candidates);
                if (landedCommit is not null)
                {
                    SafeReport(progress, "The local content commit was recorded; Git also reported: " + error.Message);
                    result = RecordedResult(checkout, original, landedCommit, candidates, error.Message);
                }
                else
                {
                    var recovery = Rollback(checkout, published, indexMayHaveChanged ? candidates : new List<Candidate>());
                    string detail = recovery.Count == 0 ? "The checkout's content files were restored. Resolve the Git error and retry recording." :
                        "Some files changed concurrently and were preserved. Inspect these paths before retrying: " + string.Join(", ", recovery);
                    throw new InvalidOperationException("Content was not committed. " + error.Message + " " + detail, error);
                }
            }
            finally
            {
                try { File.Delete(pathspec); } catch (IOException) { }
                foreach (var item in acquiredLocks)
                {
                    try
                    {
                        var unlock = RunGit(checkout, new[] { "lfs", "unlock", "--id=" + item.Id });
                        if (unlock.ExitCode != 0) cleanupWarnings.Add("Its temporary LFS lock could not be released: " + item.Path + ". " + CleanError(unlock.Error));
                    }
                    catch (Exception error) { cleanupWarnings.Add("Its temporary LFS lock could not be released: " + item.Path + ". " + error.Message); }
                }
            }
            if (cleanupWarnings.Count != 0) result = result! with { Warning = string.Join(" ", new[] { result!.Warning }.Where(s => !string.IsNullOrEmpty(s)).Concat(cleanupWarnings)) };
            return result!;
        }
    }

    private static string ValidateRoot(string checkout)
    {
        if (string.IsNullOrWhiteSpace(checkout)) throw new ArgumentException("Select the game checkout first.", nameof(checkout));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(checkout));
        CheckNoReparse(full, full);
        string actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Git(full, "rev-parse", "--show-toplevel").Output.Trim()));
        if (!string.Equals(full, actual, PathComparison)) throw new InvalidOperationException("Select the Git checkout's root folder, rather than a subfolder.");
        if (Git(full, "rev-parse", "--is-bare-repository").Output.Trim() != "false") throw new InvalidOperationException("A normal working checkout is required.");
        return full;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ValidateFiles(string checkout, IReadOnlyDictionary<string, string> files)
    {
        var approvedSeeds = ContentRecordingPolicy.AllowlistedTables.Select(t => t.SeedRelativePath).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var result = new List<KeyValuePair<string, string>>();
        foreach (var file in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            string relative = file.Key;
            if (relative.Length == 0 || relative.Any(c => char.IsControl(c) || c == '\\' || c == ':') || Path.IsPathRooted(relative) ||
                relative.Split('/').Any(s => s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.')) || !seen.Add(relative))
                throw new InvalidOperationException("Invalid or duplicate generated content path: " + relative);
            bool asset = (relative.StartsWith("x64-client/assets/Data/", StringComparison.Ordinal) || relative.StartsWith("x64-client/assets/Local/", StringComparison.Ordinal)) &&
                (relative.EndsWith(".lod", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
            asset |= relative == "x64-client/assets/Data/Map.dta";
            if (!approvedSeeds.Contains(relative) && !asset) throw new InvalidOperationException("This path is outside the reviewed content recording policy: " + relative);
            string destination = Path.GetFullPath(Path.Combine(checkout, relative.Replace('/', Path.DirectorySeparatorChar)));
            CheckNoReparse(checkout, destination);
            string source = Path.GetFullPath(file.Value);
            if (!Path.IsPathFullyQualified(file.Value) || !File.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The generated content file is missing or is a link: " + relative);
            result.Add(new(relative, source));
        }
        return result;
    }

    private static void CheckNoReparse(string root, string destination)
    {
        string relative = Path.GetRelativePath(root, destination);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Content paths must remain inside the selected checkout.");
        string current = root;
        foreach (string component in new[] { "" }.Concat(relative == "." ? Array.Empty<string>() : relative.Split(Path.DirectorySeparatorChar)))
        {
            if (component.Length != 0) current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Recording through a symbolic link or junction is not allowed: " + current);
        }
    }

    private static List<Candidate> Prepare(string checkout, IReadOnlyList<KeyValuePair<string, string>> files)
    {
        var result = new List<Candidate>();
        foreach (var file in files)
        {
            byte[] bytes = File.ReadAllBytes(file.Value);
            string newHash = FilterHash(checkout, file.Key, bytes);
            string? oldHash = HeadHash(checkout, file.Key);
            if (oldHash == newHash) continue; // Never overwrite an unchanged export's dirty worktree copy.
            string destination = Path.Combine(checkout, file.Key.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(destination)) throw new InvalidOperationException("A content file path is occupied by a directory: " + file.Key);
            string? mode = oldHash is null ? null : Git(checkout, "ls-tree", "HEAD", "--", file.Key).Output.Split(' ', '\t')[0];
            if (mode is not null && mode != "100644" && mode != "100755") throw new InvalidOperationException("Content files must be regular files: " + file.Key);
            bool exists = File.Exists(destination);
            FileAttributes attrs = exists ? File.GetAttributes(destination) : FileAttributes.Normal;
            if ((attrs & FileAttributes.ReadOnly) != 0) throw new InvalidOperationException("The content file is read-only: " + file.Key);
            var candidate = new Candidate(file.Key, file.Value, destination, oldHash, newHash, mode,
                exists ? File.ReadAllBytes(destination) : null, attrs, exists ? File.GetLastWriteTimeUtc(destination) : default, bytes);
            CheckUnchanged(checkout, candidate);
            result.Add(candidate);
        }
        return result;
    }

    private static void CheckUnchanged(string checkout, Candidate candidate)
    {
        CheckNoReparse(checkout, candidate.Destination);
        if (Git(checkout, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--", candidate.Relative).Output.Length != 0)
            throw new InvalidOperationException("Save, commit or move aside existing edits to this generated content file first: " + candidate.Relative);
        string index = Git(checkout, "ls-files", "--stage", "-z", "--", candidate.Relative).Output;
        string flags = Git(checkout, "ls-files", "-v", "-z", "--", candidate.Relative).Output;
        if (candidate.OldHash is not null)
        {
            string expected = candidate.OldMode + " " + candidate.OldHash + " 0\t" + candidate.Relative + "\0";
            if (index != expected || flags.Length == 0 || flags[0] != 'H' || !File.Exists(candidate.Destination) ||
                FilterHash(checkout, candidate.Relative, File.ReadAllBytes(candidate.Destination)) != candidate.OldHash)
                throw new InvalidOperationException("The content file has existing edits or special Git index flags: " + candidate.Relative);
        }
        else if (index.Length != 0 || File.Exists(candidate.Destination))
            throw new InvalidOperationException("An untracked or staged file already uses this generated content path: " + candidate.Relative);
        bool exists = File.Exists(candidate.Destination);
        if (exists != (candidate.OldBytes is not null) || (exists && !File.ReadAllBytes(candidate.Destination).AsSpan().SequenceEqual(candidate.OldBytes)))
            throw new InvalidOperationException("The content file changed while recording: " + candidate.Relative);
    }

    private static void Publish(string destination, byte[] bytes, FileAttributes attributes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + ".toolbox-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            File.Move(temp, destination, true);
            File.SetAttributes(destination, attributes);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static List<string> Rollback(string checkout, List<Candidate> published, List<Candidate> all)
    {
        var problems = new List<string>();
        foreach (var file in published.AsEnumerable().Reverse())
        {
            try
            {
                CheckNoReparse(checkout, file.Destination);
                // Do not clobber changes made concurrently by a user, hook, or another Git command.
                if (HeadHash(checkout, file.Relative) != file.OldHash) { problems.Add(file.Relative); continue; }
                bool exists = File.Exists(file.Destination);
                if (!exists && file.OldBytes is null) continue;
                if (exists && file.OldBytes is not null && File.ReadAllBytes(file.Destination).AsSpan().SequenceEqual(file.OldBytes))
                { File.SetAttributes(file.Destination, file.Attributes); File.SetLastWriteTimeUtc(file.Destination, file.LastWriteUtc); continue; }
                if (!exists || !File.ReadAllBytes(file.Destination).AsSpan().SequenceEqual(file.NewBytes)) { problems.Add(file.Relative); continue; }
                if (file.OldBytes is null) File.Delete(file.Destination);
                else { Publish(file.Destination, file.OldBytes, file.Attributes); File.SetLastWriteTimeUtc(file.Destination, file.LastWriteUtc); }
            }
            catch { problems.Add(file.Relative); }
        }
        // Restore only this operation's index entries. Never replace the user's complete index.
        foreach (var file in all)
        {
            try
            {
                if (HeadHash(checkout, file.Relative) != file.OldHash) { if (!problems.Contains(file.Relative)) problems.Add(file.Relative); continue; }
                string index = Git(checkout, "ls-files", "--stage", "-z", "--", file.Relative).Output;
                if (file.OldHash is not null)
                {
                    string old = file.OldMode + " " + file.OldHash + " 0\t" + file.Relative + "\0";
                    if (index == old) continue;
                    if (!index.Contains(" " + file.NewHash + " 0\t", StringComparison.Ordinal)) { problems.Add(file.Relative + " (index)"); continue; }
                    GitInput(checkout, Utf8.GetBytes(old), "update-index", "-z", "--index-info");
                }
                else if (index.Length != 0)
                {
                    string empty = GitInput(checkout, Array.Empty<byte>(), "hash-object", "--stdin").Output.Trim();
                    if (!index.Contains(" " + empty + " 0\t", StringComparison.Ordinal) && !index.Contains(" " + file.NewHash + " 0\t", StringComparison.Ordinal))
                    { problems.Add(file.Relative + " (index)"); continue; }
                    GitInput(checkout, Utf8.GetBytes("0 " + new string('0', empty.Length) + "\t" + file.Relative + "\0"), "update-index", "-z", "--index-info");
                }
            }
            catch { problems.Add(file.Relative + " (index)"); }
        }
        return problems.Distinct().ToList();
    }

    private static void AcquireLfsLocks(string checkout, List<Candidate> candidates, List<LfsLock> acquired, IProgress<string>? progress)
    {
        foreach (var file in candidates)
        {
            string[] attributes = Git(checkout, "check-attr", "-z", "filter", "--", file.Relative).Output.Split('\0');
            bool lfs = attributes.Length >= 3 && attributes[2] == "lfs";
            if (!file.Relative.StartsWith("x64-client/assets/", StringComparison.Ordinal)) continue;
            if (!lfs) throw new InvalidOperationException("Authored client data must have its Git LFS filter configured: " + file.Relative);
            Git(checkout, "lfs", "version");
            progress?.Report("Locking authored client data: " + file.Relative);
            var locked = RunGit(checkout, new[] { "lfs", "lock", "--json", file.Relative });
            if (locked.ExitCode == 0)
            {
                using var json = JsonDocument.Parse(locked.Output);
                JsonElement element = json.RootElement;
                if (element.ValueKind == JsonValueKind.Array) element = element.EnumerateArray().Single(item => item.GetProperty("path").GetString() == file.Relative);
                if (element.TryGetProperty("lock", out var inner)) element = inner;
                string id = element.GetProperty("id").GetString() ?? throw new InvalidOperationException("Git LFS returned an empty lock ID.");
                acquired.Add(new(file.Relative, id));
                continue;
            }
            // An existing lock owned by this user is usable, but must not be released by us.
            var existing = RunGit(checkout, new[] { "lfs", "locks", "--verify", "--json" });
            if (existing.ExitCode == 0)
            {
                using var json = JsonDocument.Parse(existing.Output);
                if (json.RootElement.TryGetProperty("ours", out var ours) && ours.EnumerateArray().Any(item => item.GetProperty("path").GetString() == file.Relative)) continue;
            }
            throw new InvalidOperationException("Could not obtain a Git LFS lock for " + file.Relative + ". Check repository access and existing locks. " + CleanError(locked.Error));
        }
    }

    private static string? FindRecordedCommit(string checkout, ContentCheckoutToken original, string marker, List<Candidate> files)
    {
        try
        {
            // Follow the original named branch even if a hook checked out something else.
            var log = RunGit(checkout, new[] { "log", "--format=%H%x00%B%x00", "--fixed-strings", "--grep=" + marker, original.Head + "..refs/heads/" + original.Branch });
            if (log.ExitCode != 0) return null;
            string[] parts = log.Output.Split('\0');
            for (int i = 0; i + 1 < parts.Length; i += 2)
                if (parts[i + 1].Split('\n').Contains(marker)) return parts[i].Trim();
            // A commit-msg hook may legitimately replace the complete message, including our trailer.
            string tip = Git(checkout, "rev-parse", "refs/heads/" + original.Branch).Output.Trim();
            if (tip != original.Head && MatchesRecordedTree(checkout, original.Head, tip, files)) return tip;
        }
        catch { /* The original Git error will be retained. */ }
        return null;
    }

    private static bool MatchesRecordedTree(string checkout, string oldHead, string commit, List<Candidate> files)
    {
        string[] parents = Git(checkout, "rev-list", "--parents", "-n", "1", commit).Output.Trim().Split(' ');
        string[] changed = ChangedInCommit(checkout, oldHead, commit);
        return parents.Length == 2 && parents[1] == oldHead &&
            changed.ToHashSet(StringComparer.Ordinal).SetEquals(files.Select(f => f.Relative)) &&
            files.All(f => TreeHash(checkout, commit, f.Relative) == f.NewHash);
    }

    private static string[] ChangedInCommit(string checkout, string oldHead, string commit) =>
        Git(checkout, "diff-tree", "--no-commit-id", "--name-only", "-r", "-z", oldHead, commit).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static ContentRecordResult RecordedResult(string checkout, ContentCheckoutToken original, string commit, List<Candidate> files, string? warning = null)
    {
        try
        {
            int count = ChangedInCommit(checkout, original.Head, commit).Length;
            if (!MatchesRecordedTree(checkout, original.Head, commit, files))
                warning = string.Join(" ", new[] { warning, "A Git hook or concurrent Git operation changed the recorded commit. Review local commit " + commit + " before publishing it." }.Where(s => !string.IsNullOrEmpty(s)));
            return new(true, commit, count, warning);
        }
        catch (Exception error)
        {
            return new(true, commit, files.Count, string.Join(" ", new[] { warning, "The commit was recorded, but its final verification needs attention: " + error.Message }.Where(s => !string.IsNullOrEmpty(s))));
        }
    }

    private static string? HeadHash(string checkout, string path)
        => TreeHash(checkout, "HEAD", path);

    private static string? TreeHash(string checkout, string tree, string path)
    {
        string result = Git(checkout, "ls-tree", "-z", tree, "--", path).Output;
        return result.Length == 0 ? null : result.Split(' ', '\t')[2];
    }

    private static string FilterHash(string checkout, string path, byte[] bytes) => GitInput(checkout, bytes, "hash-object", "--path=" + path, "--stdin").Output.Trim();
    private static GitResult Git(string checkout, params string[] args) => EnsureSuccess(RunGit(checkout, args));
    private static GitResult GitInput(string checkout, byte[] input, params string[] args) => EnsureSuccess(RunGit(checkout, args, input));
    private static GitResult EnsureSuccess(GitResult result)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException("Git could not record content: " + CleanError(result.Error.Length == 0 ? result.Output : result.Error));
        return result;
    }
    private static string CleanError(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"(https?://)[^\s/@]+(?::[^\s/@]*)?@", "$1[redacted]@");
    private static void SafeReport(IProgress<string>? progress, string message) { try { progress?.Report(message); } catch { } }

    private static GitResult RunGit(string checkout, IEnumerable<string> arguments, byte[]? input = null)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = checkout, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8 };
        foreach (string key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_PREFIX", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_QUARANTINE_PATH" })
            start.Environment.Remove(key); // Never inherit another checkout or staging area.
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase) || k.Equals("GIT_CURL_VERBOSE", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.ArgumentList.Add("--literal-pathspecs");
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = StartGit(start);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task write = Task.Run(async () => { try { if (input is not null) await process.StandardInput.BaseStream.WriteAsync(input); } finally { process.StandardInput.Close(); } });
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            process.WaitForExit();
            throw new TimeoutException("Git timed out while recording content. Check authentication, signing prompts, or hooks before retrying.");
        }
        try { write.GetAwaiter().GetResult(); } catch (IOException) when (process.ExitCode != 0) { }
        return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static Process StartGit(ProcessStartInfo start)
    {
        try { return Process.Start(start) ?? throw new InvalidOperationException("Git could not be started. Install Git for Windows and restart the Toolbox."); }
        catch (System.ComponentModel.Win32Exception error) { throw new InvalidOperationException("Git could not be started. Install Git for Windows and restart the Toolbox.", error); }
    }
}
