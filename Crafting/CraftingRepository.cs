using MySqlConnector;
using System.Data;
using System.Text.RegularExpressions;

namespace LastChaos_ToolBoxNG.Crafting;

public sealed class CraftingRepository(string connectionString, string locale)
{
    public static readonly string[] Tables = ["t_crafting_recipe", "t_crafting_ingredient", "t_crafting_manual",
        "t_crafting_bonus_output", "t_crafting_mastery", "t_crafting_mastery_bonus", "t_crafting_master_rank", "t_crafting_master_rank_bonus"];
    private readonly string language = Regex.IsMatch(locale, "^[a-z]+$") ? locale : throw new ArgumentException("Invalid item locale.");
    public CraftingCatalog Load()
    {
        using var db = new MySqlConnection(connectionString); db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.RepeatableRead);
        CheckSchema(db, tx);
        var result = Read(db, tx, false); tx.Commit(); return result;
    }
    // Keep authored rows stable while the canonical exporter reads them on its
    // own connection. Plain SELECTs remain available to the game and exporter.
    public void WithLockedCatalog(Action<CraftingCatalog, string> action)
    {
        using var db = new MySqlConnection(connectionString); db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.RepeatableRead);
        CheckSchema(db, tx);
        var snapshot = Read(db, tx, true);
        using var identity = new MySqlCommand("SELECT CONCAT(@@hostname, ':', @@server_id, ':', DATABASE())", db, tx);
        action(snapshot, Convert.ToString(identity.ExecuteScalar())!);
        tx.Commit();
    }
    private static MySqlCommand Command(MySqlConnection db, MySqlTransaction tx, string sql, params object[] values)
    {
        var cmd = new MySqlCommand(sql, db, tx);
        for (int i = 0; i < values.Length; ++i) cmd.Parameters.AddWithValue("@p" + i, values[i]);
        return cmd;
    }
    private static void Execute(MySqlConnection db, MySqlTransaction tx, string sql, params object[] values)
    { using var cmd = Command(db, tx, sql, values); cmd.ExecuteNonQuery(); }
    private static void Rows(MySqlConnection db, MySqlTransaction tx, string sql, Action<MySqlDataReader> read)
    { using var cmd = new MySqlCommand(sql, db, tx); using var r = cmd.ExecuteReader(); while (r.Read()) read(r); }
    private static void CheckSchema(MySqlConnection db, MySqlTransaction tx)
    {
        foreach (string t in Tables) Rows(db, tx, $"SELECT 1 FROM `{t}` LIMIT 0", _ => { });
        var found = new Dictionary<string, string>();
        Rows(db, tx, "SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN (" +
            string.Join(",", Tables.Select(t => "'" + t + "'")) + ")", r => found[r.GetString(0)] = r.GetString(1));
        if (Tables.Any(t => !found.TryGetValue(t, out string? e) || e != "InnoDB"))
            throw new InvalidOperationException("Crafting requires all eight InnoDB content tables. Apply the game's crafting migrations through 0017; this editor never creates or changes schema.");
        // Hold metadata locks until commit; never let a concurrent ALTER swap in
        // nontransactional storage between validation and the first write.
        using var triggers = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE EVENT_OBJECT_SCHEMA=DATABASE() AND EVENT_OBJECT_TABLE IN (" +
            string.Join(",", Tables.Select(t => "'" + t + "'")) + ")", db, tx);
        if (Convert.ToInt32(triggers.ExecuteScalar()) != 0)
            throw new InvalidOperationException("Crafting tables have custom triggers. Their write targets must be reviewed before transactional editing.");
    }
    private CraftingCatalog Read(MySqlConnection db, MySqlTransaction tx, bool locked)
    {
        string Lock(string sql) => sql + (locked ? " FOR UPDATE" : "");
        var c = new CraftingCatalog();
        Rows(db, tx, Lock("SELECT a_index,a_category,a_output_item,a_output_count,a_craft_time_ms,a_required_skill,a_no_skill_up,a_sort_order,a_enable,a_requires_unlock FROM t_crafting_recipe ORDER BY a_index"), r => c.Recipes.Add(new() {
            Id=r.GetInt32(0), Profession=(Profession)r.GetInt32(1), OutputItem=r.GetInt32(2), OutputQuantity=r.GetInt32(3), CraftTimeMs=r.GetInt32(4),
            RequiredSkill=r.GetInt32(5), NoSkillUp=r.GetInt32(6), SortOrder=r.GetInt32(7), Enabled=r.GetBoolean(8), RequiresUnlock=r.GetBoolean(9) }));
        var recipes = c.Recipes.ToDictionary(r => r.Id);
        Rows(db, tx, Lock("SELECT a_recipe_index,a_slot,a_item_index,a_count FROM t_crafting_ingredient ORDER BY a_recipe_index,a_slot"), r => {
            var recipe = recipes[r.GetInt32(0)]; CheckSlot(recipe.Ingredients.Count,r.GetInt32(1));
            recipe.Ingredients.Add(new() { ItemId=r.GetInt32(2), Quantity=r.GetInt32(3) }); });
        Rows(db, tx, Lock("SELECT a_recipe_index,a_manual_item FROM t_crafting_manual ORDER BY a_recipe_index,a_manual_item"), r => recipes[r.GetInt32(0)].Manuals.Add(new() { ItemId=r.GetInt32(1) }));
        Rows(db, tx, Lock("SELECT a_recipe_index,a_slot,a_item_index,a_count,a_chance FROM t_crafting_bonus_output ORDER BY a_recipe_index,a_slot"), r => {
            var recipe=recipes[r.GetInt32(0)]; CheckSlot(recipe.BonusOutputs.Count,r.GetInt32(1));
            recipe.BonusOutputs.Add(new() { ItemId=r.GetInt32(2), Quantity=r.GetInt32(3), ChancePercent=r.GetInt32(4)/100m }); });
        Rows(db, tx, Lock("SELECT a_recipe_index,a_exp_per_craft,a_exp_required FROM t_crafting_mastery ORDER BY a_recipe_index"), r => {
            var recipe=recipes[r.GetInt32(0)]; recipe.MasteryXp=r.GetInt32(1); recipe.MasteryRequired=r.GetInt32(2); });
        Rows(db, tx, Lock("SELECT a_recipe_index,a_slot,a_option_type,a_value FROM t_crafting_mastery_bonus ORDER BY a_recipe_index,a_slot"), r => {
            var recipe=recipes[r.GetInt32(0)]; CheckSlot(recipe.Bonuses.Count,r.GetInt32(1)); recipe.Bonuses.Add(new() { Effect=r.GetInt32(2), Amount=r.GetInt32(3) }); });
        Rows(db, tx, Lock("SELECT a_index,a_order,a_name,a_required_mastered,a_emblem,a_enable FROM t_crafting_master_rank ORDER BY a_index"), r => c.Ranks.Add(new() {
            Id=r.GetInt32(0), Order=r.GetInt32(1), Name=r.GetString(2), RequiredMastered=r.GetInt32(3), Emblem=r.GetString(4), Enabled=r.GetBoolean(5) }));
        var ranks=c.Ranks.ToDictionary(r => r.Id);
        Rows(db, tx, Lock("SELECT a_rank_index,a_slot,a_option_type,a_value FROM t_crafting_master_rank_bonus ORDER BY a_rank_index,a_slot"), r => {
            var rank=ranks[r.GetInt32(0)]; CheckSlot(rank.Bonuses.Count,r.GetInt32(1)); rank.Bonuses.Add(new() { Effect=r.GetInt32(2), Amount=r.GetInt32(3) }); });
        int[] rebirth = [846,2667,3218,4933,5958,7056,9194,9195,9196]; // Item.cpp engine exclusions.
        Rows(db, tx, "SELECT a_index,COALESCE(a_name_"+language+",''),a_enable,a_type_idx,a_subtype_idx,a_flag,a_weight,a_texture_id,a_texture_row,a_texture_col FROM t_item ORDER BY a_index" + (locked ? " LOCK IN SHARE MODE" : ""), r => {
            int id=r.GetInt32(0); c.Items[id]=new(id,r.GetString(1),r.GetBoolean(2),r.GetInt32(3),r.GetInt32(4),r.GetInt64(5),r.GetInt32(6),r.GetInt32(7),r.GetInt32(8),r.GetInt32(9),rebirth.Contains(id)); });
        c.UpdateNames(); return c;
    }
    private static void CheckSlot(int expected,int actual)
    { if(expected!=actual) throw new InvalidOperationException("Crafting child rows have non-contiguous slots. Repair the catalogue before editing."); }
    public void Save(CraftingCatalog original, CraftingCatalog edited)
    {
        // Snapshot the UI draft before work leaves its thread. This API owns one
        // connection/transaction and only the eight authored content tables.
        using var db=new MySqlConnection(connectionString); db.Open();
        using var tx=db.BeginTransaction(IsolationLevel.RepeatableRead);
        try {
            CheckSchema(db,tx);
            var current=Read(db,tx,true);
            if(current.Fingerprint()!=original.Fingerprint()) throw new InvalidOperationException("Crafting data changed in another editor. Your draft is retained. Reload and reapply your edits before saving.");
            if(original.Recipes.Any(r=>!edited.Recipes.Any(n=>n.Id==r.Id)) || original.Ranks.Any(r=>!edited.Ranks.Any(n=>n.Id==r.Id)))
                throw new InvalidOperationException("Published IDs cannot be removed or renumbered. Retire the definition instead.");
            edited.Items=current.Items;
            var errors=CraftingRules.Validate(edited).Where(i=>i.Error).ToList();
            if(errors.Count!=0) throw new InvalidOperationException(string.Join("\n",errors));
            foreach(var r in edited.Recipes) {
                var old=original.Recipes.FirstOrDefault(o=>o.Id==r.Id);
                if(old!=null && System.Text.Json.JsonSerializer.Serialize(old)==System.Text.Json.JsonSerializer.Serialize(r)) continue;
                Execute(db,tx,"INSERT INTO t_crafting_recipe(a_index,a_category,a_output_item,a_output_count,a_craft_time_ms,a_required_skill,a_no_skill_up,a_sort_order,a_enable,a_requires_unlock) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9) ON DUPLICATE KEY UPDATE a_category=VALUES(a_category),a_output_item=VALUES(a_output_item),a_output_count=VALUES(a_output_count),a_craft_time_ms=VALUES(a_craft_time_ms),a_required_skill=VALUES(a_required_skill),a_no_skill_up=VALUES(a_no_skill_up),a_sort_order=VALUES(a_sort_order),a_enable=VALUES(a_enable),a_requires_unlock=VALUES(a_requires_unlock)",r.Id,(int)r.Profession,r.OutputItem,r.OutputQuantity,r.CraftTimeMs,r.RequiredSkill,r.NoSkillUp,r.SortOrder,r.Enabled,r.RequiresUnlock);
                foreach(string table in new[]{"t_crafting_ingredient","t_crafting_manual","t_crafting_bonus_output","t_crafting_mastery_bonus","t_crafting_mastery"}) Execute(db,tx,$"DELETE FROM {table} WHERE a_recipe_index=@p0",r.Id);
                for(int i=0;i<r.Ingredients.Count;i++) Execute(db,tx,"INSERT INTO t_crafting_ingredient(a_recipe_index,a_slot,a_item_index,a_count) VALUES(@p0,@p1,@p2,@p3)",r.Id,i,r.Ingredients[i].ItemId,r.Ingredients[i].Quantity);
                foreach(var m in r.Manuals.OrderBy(m=>m.ItemId)) Execute(db,tx,"INSERT INTO t_crafting_manual(a_manual_item,a_recipe_index) VALUES(@p0,@p1)",m.ItemId,r.Id);
                for(int i=0;i<r.BonusOutputs.Count;i++) Execute(db,tx,"INSERT INTO t_crafting_bonus_output(a_recipe_index,a_slot,a_item_index,a_count,a_chance) VALUES(@p0,@p1,@p2,@p3,@p4)",r.Id,i,r.BonusOutputs[i].ItemId,r.BonusOutputs[i].Quantity,(int)(r.BonusOutputs[i].ChancePercent*100));
                if(r.MasteryRequired>0) Execute(db,tx,"INSERT INTO t_crafting_mastery(a_recipe_index,a_exp_per_craft,a_exp_required) VALUES(@p0,@p1,@p2)",r.Id,r.MasteryXp,r.MasteryRequired);
                WriteBonuses(db,tx,"t_crafting_mastery_bonus",r.Id,r.Bonuses);
            }
            // UNIQUE(a_order) requires temporary unused positive orders when
            // swapping rows; they are never visible outside this transaction.
            var occupied=original.Ranks.Select(r=>r.Order).Concat(edited.Ranks.Select(r=>r.Order)).ToHashSet();
            int temporary=1;
            foreach(var r in original.Ranks) {
                while(occupied.Contains(temporary)) temporary++;
                Execute(db,tx,"UPDATE t_crafting_master_rank SET a_order=@p0 WHERE a_index=@p1",temporary,r.Id); occupied.Add(temporary++);
            }
            foreach(var r in edited.Ranks) {
                Execute(db,tx,"INSERT INTO t_crafting_master_rank(a_index,a_order,a_name,a_required_mastered,a_emblem,a_enable) VALUES(@p0,@p1,@p2,@p3,@p4,@p5) ON DUPLICATE KEY UPDATE a_order=VALUES(a_order),a_name=VALUES(a_name),a_required_mastered=VALUES(a_required_mastered),a_emblem=VALUES(a_emblem),a_enable=VALUES(a_enable)",r.Id,r.Order,r.Name,r.RequiredMastered,r.Emblem,r.Enabled);
                Execute(db,tx,"DELETE FROM t_crafting_master_rank_bonus WHERE a_rank_index=@p0",r.Id);
                WriteBonuses(db,tx,"t_crafting_master_rank_bonus",r.Id,r.Bonuses);
            }
            tx.Commit();
        } catch { try { tx.Rollback(); } catch { /* Keep the original failure. */ } throw; }
    }
    private static void WriteBonuses(MySqlConnection db,MySqlTransaction tx,string table,int id,List<Bonus> bonuses)
    {
        string key=table=="t_crafting_mastery_bonus"?"a_recipe_index":"a_rank_index";
        for(int i=0;i<bonuses.Count;i++) Execute(db,tx,$"INSERT INTO {table}({key},a_slot,a_option_type,a_value) VALUES(@p0,@p1,@p2,@p3)",id,i,bonuses[i].Effect,bonuses[i].Amount);
    }
}
