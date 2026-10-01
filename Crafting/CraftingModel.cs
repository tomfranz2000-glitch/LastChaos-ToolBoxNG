using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LastChaos_ToolBoxNG.Crafting;

public enum Profession { Smithing, Alchemy, Cooking }
public sealed class Ingredient { public int ItemId { get; set; } public int Quantity { get; set; } = 1; }
public sealed class TeachingItem { public int ItemId { get; set; } }
public sealed class Bonus { public int Effect { get; set; } = 4; public int Amount { get; set; } = 1; }
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
    public int MasteryXp { get; set; }
    public int MasteryRequired { get; set; }
    public List<Ingredient> Ingredients { get; set; } = [];
    public List<TeachingItem> Manuals { get; set; } = [];
    public List<Bonus> Bonuses { get; set; } = [];
    public List<BonusOutput> BonusOutputs { get; set; } = [];
    [JsonIgnore] public string Name { get; set; } = "Choose an output item";
}
public sealed class MastersRank
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = "New rank";
    public int RequiredMastered { get; set; } = 1;
    public string Emblem { get; set; } = "AchievementCategory_2001.tex";
    public bool Enabled { get; set; } = true;
    public List<Bonus> Bonuses { get; set; } = [];
}
public sealed record CraftingItem(int Id, string Name, bool Enabled, int Type, int Subtype,
    long Flags, int StackLimit, int Texture, int Row, int Column, bool Rebirth)
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
    [JsonIgnore] public Dictionary<int, CraftingItem> Items { get; set; } = [];
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public string Fingerprint() => JsonSerializer.Serialize(new {
        SkillCap, Recipes = Recipes.OrderBy(r => r.Id), Ranks = Ranks.OrderBy(r => r.Id)
    });
    public string ItemName(int id) => Items.TryGetValue(id, out var item) ? item.Name : $"Missing item #{id}";
    public void UpdateNames() { foreach (var r in Recipes) r.Name = ItemName(r.OutputItem); }
    public IEnumerable<string> WhereUsed(int item) => Recipes.SelectMany(r => {
        var uses = new List<string>();
        if (r.OutputItem == item) uses.Add("output");
        if (r.Ingredients.Any(i => i.ItemId == item)) uses.Add("ingredient");
        if (r.Manuals.Any(i => i.ItemId == item)) uses.Add("teaches");
        if (r.BonusOutputs.Any(i => i.ItemId == item)) uses.Add("bonus output");
        return uses.Select(u => $"#{r.Id} {ItemName(r.OutputItem)} — {u}{(r.Enabled ? "" : " (retired)")}");
    });
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
    static CraftingRules() { Effects[102] = "HP Regeneration"; Effects[103] = "MP Regeneration"; }
    public static IEnumerable<int> Channels(int effect) => effect switch {
        >= 0 and <= 5 => [effect], 6 => [6, 7], 7 => [6], 8 => [7], 9 => [9], 10 => [10],
        11 => [12, 13], 12 => [12], 13 => [13], 14 => [15], 15 => [16], 16 => [8],
        17 => [11], 18 => [14], 19 => [17], 20 => [6, 7, 8], 21 => [9, 10, 11],
        22 => [12, 13, 14], 23 => [15, 16, 17], 102 => [18], 103 => [19], _ => []
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
    public static string Rewards(IEnumerable<Bonus> bonuses) => string.Join(", ", bonuses.GroupBy(b => b.Effect)
        .Select(g => $"+{g.Sum(b => (long)b.Amount):N0} {Effects.GetValueOrDefault(g.Key, $"Unknown effect {g.Key}")}"));
    public static List<CraftingIssue> Validate(CraftingCatalog c)
    {
        List<CraftingIssue> issues = [];
        void Error(string t, string m) => issues.Add(new(true, t, m));
        void Note(string t, string m) => issues.Add(new(false, t, m));
        var totals = new long[20];
        void Bonuses(string t, List<Bonus> bs) {
            if (bs.Count > 8 || bs.Select(b => b.Effect).Distinct().Count() != bs.Count) Error(t, "Use at most eight distinct bonus effects.");
            foreach (var b in bs) {
                if (!Effects.ContainsKey(b.Effect) || b.Amount is < 1 or > 32767) Error(t, "Bonuses need a supported flat effect and amount 1–32767.");
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
            if (r.MasteryXp < 0 || r.MasteryRequired < 0 || (r.MasteryRequired == 0 && (r.MasteryXp != 0 || r.Bonuses.Count != 0))) Error(t, "Disabled mastery must have zero XP and no bonuses.");
            if (r.MasteryRequired > 0 && r.MasteryXp == 0) Note(t, "Mastery is enabled but this recipe awards no XP.");
            Bonuses(t, r.Bonuses);
            if (r.BonusOutputs.Count > 8 || r.BonusOutputs.Select(b => b.ItemId).Distinct().Count() != r.BonusOutputs.Count || r.BonusOutputs.Sum(b => Math.Clamp(b.ChancePercent, 0m, 101m)) > 100 || r.BonusOutputs.Any(b => b.ItemId <= 0 || b.Quantity <= 0 || b.ChancePercent <= 0 || b.ChancePercent > 100 || decimal.Round(b.ChancePercent, 2) != b.ChancePercent || b.ItemId == r.OutputItem || r.Ingredients.Any(i => i.ItemId == b.ItemId))) Error(t, "Use up to eight different bonus outputs; chances have two decimals and total at most 100%. Bonus items cannot be the output or ingredients.");
            if (!r.Enabled) continue;
            Item(t, r.OutputItem, r.OutputQuantity, true);
            foreach (var i in r.Ingredients) Item(t, i.ItemId, i.Quantity, false);
            foreach (var i in r.BonusOutputs) Item(t, i.ItemId, i.Quantity, true);
            foreach (var m in r.Manuals) {
                if (!c.Items.TryGetValue(m.ItemId, out var i) || !i.Enabled || !i.Manual || i.Timed) Error(t, $"Teaching item {m.ItemId} must be an enabled, non-timed manual (Once subtype 1, 2 or 4).");
            }
        }
        if (c.Ranks.Count > MaxRanks || c.Ranks.Select(r => r.Id).Distinct().Count() != c.Ranks.Count) Error("Ranks", "Use at most 64 ranks with distinct permanent IDs.");
        int order = 0, threshold = 0;
        int available = c.Recipes.Count(r => r.Enabled && r.MasteryRequired > 0 && r.MasteryXp > 0);
        foreach (var r in c.Ranks.OrderBy(r => r.Order)) {
            string t = $"Rank {r.Id}";
            if (r.Id <= 0 || r.Order <= order || r.RequiredMastered <= threshold || r.RequiredMastered > 16384) Error(t, "Rank order and mastered-recipe thresholds must increase strictly (maximum threshold 16384).");
            order = r.Order; threshold = r.RequiredMastered;
            if (string.IsNullOrWhiteSpace(r.Name) || Encoding.UTF8.GetByteCount(r.Name) > 63 || r.Name.Any(ch => char.IsControl(ch) || ch is '\u2028' or '\u2029') || !ValidUtf8(r.Name)) Error(t, "Name must be single-line valid UTF-8, 1–63 bytes.");
            if (r.Emblem.Length > 63 || !Regex.IsMatch(r.Emblem, "^[A-Za-z0-9_-]+[.]tex$")) Error(t, "Emblem must be a .tex filename in Data/Interface (maximum 63 ASCII characters).");
            if (r.Enabled && r.RequiredMastered > available) Note(t, $"Needs {r.RequiredMastered} mastered recipes; only {available} are currently available. Historical mastery still counts.");
            Bonuses(t, r.Bonuses);
        }
        if (totals.Any(v => v > 32767)) Error("Bonuses", "Combined recipe and rank bonuses exceed 32767 in a stat channel, including overlapping All/Physical effects and retired definitions.");
        return issues;
    }
    private static bool ValidUtf8(string text) { try { new UTF8Encoding(false, true).GetBytes(text); return true; } catch (EncoderFallbackException) { return false; } }
}
