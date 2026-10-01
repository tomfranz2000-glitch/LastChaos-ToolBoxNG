using System.Diagnostics;
using MySqlConnector;

namespace LastChaos_ToolBoxNG.Crafting;

public static class CraftingSeedExport
{
    public static void Export(string checkout, string connectionString, string locale, string expectedFingerprint)
    {
        string script = Path.Combine(checkout, "x64-server", "db", "export-seed.sh");
        string compose = Path.Combine(checkout, "x64-server", "compose", "docker-compose.yml");
        string seeds = Path.Combine(checkout, "x64-server", "db", "seeds");
        string bash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        if (!File.Exists(script) || !File.Exists(compose)) throw new InvalidOperationException("Choose the game checkout containing x64-server/db/export-seed.sh and its Compose file.");
        if (!File.Exists(bash)) throw new InvalidOperationException("Install Git for Windows to run the game's canonical seed exporter.");
        var settings = new MySqlConnectionStringBuilder(connectionString);
        if (settings.Database != "ep4_data" || settings.UserID != "root") throw new InvalidOperationException("The canonical Docker exporter requires root and ep4_data. Save is available with other database users; export those changes using export-seed.sh with its normal credentials.");
        string staging = Path.Combine(Path.GetTempPath(), "lc-crafting-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "ep4_data"));
        try {
            foreach (string table in CraftingRepository.Tables) File.Copy(Path.Combine(seeds, "ep4_data", table + ".sql"), Path.Combine(staging, "ep4_data", table + ".sql"));
            var repository = new CraftingRepository(connectionString, locale);
            repository.WithLockedCatalog((snapshot, identity) => {
                if (snapshot.Fingerprint() != expectedFingerprint) throw new InvalidOperationException("The database changed since this editor loaded it. Reload before exporting.");
                var verify = Start("docker", checkout);
                foreach (string argument in new[] { "compose", "-f", compose, "exec", "-T", "-e", "MYSQL_PWD", "mariadb", "mariadb", "--protocol=socket", "-uroot", "--batch", "--skip-column-names", "ep4_data", "-e", "SELECT CONCAT(@@hostname, ':', @@server_id, ':', DATABASE())" }) verify.ArgumentList.Add(argument);
                verify.Environment["MYSQL_PWD"] = settings.Password;
                if (Run(verify).Trim() != identity) throw new InvalidOperationException("The selected checkout's MariaDB is not the database being edited. No seed files were changed.");
                var process = Start(bash, checkout);
                process.ArgumentList.Add(script.Replace('\\', '/'));
                foreach (string table in CraftingRepository.Tables) process.ArgumentList.Add("ep4_data/" + table);
                process.Environment["LC_DB_CONN"] = "docker";
                process.Environment["LC_DB_COMPOSE_FILE"] = compose.Replace('\\', '/');
                process.Environment["LC_DB_SEEDS_DIR"] = staging.Replace('\\', '/');
                process.Environment["MARIADB_ROOT_PASSWORD"] = settings.Password;
                Run(process);
            });
            foreach (string table in CraftingRepository.Tables) File.Copy(Path.Combine(staging, "ep4_data", table + ".sql"), Path.Combine(seeds, "ep4_data", table + ".sql"), true);
        } finally { Directory.Delete(staging, true); }
    }
    private static ProcessStartInfo Start(string executable, string directory) => new(executable) {
        WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    private static string Run(ProcessStartInfo info)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the seed exporter.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000)) { process.Kill(true); throw new TimeoutException("Seed export timed out. Authored seeds were not replaced."); }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new InvalidOperationException("Seed export failed: " + error.Result + "\n" + output.Result);
        return output.Result;
    }
}
