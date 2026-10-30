using System.Data;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.ContentRecording;

static class Program
{
    static int checks;
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    sealed class Source
    {
        public readonly List<string> Queries = [];
        public bool FailFortune;
        public int ItemCount = 1;
        public DataTable Query(string charset, string sql)
        {
            Queries.Add(sql);
            if (FailFortune && sql.Contains("t_fortune_data")) throw new InvalidOperationException("Fixture source read failed");
            var result = new DataTable();
            if (sql.Contains("LIMIT 0"))
            {
                foreach (var type in Defs.StringTypes.Values)
                foreach (string nation in new[] { "usa", "ger" })
                foreach (string c in type.Columns.Select((name, i) => i == 0 ? name : name + "_" + nation))
                    if (!result.Columns.Contains(c)) result.Columns.Add(c, typeof(object));
                return result;
            }
            if (sql.Contains("COUNT(*)")) { result.Columns.Add("COUNT(*)", typeof(int)); result.Rows.Add(1); return result; }
            string columns = Regex.Match(sql, @"SELECT\s+(?:DISTINCT\s+)?(.*?)\s+FROM", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
            if (columns.Length == 0) throw new InvalidOperationException("Unexpected fixture query: " + sql);
            foreach (string c in columns.Split(','))
            {
                string name = Regex.Split(c.Trim(), @"\s+(?:AS\s+)?", RegexOptions.IgnoreCase).Last().Split('.').Last();
                if (!result.Columns.Contains(name)) result.Columns.Add(name, typeof(object));
            }
            // Dependent tables are deliberately empty unless a test needs a row.
            if (sql.Contains("t_mapinfo") || sql.Contains("t_npc_regen") || sql.Contains("t_affinity") || sql.Contains("t_skillLevel") ||
                sql.Contains("t_npc WHERE a_flag") || sql.Contains("t_npc WHERE a_enable=1 AND a_channel_flag")) return result;
            var row = result.NewRow();
            foreach (DataColumn c in result.Columns)
            {
                string name = c.ColumnName;
                object value = 0;
                if (name is "a_index" or "a_keeper_idx" or "itemindex" or "a_ctid" or "a_item_idx") value = 7;
                if (name is "a_name" or "a_name_usa" or "a_name_ger") value = "Herbe é";
                if (name == "a_descr_usa" || name == "a_descr_ger") value = "A leaf";
                if (name.StartsWith("a_file_") || name.StartsWith("a_effect_") || name.Contains("effect_name") || name.StartsWith("a_motion_") || name.StartsWith("a_cd_")) value = "";
                if (name.StartsWith("a_cd_") && !new[] { "a_cd_ra", "a_cd_re", "a_cd_sa", "a_cd_fa", "a_cd_fe0", "a_cd_fe1", "a_cd_fe2", "a_cd_ra2", "a_cd_re2", "a_cd_sa2", "a_cd_fa2", "a_cd_fe3", "a_cd_fe4", "a_cd_fe5", "a_cd_fe_after" }.Contains(name)) value = 0;
                if (name.StartsWith("a_fireEffect") || name.StartsWith("a_skill0") || name.StartsWith("a_skill1")) value = "";
                if (name is "a_color" or "a_bgcolor") value = "0";
                if (name == "a_flag" && sql.Contains("t_item WHERE")) value = 0x0102030405060708L;
                if (name == "a_need_sskill2") value = 123;
                if (name == "a_need_sskill_level2") value = 4;
                if (name == "a_texture_id") value = 21;
                if (name == "a_castle_war") value = 3;
                if (name == "a_fireRange2") value = 12.5f;
                row[c] = value;
            }
            result.Rows.Add(row);
            if (sql.Contains(".t_item WHERE", StringComparison.OrdinalIgnoreCase) || sql.Contains(".t_item ORDER", StringComparison.OrdinalIgnoreCase))
            for (int i = 1; i < ItemCount; i++)
            {
                var extra = result.NewRow();
                extra.ItemArray = (object[])row.ItemArray.Clone();
                extra["a_index"] = 7 + i;
                result.Rows.Add(extra);
            }
            return result;
        }
    }

    static string PathFor(IReadOnlyDictionary<string, string> files, string name) => files.Single(x => x.Key.EndsWith("/" + name, StringComparison.Ordinal)).Value;

    [STAThread] static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "lc-client-content-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string oldDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = root;
        try
        {
            using var host = new LastChaos_ToolBoxNG.Main();
            host.pSettings.ClientPath = Path.Combine(root, "runtime");
            host.pSettings.WorkLocale = "usa";
            host.pSettings.NationSupported = ["USA", "GER", "THAI", "JPN"];
            host.pSettings.DBData = "must_not_query_live";
            host.pSettings.DBHost = "invalid.offline.test";
            host.pSettings.ExportToLocal = true;
            host.pSettings.Exporter = new() { ["ITEMS"] = "false" };
            Directory.CreateDirectory(host.pSettings.ClientPath);
            File.WriteAllText(Path.Combine(host.pSettings.ClientPath, "unchanged.txt"), "untouched");
            File.WriteAllText("Settings.ini", "fixture settings must stay unchanged");

            var fishing = ContentClientExports.Plan(["ep4_data.t_fishing_region", "ep4_data.t_gathering_material", "ep4_db.t_item"], "usa", ["USA"]);
            Check(fishing.Count == 0, "server-delivered profession tables and other schemas generate no client files");
            string configuredClient = host.pSettings.ClientPath;
            host.pSettings.ClientPath = "";
            Check(ContentClientExports.GenerateAsync(host, (_, _) => throw new Exception("Unexpected read"), ["ep4_data.t_gathering_material"], "").GetAwaiter().GetResult().Count == 0,
                "profession-only recording needs no configured client or output directory");
            host.pSettings.ClientPath = configuredClient;
            var npc = ContentClientExports.Plan(["ep4_data.t_npc"], "usa", ["USA"]);
            Check(new[] { "mobAll.lod", "npc_channel.bin", "ZoneFlag.lod", "Map.dta", "npchelp.lod", "mobhelp.lod", "strNPCName_us.lod" }.All(x => npc.Any(p => p.FileName == x)), "NPC dependency fanout covers model, channels, flags, maps, help and strings");
            Check(ContentClientExports.Plan(["ep4_data.t_shopitem"], "usa", []).Single().FileName == "ShopAll.lod", "shop item dependency uses shipping file");
            Check(ContentClientExports.Plan(["ep4_data.t_magiclevel"], "usa", []).Single().FileName == "Skills.lod", "magic level dependency refreshes skills");
            Check(ContentClientExports.Plan(["ep4_data.t_quest"], "usa", []).Select(x => x.FileName).Order().SequenceEqual(new[] { "questAll.lod", "strQuest_us.lod" }.Order()), "quest data and string dependencies");

            var source = new Source();
            var files = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_item"], Path.Combine(root, "items")).GetAwaiter().GetResult();
            byte[] items = File.ReadAllBytes(PathFor(files, "ItemAll.lod"));
            // These offsets/sizes come from shipping packed stItem in Common/header/def_item.h.
            Check(items.Length == 4 + 449 && BitConverter.ToInt32(items, 0) == 1, "ItemAll contains one complete shipping packed stItem");
            Check(BitConverter.ToInt64(items, 4 + 20) == 0x0102030405060708L, "item flags retain 64-bit width");
            Check(BitConverter.ToInt32(items, 4 + 128) == 123 && BitConverter.ToInt32(items, 4 + 132) == 4 && BitConverter.ToInt32(items, 4 + 136) == 21, "second special-skill requirement preserves following item offsets");
            Check(BitConverter.ToInt32(items, 4 + 444) == 1 && items[^1] == 3, "fortune flag and final castle byte preserve packed layout");
            Check(source.Queries.All(q => !q.Contains("must_not_query_live")), "all writer reads use canonical snapshot queries");
            Check(source.Queries.Count(q => q.Contains("t_fortune_data")) == 1 && source.Queries.All(q => !q.Contains("COUNT(*)")), "item fortune presence is batched into one immutable source read");
            var manyItems = new Source { ItemCount = 100 };
            var manyFiles = ContentClientExports.GenerateAsync(host, manyItems.Query, ["ep4_data.t_item"], Path.Combine(root, "many-items")).GetAwaiter().GetResult();
            byte[] many = File.ReadAllBytes(PathFor(manyFiles, "ItemAll.lod"));
            Check(many.Length == 4 + 100 * 449 && BitConverter.ToInt32(many, 4 + 444) == 1 && BitConverter.ToInt32(many, 4 + 449 + 444) == 0 &&
                manyItems.Queries.Count(q => q.Contains("t_fortune_data")) == 1, "100 items retain individual fortune flags using one constant-count source query");
            using (var reader = new BinaryReader(File.OpenRead(PathFor(files, "strItem_us.lod"))))
            {
                Check(reader.ReadInt32() == 1 && reader.ReadInt32() == 7 && reader.ReadInt32() == 7, "string header contains count, last ID and row ID");
                int bytes = reader.ReadInt32();
                Check(Encoding.UTF8.GetString(reader.ReadBytes(bytes)) == "Herbe é" && bytes == 8, "strings preserve UTF-8 bytes and byte length");
                Check(Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())) == "A leaf" && reader.BaseStream.Position == reader.BaseStream.Length, "string row has expected description and no trailing fields");
            }
            Check(files.Keys.Any(x => x.Contains("Local/de/")) && files.Keys.All(x => !x.Contains("Local/th/") && !x.Contains("...")), "supported locale strings emitted; missing or unmapped locale schema skipped");
            using (var reader = new BinaryReader(File.OpenRead(PathFor(files, "ShopAll.lod"))))
            {
                Check(reader.ReadInt32() == 8 && reader.ReadInt32() == 7, "shop header allocates through sparse keeper ID");
                reader.ReadBytes(reader.ReadInt32()); reader.ReadInt32(); reader.ReadInt32();
                Check(reader.ReadInt32() == 1 && reader.ReadInt32() == 7 && reader.BaseStream.Position == reader.BaseStream.Length, "shipping shop record has a name and unpaired item IDs");
            }

            var skillFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_magiclevel"], Path.Combine(root, "skills")).GetAwaiter().GetResult();
            byte[] skill = File.ReadAllBytes(PathFor(skillFiles, "Skills.lod"));
            Check(BitConverter.ToInt32(skill, 0) == 7 && BitConverter.ToSingle(skill, 4 + 34) == 12.5f, "shipping skill header and second fire range");
            Check(BitConverter.ToInt32(skill, skill.Length - 8) == -9999, "shipping skill end marker");
            int sum = 0, previous = 0;
            for (int i = 0; i < skill.Length - 4; i += 20)
            {
                byte[] word = BitConverter.GetBytes(previous);
                Array.Copy(skill, i, word, 0, Math.Min(4, skill.Length - 4 - i));
                previous = BitConverter.ToInt32(word); sum = unchecked(sum + previous);
            }
            Check(sum == BitConverter.ToInt32(skill, skill.Length - 4), "shipping skill security checksum matches FileSecure.cpp");
            var npcFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_npc"], Path.Combine(root, "npc")).GetAwaiter().GetResult();
            Check(new FileInfo(PathFor(npcFiles, "mobAll.lod")).Length == 4 + 947, "shipping NPC packed record includes effects and all eleven animation/effect names");
            var questFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_quest"], Path.Combine(root, "quest")).GetAwaiter().GetResult();
            Check(new FileInfo(PathFor(questFiles, "questAll.lod")).Length == 4 + 396, "shipping quest record retains both penalty bounds and 64-bit reward values");
            var titleFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_title"], Path.Combine(root, "titles")).GetAwaiter().GetResult();
            Check(new FileInfo(PathFor(titleFiles, "titletool.lod")).Length == 4 + 254, "shipping title packed record retains five explicit option values");
            var rareFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_rareoption"], Path.Combine(root, "rare")).GetAwaiter().GetResult();
            Check(new FileInfo(PathFor(rareFiles, "RareOption.lod")).Length == 4 + 108, "shipping rare option record retains its type field");
            var comboFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_missioncase"], Path.Combine(root, "combo")).GetAwaiter().GetResult();
            Check(new FileInfo(PathFor(comboFiles, "combo.lod")).Length == 4 + 25, "shipping combo penalty indicator is one byte");
            var catalogFiles = ContentClientExports.GenerateAsync(host, source.Query, ["ep4_data.t_catalog"], Path.Combine(root, "catalog")).GetAwaiter().GetResult();
            using (var reader = new BinaryReader(File.OpenRead(PathFor(catalogFiles, "catalog.lod"))))
            {
                Check(reader.ReadInt32() == 1 && reader.ReadInt32() == 7, "cash catalog uses shipping path and count/index header");
                reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32();
                Check(reader.ReadInt32() == 21, "G_USA cash catalog includes texture coordinates before text");
                reader.ReadInt32(); reader.ReadInt32(); reader.ReadBytes(reader.ReadInt32()); reader.ReadBytes(reader.ReadInt32());
                Check(reader.ReadInt32() == 1 && reader.BaseStream.Length - reader.BaseStream.Position == 20, "cash catalog ends with five-field item rows without disabled custom flags");
            }
            Check(File.ReadAllText("Settings.ini") == "fixture settings must stay unchanged" && host.pSettings.ExportToLocal && host.pSettings.Exporter["ITEMS"] == "false", "recording preserves settings and manual export selections");
            Check(Directory.GetFiles(host.pSettings.ClientPath, "*", SearchOption.AllDirectories).Length == 1, "all generated writes remain outside configured runtime");
            bool failure = false;
            try { ContentClientExports.GenerateAsync(host, new Source { FailFortune = true }.Query, ["ep4_data.t_item"], Path.Combine(root, "failure")).GetAwaiter().GetResult(); }
            catch (InvalidOperationException e) when (e.Message.Contains("Fixture source read failed")) { failure = true; }
            Check(failure, "source query failure aborts capture instead of returning partial output");

            using var manual = new Exporter(host);
            var method = typeof(Exporter).GetMethod("ExportDataFilePath", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Check((string)method.Invoke(manual, ["Data", "Items"])! == Path.Combine("Data", "Items.lod"), "ordinary exporter retains its original filename convention");
            Console.WriteLine($"{checks} content client checks passed; no live database or running client used.");
            return 0;
        }
        finally { Environment.CurrentDirectory = oldDirectory; Console.WriteLine("Fixture directory: " + root); }
    }
}
