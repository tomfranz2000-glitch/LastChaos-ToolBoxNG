using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LastChaos_ToolBoxNG.ContentRecording;

static class Verify
{
    const string Item = "x64-server/db/seeds/ep4_data/t_item.sql";
    const string Npc = "x64-server/db/seeds/ep4_data/t_npc.sql";
    static int count;
    static string? filter;

    static void Main(string[] args)
    {
        filter = args.FirstOrDefault();
        Test("commit preserves unrelated staged, unstaged and untracked bytes", () => {
            using var r = new Repo();
            r.Write("other.txt", "staged\n"); r.Git("add", "other.txt"); r.Write("other.txt", "unstaged\n"); r.Write("notes untracked.txt", "keep me");
            string index = r.Git("ls-files", "--stage", "--", "other.txt");
            var result = ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Record fixture");
            Check(result.Changed && result.Files == 1, "commit result");
            Check(r.Git("ls-files", "--stage", "--", "other.txt") == index, "unrelated staged entry changed");
            Check(r.Read("other.txt") == "unstaged\n" && r.Read("notes untracked.txt") == "keep me", "unrelated working bytes changed");
            Check(r.Git("show", "--format=", "--name-only", "HEAD").Trim() == Item, "commit contained unrelated path");
        });
        Test("filtered CRLF exports produce no commit and preserve dirty copy", () => {
            using var r = new Repo();
            r.Write(Item, "player's local edit\r\n");
            var files = r.Files(Item, "old item\r\n");
            Check(ContentGitRecorder.FindChangedFiles(r.Root, files).Count == 0, "clean-filter hash mismatch");
            var result = ContentGitRecorder.Record(r.Root, files, "No changes");
            Check(!result.Changed && result.Commit.Length == 0 && r.Read(Item) == "player's local edit\r\n", "no-op overwrote dirty file");
        });
        Test("changed target with unstaged edits aborts whole batch", () => {
            using var r = new Repo(); r.Write(Npc, "my edits\n");
            var files = r.Files(Item, "new item\n"); files.Add(Npc, r.Generate("new npc\n"));
            Throws(() => ContentGitRecorder.Record(r.Root, files, "Must fail"), "existing edits");
            Check(r.Read(Item) == "old item\n" && r.Read(Npc) == "my edits\n", "partial publication");
        });
        Test("staged target conflicts even if worktree matches HEAD", () => {
            using var r = new Repo(); r.Write(Item, "staged edit\n"); r.Git("add", Item); r.Write(Item, "old item\n");
            string index = r.Git("ls-files", "--stage", "--", Item);
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail"), "existing edits");
            Check(r.Git("ls-files", "--stage", "--", Item) == index && r.Read(Item) == "old item\n", "dirty target changed");
        });
        Test("failed hook restores exact file bytes, timestamp and index", () => {
            using var r = new Repo();
            DateTime stamp = DateTime.UtcNow.AddDays(-2); File.SetLastWriteTimeUtc(r.PathFor(Item), stamp);
            r.Write("other.txt", "staged\n"); r.Git("add", "other.txt");
            string index = r.Git("ls-files", "--stage", "-z"); string head = r.Git("rev-parse", "HEAD");
            r.Hook("pre-commit", "#!/bin/sh\necho Fixture hook refused >&2\nexit 1\n");
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail"), "Fixture hook refused");
            Check(r.Read(Item) == "old item\n" && File.GetLastWriteTimeUtc(r.PathFor(Item)) == stamp, "rollback lost bytes/timestamp");
            Check(r.Git("ls-files", "--stage", "-z") == index && r.Git("rev-parse", "HEAD") == head, "rollback changed staged content or HEAD");
        });
        Test("traversal and private-data paths are rejected", () => {
            using var r = new Repo();
            foreach (string path in new[] { "x64-server/db/seeds/ep4_data/../t_item.sql", "../escape.sql", "x64-server/db/seeds/ep4_db/t_characters.sql", "Settings.ini", "x64-client/assets/Data/private.txt" })
                Throws(() => ContentGitRecorder.Record(r.Root, r.Files(path, "bad"), "Must fail"));
        });
        Test("commit-message hooks may replace the recording trailer", () => {
            using var r = new Repo();
            r.Hook("commit-msg", "#!/bin/sh\nprintf 'Required team commit message\\n' > \"$1\"\n");
            var result = ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Record fixture");
            Check(result.Changed && r.Git("log", "-1", "--format=%s").Trim() == "Required team commit message", "landed commit misreported as failure");
        });
        Test("signing failures respect config and restore files", () => {
            using var r = new Repo(); r.Git("config", "commit.gpgsign", "true"); r.Git("config", "gpg.program", "definitely-missing-fixture-signing-program");
            string head = r.Git("rev-parse", "HEAD");
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail"));
            Check(r.Read(Item) == "old item\n" && r.Git("rev-parse", "HEAD") == head, "signing was bypassed or rollback failed");
        });
        Test("junction destinations cannot escape checkout", () => {
            using var r = new Repo(); string outside = System.IO.Path.Combine(r.Area, "outside"); Directory.CreateDirectory(outside);
            string data = r.PathFor("x64-client/assets/Data"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(data)!);
            if (OperatingSystem.IsWindows()) Run("cmd", r.Root, "/c", "mklink", "/J", data, outside);
            else Directory.CreateSymbolicLink(data, outside);
            Throws(() => ContentGitRecorder.FindChangedFiles(r.Root, r.Files("x64-client/assets/Data/escape.lod", "bad")), "link");
            Check(!File.Exists(System.IO.Path.Combine(outside, "escape.lod")), "escaped write");
            Directory.Delete(data); // Remove link only, before fixture's ordinary cleanup.
        });
        Test("HEAD move during generation aborts before publication", () => {
            using var r = new Repo(); var token = ContentGitRecorder.CaptureCheckout(r.Root);
            r.Git("commit", "--allow-empty", "-m", "another change");
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail", expectedCheckout: token), "changed");
            Check(r.Read(Item) == "old item\n", "published after HEAD move");
        });
        Test("same-HEAD branch switch during generation is detected", () => {
            using var r = new Repo(); var token = ContentGitRecorder.CaptureCheckout(r.Root); r.Git("switch", "-c", "another-branch");
            Throws(() => ContentGitRecorder.AssertCheckout(r.Root, token), "changed");
        });
        Test("HEAD move just before commit rolls back own file", () => {
            using var r = new Repo(); bool moved = false;
            var progress = new InlineProgress(text => { if (text.StartsWith("Committing", StringComparison.Ordinal) && !moved) { moved = true; r.Git("commit", "--allow-empty", "-m", "concurrent commit"); } });
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail", progress), "changed");
            Check(r.Read(Item) == "old item\n" && r.Git("log", "-1", "--format=%s").Trim() == "concurrent commit", "race rollback damaged unrelated commit");
        });
        Test("concurrent target edits are preserved on rollback", () => {
            using var r = new Repo();
            var progress = new InlineProgress(text => { if (text.StartsWith("Committing", StringComparison.Ordinal)) { r.Write(Item, "concurrent human edit\n"); throw new Exception("fixture interrupt"); } });
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail", progress), "concurrently");
            Check(r.Read(Item) == "concurrent human edit\n", "rollback clobbered human edit");
        });
        Test("detached HEAD and in-progress merges are rejected", () => {
            using var r = new Repo(); string head = r.Git("rev-parse", "HEAD").Trim(); r.Git("switch", "--detach", head);
            Throws(() => ContentGitRecorder.ValidateCheckout(r.Root)); r.Git("switch", "fixture");
            File.WriteAllText(r.PathFor(".git/MERGE_HEAD"), head + "\n"); Throws(() => ContentGitRecorder.ValidateCheckout(r.Root), "merge");
        });
        Test("assume-unchanged target edits are detected", () => {
            using var r = new Repo(); r.Git("update-index", "--assume-unchanged", Item); r.Write(Item, "hidden edit\n");
            Throws(() => ContentGitRecorder.Record(r.Root, r.Files(Item, "new item\n"), "Must fail"), "index flags");
            Check(r.Read(Item) == "hidden edit\n", "hidden edits overwritten");
        });
        if (CanRunLfs())
        {
            Test("real LFS stores pointer, preserves existing locks and cleans up new locks", () => {
                using var server = new LfsServer(); using var r = new Repo(); r.EnableLfs(server.Url);
                string asset = "x64-client/assets/Data/魚 rod icon.lod";
                var files = r.Files(asset, "binary fixture Ω\0\x01");
                var result = ContentGitRecorder.Record(r.Root, files, "New Unicode asset");
                Check(result.Changed && result.Files == 1, "asset not recorded");
                Check(r.Git("show", "HEAD:" + asset).StartsWith("version https://git-lfs.github.com/spec/v1\n", StringComparison.Ordinal), "asset not LFS pointer");
                Check(server.ActiveLocks.Count == 0, "new LFS lock leaked");
                Check(!ContentGitRecorder.Record(r.Root, files, "LFS unchanged").Changed, "LFS pointer compared to raw data");
                server.ActiveLocks.Add(asset, "preexisting");
                ContentGitRecorder.Record(r.Root, r.Files(asset, "changed binary fixture\0"), "Keep user's lock");
                Check(server.ActiveLocks.TryGetValue(asset, out string? id) && id == "preexisting", "user's existing LFS lock was released");
            });
            Test("failed commit removes only new file's intent-to-add index entry", () => {
                using var server = new LfsServer(); using var r = new Repo(); r.EnableLfs(server.Url);
                string asset = "x64-client/assets/Data/new file.lod";
                r.Write("other.txt", "staged\n"); r.Git("add", "other.txt"); string index = r.Git("ls-files", "--stage", "-z");
                r.Hook("pre-commit", "#!/bin/sh\nexit 1\n");
                Throws(() => ContentGitRecorder.Record(r.Root, r.Files(asset, "bytes\0"), "Must fail"));
                Check(!File.Exists(r.PathFor(asset)) && r.Git("ls-files", "--stage", "-z") == index, "intent-to-add rollback failed");
                Check(server.ActiveLocks.Count == 0, "LFS lock leaked on failure");
            });
            Test("unavailable LFS lock service aborts before replacing files", () => {
                using var r = new Repo(); r.EnableLfs("http://127.0.0.1:1/lfs");
                r.Git("config", "lfs.activitytimeout", "1"); r.Git("config", "lfs.dialtimeout", "1");
                string asset = "x64-client/assets/Data/no-service.lod";
                Throws(() => ContentGitRecorder.Record(r.Root, r.Files(asset, "bytes\0"), "Must fail"), "LFS lock");
                Check(!File.Exists(r.PathFor(asset)), "published before obtaining lock");
            });
        }
        else Console.WriteLine("SKIP LFS fixtures: Git LFS unavailable.");
        Console.WriteLine($"Passed {count} isolated Git fixtures.");
    }

    static void Test(string name, Action action) { if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return; action(); ++count; Console.WriteLine("PASS " + name); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws(Action action, string? contains = null)
    {
        try { action(); } catch (Exception error) { if (contains is not null && !error.ToString().Contains(contains, StringComparison.OrdinalIgnoreCase)) throw new Exception("Wrong error, expected " + contains, error); return; }
        throw new Exception("Expected failure.");
    }
    static bool CanRunLfs() { try { Run("git", Environment.CurrentDirectory, "lfs", "version"); return true; } catch { return false; } }
    static string Run(string executable, string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new Exception("Fixture Git timed out."); }
        if (process.ExitCode != 0) throw new Exception(error.Result); return output.Result;
    }

    sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    sealed class Repo : IDisposable
    {
        public string Area { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Toolbox Git fixtures " + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public Repo()
        {
            Root = System.IO.Path.Combine(Area, "checkout ü 中文"); Directory.CreateDirectory(Root);
            Git("init", "-b", "fixture"); Git("config", "user.name", "Fixture User"); Git("config", "user.email", "fixture@example.invalid"); Git("config", "commit.gpgsign", "false"); Git("config", "core.autocrlf", "false");
            Git("config", "core.hooksPath", ".git/hooks");
            Write(".gitattributes", "*.sql text eol=lf\n"); Write(Item, "old item\n"); Write(Npc, "old npc\n"); Write("other.txt", "original\n"); Git("add", "."); Git("commit", "-m", "fixture baseline");
        }
        public string Git(params string[] args) => Run("git", Root, args);
        public string PathFor(string relative) => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public void Write(string relative, string bytes) { string path = PathFor(relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, bytes, new UTF8Encoding(false)); }
        public string Read(string relative) => File.ReadAllText(PathFor(relative));
        public string Generate(string text) { string path = System.IO.Path.Combine(Area, "generated ' ü 中文 " + Guid.NewGuid() + ".sql"); File.WriteAllText(path, text, new UTF8Encoding(false)); return path; }
        public Dictionary<string, string> Files(string path, string bytes) => new() { [path] = Generate(bytes) };
        public void Hook(string name, string text) { Write(".git/hooks/" + name, text); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(PathFor(".git/hooks/" + name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        public void EnableLfs(string endpoint)
        {
            Git("lfs", "install", "--local"); Git("config", "lfs.url", endpoint); Git("remote", "add", "origin", endpoint.Replace("/lfs", "/repo.git"));
            Write(".gitattributes", "*.sql text eol=lf\nx64-client/assets/** filter=lfs diff=lfs merge=lfs -text\n"); Git("add", ".gitattributes"); Git("commit", "-m", "fixture LFS attributes");
        }
        public void Dispose()
        {
            // Area is the fixture's explicit, unique absolute directory. No user checkout is removed.
            string prefix = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (!System.IO.Path.GetFullPath(Area).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(Area).StartsWith("Toolbox Git fixtures ", StringComparison.Ordinal)) throw new Exception("Unsafe fixture cleanup path.");
            foreach (string file in Directory.EnumerateFiles(Area, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Area, true);
        }
    }

    // A real localhost LFS locking endpoint; Git and Git LFS execute unchanged.
    sealed class LfsServer : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly Task running;
        public Dictionary<string, string> ActiveLocks { get; } = new(StringComparer.Ordinal);
        public string Url { get; }
        public LfsServer() { listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/lfs"; running = Task.Run(Serve); }
        async Task Serve()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(stop.Token); using NetworkStream stream = client.GetStream();
                    using var headerBytes = new MemoryStream(); byte[] one = new byte[1];
                    while (true)
                    {
                        await stream.ReadExactlyAsync(one, stop.Token); headerBytes.WriteByte(one[0]);
                        if (headerBytes.Length >= 4 && Encoding.ASCII.GetString(headerBytes.GetBuffer(), (int)headerBytes.Length - 4, 4) == "\r\n\r\n") break;
                    }
                    string[] headersRead = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n");
                    string first = headersRead[0]; string route = first.Split(' ')[1];
                    string? lengthHeader = headersRead.FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    int length = lengthHeader is null ? 0 : int.Parse(lengthHeader[15..].Trim());
                    byte[] request = new byte[length]; await stream.ReadExactlyAsync(request, stop.Token);
                    string body = Encoding.UTF8.GetString(request); int status = 200; object response;
                    lock (ActiveLocks)
                    {
                        if (route.EndsWith("/locks/verify", StringComparison.Ordinal)) response = new { ours = ActiveLocks.Select(x => Lock(x.Key, x.Value)).ToArray(), theirs = Array.Empty<object>() };
                        else if (route.Contains("/unlock", StringComparison.Ordinal))
                        { string id = route.Split('/')[^2]; var pair = ActiveLocks.Single(x => x.Value == id); ActiveLocks.Remove(pair.Key); response = new { @lock = Lock(pair.Key, id) }; }
                        else if (route.EndsWith("/locks", StringComparison.Ordinal) && first.StartsWith("POST", StringComparison.Ordinal))
                        {
                            string path = JsonDocument.Parse(body).RootElement.GetProperty("path").GetString()!;
                            if (ActiveLocks.TryGetValue(path, out string? id)) { status = 409; response = new { message = "Lock exists", @lock = Lock(path, id) }; }
                            else { id = Guid.NewGuid().ToString("N"); ActiveLocks.Add(path, id); response = new { @lock = Lock(path, id) }; }
                        }
                        else response = new { locks = ActiveLocks.Select(x => Lock(x.Key, x.Value)).ToArray() };
                    }
                    byte[] data = JsonSerializer.SerializeToUtf8Bytes(response);
                    byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Conflict")}\r\nContent-Type: application/vnd.git-lfs+json\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers); await stream.WriteAsync(data);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        }
        static object Lock(string path, string id) => new { path, id, locked_at = "2026-01-01T00:00:00Z", owner = new { name = "Fixture User" } };
        public void Dispose() { stop.Cancel(); listener.Stop(); running.GetAwaiter().GetResult(); stop.Dispose(); }
    }
}
