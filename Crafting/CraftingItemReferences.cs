using MySqlConnector;

namespace LastChaos_ToolBoxNG.Crafting;

public static class CraftingItemReferences
{
    private static readonly (string Table, string Field, string Role)[] Uses = [
        ("t_crafting_recipe", "a_output_item", "output"),
        ("t_crafting_ingredient", "a_item_index", "ingredient"),
        ("t_crafting_manual", "a_manual_item", "teaching item"),
        ("t_crafting_bonus_output", "a_item_index", "bonus output"),
        ("t_gathering_material", "a_item_index", "gathering mastery"),
        ("t_gathering_node", "a_item_index", "Node base resource"),
        ("t_gathering_node_loot", "a_item_index", "Node weighted outcome"),
        ("t_gathering_tool", "a_item_index", "Gathering tool"),
        ("t_herb_population", "a_visual_tool_index", "Herb visual tool"),
        ("t_herb_loot", "a_item_index", "Herb harvest outcome"),
        ("t_fishing_settings", "a_rod_item_index", "Required fishing rod"),
        ("t_fishing_loot", "a_item_index", "Fishing catch"),
        ("t_alchemy_effect", "a_item_index", "Original infusion potion"),
        ("t_alchemy_offer", "a_output_item_index", "Brewed potion output"),
        ("t_alchemy_ingredient", "a_item_index", "Brewing ingredient"),
        ("t_alchemy_facility_ingredient", "a_item_index", "Station construction material"),
        ("t_alchemy_consumable", "a_item_index", "Permanent potion effect"),
        ("t_transmutation_offer", "a_output_item_index", "Transmutation output"),
        ("t_transmutation_facility_ingredient", "a_item_index", "Crucible construction material"),
        ("t_transmutation_item_rule", "a_item_index", "Transmutation equipment exception")];

    public static bool HasSchema(MySqlConnection db)
    {
        using var cmd = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN (" +
            string.Join(",", CraftingRepository.Tables.Select(t => "'" + t + "'")) + ")", db);
        int count = Convert.ToInt32(cmd.ExecuteScalar());
        if (count != 0 && count != CraftingRepository.Tables.Length) throw new InvalidOperationException("Incomplete profession schema. Apply the profession migrations before changing item references.");
        return count == CraftingRepository.Tables.Length;
    }
    public static List<string> Read(string connectionString, int item, string locale = "usa")
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(locale,"^[a-z]+$")) throw new ArgumentException("Invalid item locale.");
        using var db = new MySqlConnection(connectionString); db.Open();
        if (!HasSchema(db)) return [];
        List<string> result = [];
        foreach (var use in Uses) {
            if(use.Table=="t_gathering_material") {
                using var resource=new MySqlCommand($"SELECT COALESCE(i.a_name_{locale},'Missing resource item') FROM t_gathering_material u LEFT JOIN t_item i ON i.a_index=u.a_item_index WHERE u.a_item_index=@item",db);
                resource.Parameters.AddWithValue("@item",item);var name=resource.ExecuteScalar();
                if(name is not null)result.Add($"Gathering resource #{item} {name} — mastery");
                continue;
            }
            if(!use.Table.StartsWith("t_crafting_",StringComparison.Ordinal)) {
                string key=use.Table switch {
                    "t_gathering_node_loot"=>"CONCAT(a_node_index,' / ',a_slot)",
                    "t_herb_loot"=>"CONCAT(a_population_index,' / ',a_slot)",
                    "t_fishing_loot"=>"CONCAT(a_water_index,' / ',a_slot)",
                    "t_gathering_tool" or "t_alchemy_consumable" or "t_transmutation_item_rule"=>"CAST(a_item_index AS CHAR)",
                    "t_alchemy_ingredient"=>"CONCAT(a_offer_index,' / ',a_slot)",
                    "t_alchemy_facility_ingredient" or "t_transmutation_facility_ingredient"=>"CONCAT(a_facility_index,' / ',a_slot)",
                    _=>"CAST(a_index AS CHAR)"
                };
                using var source=new MySqlCommand($"SELECT {key} FROM {use.Table} WHERE {use.Field}=@item ORDER BY 1",db);
                source.Parameters.AddWithValue("@item",item);
                using var sourceRows=source.ExecuteReader();
                while(sourceRows.Read())result.Add($"{use.Role} #{sourceRows.GetString(0)}");
                continue;
            }
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
