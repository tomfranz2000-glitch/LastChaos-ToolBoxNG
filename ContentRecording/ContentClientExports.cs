namespace LastChaos_ToolBoxNG.ContentRecording;

public sealed record ContentClientExport(string Writer, string FileName, string RelativeClientPath,
    string? Nation = null, Defs.StringType? StringType = null);

/// <summary>Explicit dependency registry for shipping client files, independent of exporter UI preferences.</summary>
public static class ContentClientExports
{
    private sealed record DataExport(string Writer, string FileName, string[] Tables);
    private static readonly DataExport[] DataExports =
    [
        new("ITEMS", "ItemAll.lod", ["t_item", "t_fortune_data"]),
        new("MOONSTONES", "MoonStone.lod", ["t_moonstone_reward"]),
        new("LACARETTES", "lacarette.lod", ["t_lacarette"]),
        new("CATALOGS", "catalog.lod", ["t_catalog", "t_ct_item"]),
        new("SMC", "smc.lod", ["t_item"]),
        new("MONSTERCOMBO", "combo.lod", ["t_missioncase"]),
        new("ITEMEXCHANGES", "item_exchange.lod", ["t_item_exchange"]),
        new("SPECIALSKILLS", "sskill.lod", ["t_special_skill"]),
        new("SKILLS", "Skills.lod", ["t_skill", "t_skilllevel", "t_magic", "t_magiclevel"]),
        new("EQUIPMENTEXCHANGES", "change_item.bin", ["t_equipment_exchange"]),
        new("NPCCHANNELS", "npc_channel.bin", ["t_npc"]),
        new("ACTIONS", "Actions.lod", ["t_action"]),
        new("SKILLTREES", "Skill_tree.bin", ["t_skill"]),
        new("SHOPS", "ShopAll.lod", ["t_shop", "t_shopitem", "t_item"]),
        new("QUESTS", "questAll.lod", ["t_quest"]),
        new("RAREOPTIONS", "RareOption.lod", ["t_rareoption"]),
        new("BIGPETS", "bigpet.lod", ["t_attack_pet", "t_attack_pet_ev", "t_attack_pet_exp"]),
        new("MOBS", "mobAll.lod", ["t_npc"]),
        new("OPTIONS", "Option.lod", ["t_option"]),
        new("TITLES", "titletool.lod", ["t_title"]),
        new("MAPS", "Map.dta", ["t_mapinfo", "t_npc_regen", "t_npc", "t_shop", "t_affinity_npc"]),
        new("NPCHELP", "npchelp.lod", ["t_npc_regen", "t_npc", "t_shop", "t_affinity_npc"]),
        new("AFFINITIES", "Affinity.lod", ["t_affinity", "t_affinity_npc", "t_affinity_work", "t_affinity_reward_item"]),
        new("DATAITEMCOLLECTION", "itemCollection.lod", ["t_item_collection"]),
        new("ITEMFORTUNE", "itemFortune.lod", ["t_fortune_data"]),
        new("MOBHELP", "mobhelp.lod", ["t_npc_regen", "t_npc"]),
        new("DATASETITEM", "setItemALL.lod", ["t_set_item"]),
        new("ZONEFLAG", "ZoneFlag.lod", ["t_npc"])
    ];

    public static IReadOnlyList<ContentClientExport> Plan(IReadOnlyCollection<string> changedCanonicalTables,
        string workLocale, IEnumerable<string>? supportedNations)
    {
        var changed = new HashSet<string>(changedCanonicalTables.Select(CanonicalDataTable).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        string nation = workLocale.Trim().ToUpperInvariant();
        var nations = (supportedNations ?? []).Append(nation).Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var result = new List<ContentClientExport>();
        foreach (var data in DataExports.Where(x => x.Tables.Any(changed.Contains)))
        {
            string? dataNation = data.Writer switch { "SHOPS" => "USA", "CATALOGS" => nation, _ => null };
            if (data.Writer == "CATALOGS") ValidateNation(nation);
            result.Add(new(data.Writer, data.FileName, "Data/" + data.FileName, dataNation));
        }
        foreach (var type in Defs.StringTypes.Values.Where(x => changed.Contains(x.TableName)))
        foreach (string locale in nations)
        {
            if (!Defs.NationsCharsetsNPostfix.TryGetValue(locale, out var encoding) || encoding.Postfix == "...")
            {
                if (locale == nation) ValidateNation(locale);
                continue;
            }
            string file = type.FileName + "_" + encoding.Postfix + ".lod";
            result.Add(new("STRINGS", file, $"Local/{encoding.Postfix}/String/{file}", locale, type));
        }
        return result;
    }

    public static async Task<IReadOnlyDictionary<string, string>> GenerateAsync(Main host,
        Func<string, string, DataTable> query, IReadOnlyCollection<string> changedCanonicalTables,
        string stagingClientRoot, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(query);
        string locale = host.pSettings.WorkLocale;
        var plans = Plan(changedCanonicalTables, locale, host.pSettings.NationSupported);
        if (plans.Count == 0) return new Dictionary<string, string>();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingClientRoot));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("The client capture directory must be empty.");
        if (string.IsNullOrWhiteSpace(host.pSettings.ClientPath))
            throw new InvalidOperationException("Set the toolbox Client Path before recording changes that need client data.");
        string input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(host.pSettings.ClientPath));
        if (root.Equals(input, StringComparison.OrdinalIgnoreCase) || root.StartsWith(input + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Content recording needs a temporary output directory outside the configured client.");
        var context = new RecordingExportContext(input, root, query, host.pSettings.LogVerbose, progress);
        using var exporter = new Exporter(host, context);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var schemas = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            ValidateRelativePath(plan.RelativeClientPath);
            if (plan.StringType is { } type)
            {
                if (!schemas.TryGetValue(type.TableName, out var columns))
                {
                    using DataTable schema = query("utf8", $"SELECT * FROM ep4_data.{type.TableName} LIMIT 0;")
                        ?? throw new InvalidOperationException($"No schema returned for {type.TableName}.");
                    columns = schema.Columns.Cast<DataColumn>().Select(x => x.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    schemas.Add(type.TableName, columns);
                }
                var required = type.Columns.Select((name, i) => i == 0 ? name : name + "_" + plan.Nation!.ToLowerInvariant());
                if (required.Any(name => !columns.Contains(name)))
                {
                    if (plan.Nation!.Equals(locale, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{type.TableName} does not support the working language {locale}; its client strings cannot be recorded.");
                    progress?.Report($"Skipping unavailable {plan.Nation} strings for {type.TableName}.");
                    continue;
                }
            }
            progress?.Report("Capturing client data: " + plan.RelativeClientPath);
            await exporter.GenerateRecordedFileAsync(plan).ConfigureAwait(false);
            string output = Path.GetFullPath(Path.Combine(root, plan.RelativeClientPath));
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A client writer escaped its capture directory.");
            result.Add("x64-client/assets/" + plan.RelativeClientPath, output);
        }
        return result;
    }

    private static string? CanonicalDataTable(string value)
    {
        string[] parts = value.Replace("`", "", StringComparison.Ordinal).Split('.');
        return parts.Length == 1 ? parts[0] : parts.Length == 2 && parts[0].Equals("ep4_data", StringComparison.OrdinalIgnoreCase) ? parts[1] : null;
    }

    private static void ValidateNation(string nation)
    {
        if (!Defs.NationsCharsetsNPostfix.TryGetValue(nation, out var data) || data.Postfix == "...")
            throw new InvalidOperationException($"No shipping client locale mapping exists for '{nation}'. Check WorkLocale.");
    }

    private static void ValidateRelativePath(string path)
    {
        if (path.Length > 180 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(x => x is "" or "." or "..") ||
            !(path.StartsWith("Data/", StringComparison.Ordinal) || path.StartsWith("Local/", StringComparison.Ordinal)) ||
            !(path.EndsWith(".lod", StringComparison.Ordinal) || path.EndsWith(".bin", StringComparison.Ordinal) || path == "Data/Map.dta"))
            throw new InvalidOperationException("Invalid generated client content path: " + path);
    }
}
