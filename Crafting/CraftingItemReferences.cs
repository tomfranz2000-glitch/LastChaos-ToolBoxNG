using MySqlConnector;

namespace LastChaos_ToolBoxNG.Crafting;

public static class CraftingItemReferences
{
    private static readonly (string Table, string Field, string Role)[] Uses = [
        ("t_crafting_recipe", "a_output_item", "output"),
        ("t_crafting_ingredient", "a_item_index", "ingredient"),
        ("t_crafting_manual", "a_manual_item", "teaching item"),
        ("t_crafting_bonus_output", "a_item_index", "bonus output")];

    public static bool HasSchema(MySqlConnection db)
    {
        using var cmd = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN (" +
            string.Join(",", CraftingRepository.Tables.Select(t => "'" + t + "'")) + ")", db);
        int count = Convert.ToInt32(cmd.ExecuteScalar());
        if (count is not 0 and not 8) throw new InvalidOperationException("Incomplete crafting schema. Apply all crafting migrations before changing item references.");
        return count == 8;
    }
    public static List<string> Read(string connectionString, int item, string locale = "usa")
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(locale,"^[a-z]+$")) throw new ArgumentException("Invalid item locale.");
        using var db = new MySqlConnection(connectionString); db.Open();
        if (!HasSchema(db)) return [];
        List<string> result = [];
        foreach (var use in Uses) {
            string id = use.Table == "t_crafting_recipe" ? "a_index" : "a_recipe_index";
            using var cmd = new MySqlCommand($"SELECT u.{id},COALESCE(i.a_name_{locale},'Missing output item') FROM {use.Table} u JOIN t_crafting_recipe r ON r.a_index=u.{id} LEFT JOIN t_item i ON i.a_index=r.a_output_item WHERE u.{use.Field}=@item ORDER BY u.{id}", db);
            cmd.Parameters.AddWithValue("@item", item);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) result.Add($"Recipe #{reader.GetInt32(0)} {reader.GetString(1)} — {use.Role}");
        }
        return result;
    }
    public static string DeleteGuard(string schema, int item)
    {
        if (item <= 0) throw new ArgumentOutOfRangeException(nameof(item));
        string database = "`" + schema.Replace("`", "``") + "`";
        // INSERT SELECT takes shared next-key locks under InnoDB REPEATABLE READ.
        // A concurrently added reference either wins first (and fails this
        // CHECK) or waits until the item deletion has committed.
        return "CREATE TEMPORARY TABLE Temp_CraftingReferenceGuard (n BIGINT CHECK(n=0)) ENGINE=InnoDB;\n" +
            string.Join("\n", Uses.Select(u => $"INSERT INTO Temp_CraftingReferenceGuard(n) SELECT COUNT(*) FROM {database}.{u.Table} WHERE {u.Field}={item};")) +
            "\nDROP TEMPORARY TABLE Temp_CraftingReferenceGuard;\n";
    }
}
