using System.Text;
using LastChaos_ToolBoxNG.Professions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LastChaos_ToolBoxNG.Crafting;

public enum Profession { Smithing, Alchemy, Cooking }
public sealed class Ingredient { public int ItemId { get; set; } public int Quantity { get; set; } = 1; }
public sealed class TeachingItem { public int ItemId { get; set; } }
public sealed class Bonus
{
    public int Effect { get; set; } = 4;
    public int Amount { get; set; } = 1;
    public int Rank { get; set; } = 3;
    [JsonIgnore] public decimal DisplayAmount { get => MasteryBonusUnits.Display(Effect, Amount); set => Amount = MasteryBonusUnits.Store(Effect, value); }
    [JsonIgnore] public string DisplayUnit => MasteryBonusUnits.Unit(Effect);
}
public sealed class BonusOutput
{
    public int ItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal ChancePercent { get; set; } = 1;
}
public sealed class Recipe
{
    public int Id { get; set; }
    public Profession Profession { get; set; }
    public int OutputItem { get; set; }
    public int OutputQuantity { get; set; } = 1;
    public int CraftTimeMs { get; set; } = 3000;
    public int RequiredSkill { get; set; } = 1;
    public int NoSkillUp { get; set; } = 30;
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RequiresUnlock { get; set; }
    public int MasteryXp { get; set; } = 10;
    public int MasteryRequired { get; set; } = 300;
    public int BronzeRequired { get; set; } = 50;
    public int SilverRequired { get; set; } = 150;
    public List<Ingredient> Ingredients { get; set; } = [];
    public List<TeachingItem> Manuals { get; set; } = [];
    public List<Bonus> Bonuses { get; set; } = [];
    public List<BonusOutput> BonusOutputs { get; set; } = [];
    [JsonIgnore] public string Name { get; set; } = "Choose an output item";
}
public sealed class GatheringResource
{
    public int ItemId { get; set; }
    public int Profession { get; set; }
    public int MasteryXp { get; set; }
    public int BronzeRequired { get; set; }
    public int SilverRequired { get; set; }
    public int MasteryRequired { get; set; }
    [JsonIgnore] public bool Enabled { get; set; } = true;
    public List<Bonus> Bonuses { get; set; } = [];
    [JsonIgnore] public string Name { get; set; } = "";
    [JsonIgnore] public string ProfessionName => Profession switch { 1=>"Mining",2=>"Herbalism",3=>"Fishing",4=>"Wave Absorption",_=>"Unknown" };
}
public sealed class RankPerk
{
    public int Type { get; set; }
    public bool Enabled { get; set; } = true;
}
public static class ArtisanPerks
{
    public const int UpgradeBreakProtection = 1;
    public static IReadOnlyDictionary<int,string> Names { get; } = new Dictionary<int,string> {
        [UpgradeBreakProtection] = "Equipment cannot break during upgrades"
    };
    public static IEnumerable<string> Active(IEnumerable<MastersRank> ranks) => ranks.SelectMany(r=>r.Perks)
        .Where(p=>p.Enabled && Names.ContainsKey(p.Type)).Select(p=>p.Type).Distinct().Order().Select(type=>Names[type]);
}
public sealed class MastersRank
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = "New Artisan Rank";
    public int RequiredMastered { get; set; } = 1;
    public string Emblem { get; set; } = "AchievementCategory_2001.tex";
    public bool Enabled { get; set; } = true;
    public List<Bonus> Bonuses { get; set; } = [];
    public List<RankPerk> Perks { get; set; } = [];
}
public sealed record CraftingItem(int Id, string Name, bool Enabled, int Type, int Subtype,
    long Flags, int StackLimit, int Texture, int Row, int Column, bool Rebirth, int Wearing = -1)
{
    public bool Manual => Type == 2 && Subtype is 1 or 2 or 4;
    public bool Stackable => (Flags & 1) != 0;
    public bool Timed => (Flags & (1L << 16)) != 0;
}
public sealed class CraftingCatalog
{
    public int SkillCap { get; set; } = 50;
    public List<Recipe> Recipes { get; set; } = [];
    public List<MastersRank> Ranks { get; set; } = [];
    public List<GatheringResource> Gathering { get; set; } = [];
    public ProfessionContent Professions { get; set; } = new();
    [JsonIgnore] public Dictionary<int, CraftingItem> Items { get; set; } = [];
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public string Fingerprint() {
        var copy=Copy(this);
        static List<Bonus> Ordered(List<Bonus> bonuses)=>bonuses.OrderBy(b=>b.Rank).ThenBy(b=>b.Effect).ToList();
        foreach(var r in copy.Recipes) r.Bonuses=Ordered(r.Bonuses);
        foreach(var r in copy.Gathering) r.Bonuses=Ordered(r.Bonuses);
        foreach(var r in copy.Ranks) { r.Bonuses=Ordered(r.Bonuses);r.Perks=r.Perks.OrderBy(p=>p.Type).ToList(); }
        return JsonSerializer.Serialize(new {copy.SkillCap,Recipes=copy.Recipes.OrderBy(r=>r.Id),Ranks=copy.Ranks.OrderBy(r=>r.Id),Gathering=copy.Gathering.OrderBy(r=>r.ItemId),Professions=copy.Professions.Canonical()});
    }
    public string ItemName(int id) => Items.TryGetValue(id, out var item) ? item.Name : $"Missing item #{id}";
    public void UpdateNames() { foreach (var r in Recipes) r.Name = ItemName(r.OutputItem); foreach(var r in Gathering) { r.Name=ItemName(r.ItemId);r.Enabled=IsGatheringAvailable(r.ItemId); } }
    public bool IsGatheringAvailable(int item) {
        bool ParentOn(string table,string key,int id)=>Professions.Rows(table).Any(r=>r.Int(key)==id&&r.Enabled);
        return Professions.Rows("t_gathering_node").Any(r=>r.Enabled&&r.Int("a_profession")==1&&r.Int("a_item_index")==item)
            || Professions.Rows("t_gathering_node_loot").Any(r=>r.Enabled&&r.Int("a_item_index")==item&&ParentOn("t_gathering_node","a_index",r.Int("a_node_index")))
            || Professions.Rows("t_herb_loot").Any(r=>r.Enabled&&r.Int("a_item_index")==item&&ParentOn("t_herb_population","a_index",r.Int("a_population_index")))
            || Professions.Rows("t_fishing_loot").Any(r=>r.Enabled&&r.Int("a_item_index")==item&&ParentOn("t_fishing_water","a_index",r.Int("a_water_index")));
    }
    private IEnumerable<string> ProfessionUses(int item) {
        foreach(var table in ProfessionSchema.Tables)foreach(var row in Professions.Rows(table.Name))foreach(var field in table.Fields.Where(f=>f.Kind==FieldKind.Item))
            if(row.Int(field.Column)==item)yield return table.Title+" #"+row.Key(table)+" — "+field.Label+(row.Enabled?"":" (retired)");
    }
    public IEnumerable<string> WhereUsed(int item) => Recipes.SelectMany(r => {
        var uses = new List<string>();
        if (r.OutputItem == item) uses.Add("output");
        if (r.Ingredients.Any(i => i.ItemId == item)) uses.Add("ingredient");
        if (r.Manuals.Any(i => i.ItemId == item)) uses.Add("teaches");
        if (r.BonusOutputs.Any(i => i.ItemId == item)) uses.Add("bonus output");
        return uses.Select(u => $"#{r.Id} {ItemName(r.OutputItem)} — {u}{(r.Enabled ? "" : " (retired)")}");
    }).Concat(Gathering.Where(r=>r.ItemId==item).Select(r=>$"{r.ProfessionName} resource #{r.ItemId} — mastery")).Concat(ProfessionUses(item));
}
public sealed record CraftingIssue(bool Error, string Target, string Message)
{
    public override string ToString() => $"{(Error ? "ERROR" : "NOTE")} {Target}: {Message}";
}

// Mirrors the game's CraftingPrototypeCatalog/Ranks/Mastery contracts. Limits
// and effect meanings are engine rules; all authored rewards remain database data.
public static class CraftingRules
{
    public const int MaxSkillLevel = 65535, MaxRecipes = 4096, MaxRanks = 64;
    public static readonly Dictionary<int, string> Effects = new[] {
        "Strength", "Dexterity", "Intelligence", "Constitution", "Maximum HP", "Maximum MP",
        "Physical Attack", "Melee Attack", "Ranged Attack", "Melee Accuracy", "Ranged Accuracy",
        "Physical Defense", "Melee Defense", "Ranged Defense", "Melee Evasion", "Ranged Evasion",
        "Magic Attack", "Magic Accuracy", "Magic Defense", "Magic Evasion", "All Attack", "All Accuracy",
        "All Defense", "All Evasion"
    }.Select((s, i) => (s, i)).ToDictionary(x => x.i, x => x.s);
    static CraftingRules() {
        Effects[102] = "HP Regeneration"; Effects[103] = "MP Regeneration";
        Effects[MasteryBonusUnits.MovementSpeed] = "Movement Speed";
        Effects[MasteryBonusUnits.AttackIntervalReduction] = "Attack Interval Reduction";
        Effects[MasteryBonusUnits.PveDoubleDamageChance] = "PvE Double Damage Chance";
    }
    public static IEnumerable<int> Channels(int effect) => effect switch {
        >= 0 and <= 5 => [effect], 6 => [6, 7], 7 => [6], 8 => [7], 9 => [9], 10 => [10],
        11 => [12, 13], 12 => [12], 13 => [13], 14 => [15], 15 => [16], 16 => [8],
        17 => [11], 18 => [14], 19 => [17], 20 => [6, 7, 8], 21 => [9, 10, 11],
        22 => [12, 13, 14], 23 => [15, 16, 17], 102 => [18], 103 => [19],
        MasteryBonusUnits.MovementSpeed => [20], MasteryBonusUnits.AttackIntervalReduction => [21], MasteryBonusUnits.PveDoubleDamageChance => [22], _ => []
    };
    public static int Chance(Recipe r, int skill, int skillCap) => skill < r.RequiredSkill || skill >= skillCap || skill >= r.NoSkillUp ? 0 :
        skill >= r.RequiredSkill + (2L * (r.NoSkillUp - r.RequiredSkill) + 2) / 3 ? 20 :
        skill >= r.RequiredSkill + ((long)r.NoSkillUp - r.RequiredSkill + 2) / 3 ? 60 : 100;
    public static string Training(Recipe r, int skillCap)
    {
        List<string> bands = [];
        for (int start = 1; start <= skillCap;) {
            int end = start, chance = Chance(r, start, skillCap);
            while (end < skillCap && Chance(r, end + 1, skillCap) == chance) end++;
            bands.Add($"{chance}% at {start}–{end}"); start = end + 1;
        }
        return string.Join("  |  ", bands);
    }
    public static string MasteryEstimate(Recipe r)
    {
        if (r.MasteryRequired == 0) return "Mastery disabled";
        if (r.MasteryXp <= 0) return "No XP awarded: mastery cannot progress by crafting.";
        long crafts = ((long)r.MasteryRequired + r.MasteryXp - 1) / r.MasteryXp;
        return $"From zero: {crafts:N0} crafts • {crafts * r.CraftTimeMs / 1000m:N1} seconds\r\n" +
            string.Join(", ", r.Ingredients.Select(i => $"item {i.ItemId} × {crafts * i.Quantity:N0}"));
    }
    public static string TierName(int rank) => rank switch { 1=>"Bronze",2=>"Silver",3=>"Gold",_=>"Invalid" };
    public static bool ValidThresholds(int bronze,int silver,int gold) => bronze>0 && (long)silver-bronze>bronze && (long)gold-silver>(long)silver-bronze;
    public static string Rewards(IEnumerable<Bonus> bonuses) => string.Join(", ", bonuses.GroupBy(b => b.Effect)
        .Select(g => MasteryBonusUnits.Format(g.Key, g.Sum(b => (long)b.Amount))));
    public static List<CraftingIssue> Validate(CraftingCatalog c)
    {
        List<CraftingIssue> issues = [];
        void Error(string t, string m) => issues.Add(new(true, t, m));
        void Note(string t, string m) => issues.Add(new(false, t, m));
        var totals = new long[MasteryBonusUnits.ChannelCount];
        void Bonuses(string t, List<Bonus> bs,bool tiered=false) {
            if (tiered ? bs.Any(b=>b.Rank is <1 or >3) || bs.GroupBy(b=>b.Rank).Any(g=>g.Count()>8 || g.Select(b=>b.Effect).Distinct().Count()!=g.Count())
                : bs.Count > 8 || bs.Select(b => b.Effect).Distinct().Count() != bs.Count) Error(t, "Use at most eight distinct bonus effects per rank.");
            foreach (var b in bs) {
                if (!Effects.ContainsKey(b.Effect) || b.Amount < 1 || b.Amount > MasteryBonusUnits.Maximum(b.Effect))
                    Error(t, "Bonuses need a supported effect and positive amount within its limit: " + MasteryBonusUnits.Unit(b.Effect) + "; maximum " + MasteryBonusUnits.Display(b.Effect, MasteryBonusUnits.Maximum(b.Effect)).ToString("0.##") + ".");
                foreach (int channel in Channels(b.Effect)) totals[channel] += b.Amount;
            }
        }
        void Item(string t, int id, int quantity, bool output) {
            if (!c.Items.TryGetValue(id, out var i) || !i.Enabled) { Error(t, $"Item {id} is missing or disabled."); return; }
            if (id == 19) Error(t, "Gold cannot be used as a crafting ingredient or output.");
            if (output && (i.Rebirth || (i.Stackable ? quantity > i.StackLimit : quantity != 1))) Error(t, $"Output {id} has an unsupported type or quantity/stack limit.");
        }
        if (c.SkillCap is < 1 or > MaxSkillLevel) Error("Settings", "Maximum crafting skill must be 1–65535.");
        if (c.Recipes.Count > MaxRecipes || c.Recipes.Select(r => r.Id).Distinct().Count() != c.Recipes.Count) Error("Catalogue", "Recipe limit exceeded or duplicate IDs.");
        foreach (var r in c.Recipes) {
            string t = $"Recipe {r.Id}";
            if (r.Id <= 0 || !Enum.IsDefined(r.Profession) || r.OutputItem <= 0 || r.OutputQuantity <= 0 || r.SortOrder < 0) Error(t, "Invalid identity, profession, output or display order.");
            if (r.CraftTimeMs is < 1 or > 600000) Error(t, "Craft duration must be 0.001–600 seconds.");
            if (r.RequiredSkill is < 1 or > MaxSkillLevel || r.NoSkillUp <= r.RequiredSkill || r.NoSkillUp > 1000000) Error(t, "Minimum skill must be 1–65535; no-gain skill must be greater (maximum 1000000).");
            if (r.Enabled && r.RequiredSkill > c.SkillCap) Note(t, $"Requires skill {r.RequiredSkill}, above the configured cap {c.SkillCap}. New characters cannot reach this requirement yet.");
            if (r.Ingredients.Count > 10 || (r.Enabled && r.Ingredients.Count == 0) || r.Ingredients.Any(i => i.ItemId <= 0 || i.Quantity <= 0 || i.ItemId == r.OutputItem) || r.Ingredients.Select(i => i.ItemId).Distinct().Count() != r.Ingredients.Count) Error(t, "Use 1–10 different ingredients with positive quantities; output cannot also be an ingredient.");
            if (r.Manuals.Count > 8 || r.Manuals.Any(i => i.ItemId <= 0) || r.Manuals.Select(i => i.ItemId).Distinct().Count() != r.Manuals.Count || (!r.RequiresUnlock && r.Manuals.Count != 0) || (r.Enabled && r.RequiresUnlock && r.Manuals.Count == 0)) Error(t, "A learned recipe needs 1–8 distinct teaching items; automatic recipes have none.");
            if (r.MasteryXp < 0 || !ValidThresholds(r.BronzeRequired,r.SilverRequired,r.MasteryRequired)) Error(t, "All three mastery targets are required. Each tier must cost more additional XP than the previous tier.");
            if (r.MasteryRequired > 0 && r.MasteryXp == 0) Note(t, "Mastery is enabled but this recipe awards no XP.");
            Bonuses(t, r.Bonuses,true);
            if (r.BonusOutputs.Count > 8 || r.BonusOutputs.Select(b => b.ItemId).Distinct().Count() != r.BonusOutputs.Count || r.BonusOutputs.Sum(b => Math.Clamp(b.ChancePercent, 0m, 101m)) > 100 || r.BonusOutputs.Any(b => b.ItemId <= 0 || b.Quantity <= 0 || b.ChancePercent <= 0 || b.ChancePercent > 100 || decimal.Round(b.ChancePercent, 2) != b.ChancePercent || b.ItemId == r.OutputItem || r.Ingredients.Any(i => i.ItemId == b.ItemId))) Error(t, "Use up to eight different bonus outputs; chances have two decimals and total at most 100%. Bonus items cannot be the output or ingredients.");
            if (!r.Enabled) continue;
            Item(t, r.OutputItem, r.OutputQuantity, true);
            foreach (var i in r.Ingredients) Item(t, i.ItemId, i.Quantity, false);
            foreach (var i in r.BonusOutputs) Item(t, i.ItemId, i.Quantity, true);
            foreach (var m in r.Manuals) {
                if (!c.Items.TryGetValue(m.ItemId, out var i) || !i.Enabled || !i.Manual || i.Timed) Error(t, $"Teaching item {m.ItemId} must be an enabled, non-timed manual (Once subtype 1, 2 or 4).");
            }
        }
        if (c.Ranks.Count > MaxRanks || c.Ranks.Select(r => r.Id).Distinct().Count() != c.Ranks.Count) Error("Artisan Ranks", "Use at most 64 Artisan Ranks with distinct permanent IDs.");
        int order = 0, threshold = 0;
        foreach(var r in c.Gathering) {
            string t=$"Gathering item {r.ItemId}";
            if(r.ItemId<=0 || r.Profession is <1 or >4 || r.MasteryXp<0 || !ValidThresholds(r.BronzeRequired,r.SilverRequired,r.MasteryRequired)) Error(t,"Invalid gathering identity, XP or tier targets. Each tier must cost more additional XP.");
            if(!c.Items.TryGetValue(r.ItemId,out var resourceItem) || !resourceItem.Enabled) Error(t,"Resource item does not exist or is disabled.");
            Bonuses(t,r.Bonuses,true);
        }
        if(c.Gathering.Count(r=>r.Profession==3)>64 || c.Gathering.Count(r=>r.Profession!=3)>64 || c.Gathering.Select(r=>r.ItemId).Distinct().Count()!=c.Gathering.Count) Error("Gathering","Use at most 64 distinct resources in each common/Fishing catalogue, with globally unique item IDs.");
        int available = 3*(c.Recipes.Count(r => r.Enabled && r.MasteryXp > 0)+c.Gathering.Count(r=>c.IsGatheringAvailable(r.ItemId) && r.MasteryXp>0));
        foreach (var r in c.Ranks.OrderBy(r => r.Order)) {
            string t = $"Artisan Rank {r.Id}";
            if (r.Id <= 0 || r.Order <= order || r.RequiredMastered <= threshold || r.RequiredMastered > 98304) Error(t, "Artisan Rank order and mastery-point thresholds must increase strictly (maximum threshold 98304).");
            order = r.Order; threshold = r.RequiredMastered;
            if (string.IsNullOrWhiteSpace(r.Name) || Encoding.UTF8.GetByteCount(r.Name) > 63 || r.Name.Any(ch => char.IsControl(ch) || ch is '\u2028' or '\u2029') || !ValidUtf8(r.Name)) Error(t, "Name must be single-line valid UTF-8, 1–63 bytes.");
            if (r.Emblem.Length > 63 || !Regex.IsMatch(r.Emblem, "^[A-Za-z0-9_-]+[.]tex$")) Error(t, "Emblem must be a .tex filename in Data/Interface (maximum 63 ASCII characters).");
            if (r.Enabled && r.RequiredMastered > available) Note(t, $"Needs {r.RequiredMastered} mastery points; {available} are currently available across crafting and gathering. Historical ranks still count.");
            Bonuses(t, r.Bonuses);
            if (r.Perks.Any(p=>!ArtisanPerks.Names.ContainsKey(p.Type)) || r.Perks.Select(p=>p.Type).Distinct().Count()!=r.Perks.Count)
                Error(t, "Use each supported Artisan perk at most once; unknown perk types cannot be saved.");
        }
        if (totals.Take(20).Any(v => v > 32767)) Error("Bonuses", "Combined crafting, gathering and Artisan Rank bonuses exceed 32767 in a stat channel, including overlapping All/Physical effects and retired definitions.");
        for (int channel=20;channel<totals.Length;channel++)
            if (totals[channel] > MasteryBonusUnits.ChannelMaximum(channel))
                Error("Bonuses", "Combined crafting, gathering and Artisan Rank bonuses exceed the limit: " + MasteryBonusUnits.Format(channel + 980, MasteryBonusUnits.ChannelMaximum(channel)) + ". Retired definitions and all earned tiers are included.");
        issues.AddRange(ProfessionRules.Validate(c));
        return issues;
    }
    private static bool ValidUtf8(string text) { try { new UTF8Encoding(false, true).GetBytes(text); return true; } catch (EncoderFallbackException) { return false; } }
}
