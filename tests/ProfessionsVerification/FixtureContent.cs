using MySqlConnector;
using LastChaos_ToolBoxNG.Crafting;
using System.Text.RegularExpressions;

internal static class FixtureContent
{
    // Actual authored DDL is used. The fixture projects data-only sections when
    // an old coordinated migration expects workstation baseline item/NPC tables.
    // It never uses the Toolbox connection or live Compose configuration.
    public static void Setup(string game,MySqlConnection db)
    {
        void Exec(string sql){using var command=new MySqlCommand(sql,db);command.ExecuteNonQuery();}
        string Migration(string name)=>File.ReadAllText(Path.Combine(game,"x64-server/db/migrations",name));
        void Run(string name)=>Exec(Migration(name));
        void Data(string name){var sql=Migration(name);Exec("USE ep4_data;"+sql[(sql.IndexOf("USE ep4_data;",StringComparison.Ordinal)+13)..]);}
        Exec("CREATE TABLE ep4_db.t_characters(a_index INT PRIMARY KEY,a_crafting_smithing TINYINT UNSIGNED NOT NULL DEFAULT1,a_crafting_alchemy TINYINT UNSIGNED NOT NULL DEFAULT1,CONSTRAINT chk_crafting_smithing CHECK(a_crafting_smithing BETWEEN1 AND50),CONSTRAINT chk_crafting_alchemy CHECK(a_crafting_alchemy BETWEEN1 AND50),a_crafting_stat_points INT NOT NULL,CONSTRAINT chk_crafting_stat_points CHECK(a_crafting_stat_points BETWEEN0 AND98)) ENGINE=InnoDB;".Replace("DEFAULT1","DEFAULT 1").Replace("BETWEEN1","BETWEEN 1").Replace("BETWEEN0","BETWEEN 0").Replace("AND50","AND 50").Replace("AND98","AND 98"));
        for(int i=0;i<10;i++)Exec($"CREATE TABLE ep4_db.t_inven{i:00}(a_char_idx INT,a_slot INT,a_item_idx INT,a_count BIGINT,PRIMARY KEY(a_char_idx,a_slot)) ENGINE=InnoDB;");
        foreach(var file in Directory.GetFiles(Path.Combine(game,"x64-server/db/migrations"),"*.sql").Where(p=>int.TryParse(Path.GetFileName(p)[..4],out int n)&&n is >=13 and <=18).Order())Exec(File.ReadAllText(file));
        foreach(var name in new[]{"0019-active-mining.sql","0020-gathering-surface-profiles.sql","0021-gathering-score-rounds.sql","0022-passive-mining.sql","0023-gathering-material-mastery.sql","0027-herb-populations.sql","0028-herb-loot.sql"})Run(name);
        Data("0029-herbalism-mastery.sql");Run("0030-herb-harvest-animation.sql");
        string fishing=Migration("0031-fishing-prototype.sql");int begin=fishing.IndexOf("CREATE TABLE t_fishing_settings",StringComparison.Ordinal),end=fishing.IndexOf("INSERT INTO t_fishing_settings",begin,StringComparison.Ordinal);Exec("USE ep4_data;"+fishing[begin..end]);
        string rod=Migration("0032-fishing-rod-item.sql");begin=rod.IndexOf("ALTER TABLE t_fishing_settings",StringComparison.Ordinal);end=rod.IndexOf(';',begin);Exec("USE ep4_data;"+rod[begin..(end+1)]);
        Data("0033-wave-absorption.sql");Run("0034-active-wave-attunement.sql");Run("0035-wave-attunement-holds.sql");
        ExecuteScript(db,Migration("0036-three-tier-mastery.sql"));Run("0037-artisan-rank-names.sql");
        Exec("USE ep4_data;CREATE TABLE t_item(a_index INT PRIMARY KEY,a_name_usa VARCHAR(100),a_enable INT,a_type_idx INT,a_subtype_idx INT,a_flag BIGINT,a_weight INT,a_texture_id INT,a_texture_row INT,a_texture_col INT,a_wearing INT NOT NULL DEFAULT -1,a_num_0 INT NOT NULL DEFAULT 0,a_num_1 INT NOT NULL DEFAULT 0,a_max_use INT NOT NULL DEFAULT -1) ENGINE=InnoDB;");
        ExecuteScript(db,Migration("0038-alchemy-potion-mastery.sql"));ExecuteScript(db,Migration("0039-alchemy-station.sql"));ExecuteScript(db,Migration("0040-alchemy-artisan-rank-unlock.sql"));
        ExecuteScript(db,Migration("0041-alchemy-work-orders.sql"));
        // Project 0042's authored recipe/mapping rows; the compact fixture item
        // schema deliberately omits unrelated localization/equipment columns.
        string products=Migration("0042-alchemy-permanent-potion-items.sql");
        Exec("USE ep4_data;"+products[products.IndexOf("UPDATE t_alchemy_offer",StringComparison.Ordinal)..]);
        Exec("CREATE TABLE t_npc(a_index INT PRIMARY KEY,a_name VARCHAR(100),a_name_usa VARCHAR(100),a_flag BIGINT,a_enable INT) ENGINE=InnoDB;CREATE TABLE t_zonedata(a_zone_index INT PRIMARY KEY,a_name VARCHAR(100)) ENGINE=InnoDB;");
        foreach(string table in CraftingRepository.Tables)Exec(File.ReadAllText(Path.Combine(game,"x64-server/db/seeds/ep4_data",table+".sql")));
        // Eligibility metadata is intentionally synthetic here. The offline UI
        // fixture separately checks all actual item/skill/magic seed rows.
        foreach(var pair in LastChaos_ToolBoxNG.Professions.InfusionMetadata.Columns.Where(p=>p.Key!="t_item")) {
            Exec("CREATE TABLE `"+pair.Key+"` ("+string.Join(",",pair.Value.Select(c=>"`"+c+"` INT"))+(pair.Key=="t_skill"?",a_name_usa VARCHAR(100)":"")+") ENGINE=InnoDB;");
        }
        Exec("INSERT INTO t_item(a_index,a_name_usa,a_enable,a_type_idx,a_subtype_idx,a_flag,a_weight,a_texture_id,a_texture_row,a_texture_col) SELECT item,CONCAT('Fixture item ',item),1,4,0,1,2147483647,0,0,0 FROM (SELECT a_output_item item FROM t_crafting_recipe UNION SELECT a_item_index FROM t_crafting_ingredient UNION SELECT a_manual_item FROM t_crafting_manual UNION SELECT a_item_index FROM t_crafting_bonus_output UNION SELECT a_item_index FROM t_gathering_material UNION SELECT a_item_index FROM t_gathering_tool UNION SELECT a_visual_tool_index FROM t_herb_population UNION SELECT a_rod_item_index FROM t_fishing_settings UNION SELECT a_item_index FROM t_alchemy_consumable UNION SELECT a_item_index FROM t_alchemy_ingredient) refs;");
        Exec("UPDATE t_item SET a_type_idx=2,a_subtype_idx=1 WHERE a_index IN(SELECT a_manual_item FROM t_crafting_manual);UPDATE t_item i JOIN t_gathering_tool t ON t.a_item_index=i.a_index SET i.a_type_idx=0,i.a_subtype_idx=IF(t.a_profession=4,10,8);UPDATE t_item SET a_type_idx=0,a_subtype_idx=9 WHERE a_index IN(SELECT a_visual_tool_index FROM t_herb_population);");
        Exec("UPDATE t_item i JOIN t_alchemy_consumable p ON p.a_item_index=i.a_index JOIN t_alchemy_effect e ON e.a_index=p.a_effect_index SET i.a_type_idx=6,i.a_subtype_idx=5,i.a_flag=27,i.a_num_0=0,i.a_num_1=0;");
        Exec("UPDATE t_item i JOIN t_alchemy_effect e ON e.a_item_index=i.a_index SET i.a_type_idx=6,i.a_subtype_idx=5,i.a_num_0=e.a_skill_index,i.a_num_1=e.a_skill_level;INSERT INTO t_skill SELECT a_skill_index,2,2,0,a_skill_level,0,0,CONCAT('Fixture skill ',a_skill_index) FROM t_alchemy_effect;INSERT INTO t_skilllevel SELECT a_skill_index,a_skill_level,36000,0,1000+a_skill_index,1,-1,0,-1,0 FROM t_alchemy_effect;INSERT INTO t_magic SELECT 1000+a_skill_index,0,0,1 FROM t_alchemy_effect;INSERT INTO t_magiclevel SELECT 1000+a_skill_index,1 FROM t_alchemy_effect;");
        Exec("INSERT INTO t_npc SELECT a_npc_index,CONCAT('NPC ',a_npc_index),CONCAT('English NPC ',a_npc_index),IF(a_profession=4,4096,1024),1 FROM t_gathering_node;INSERT INTO t_npc SELECT DISTINCT a_npc_index,CONCAT('NPC ',a_npc_index),CONCAT('English NPC ',a_npc_index),2064,1 FROM t_herb_population ON DUPLICATE KEY UPDATE a_flag=2064;");
        Exec("INSERT INTO t_zonedata SELECT DISTINCT zone,CONCAT('Zone ',zone) FROM(SELECT a_zone_index zone FROM t_herb_population UNION SELECT a_zone_index FROM t_fishing_water UNION SELECT0 UNION SELECT4) z;".Replace("SELECT0","SELECT 0").Replace("SELECT4","SELECT 4"));
        Exec("SET FOREIGN_KEY_CHECKS=1;INSERT INTO ep4_db.t_characters(a_index,a_crafting_stat_points) VALUES(1,0);INSERT INTO ep4_db.t_inven01 VALUES(1,0,156,123);INSERT INTO ep4_db.t_crafting_mastery VALUES(1,1,100,1,3);INSERT INTO ep4_db.t_gathering_mastery VALUES(1,156,200,0,1);INSERT INTO ep4_db.t_crafting_master_rank VALUES(1,1);INSERT INTO ep4_db.t_alchemy_infusion_unlock(a_char_index,a_effect_index,a_operation_token) VALUES(1,1,'fixture-token');INSERT INTO ep4_db.t_alchemy_work_order(a_char_index,a_active_char_index,a_offer_index,a_effect_index,a_output_item_index,a_output_count,a_costs,a_started_at,a_ready_at,a_start_token) VALUES(1,1,1,1,640124,1,'156:1',1,100,'fixture-brew-token');");
    }
    public static void ExecuteScript(MySqlConnection db,string sql)
    {
        string delimiter=";";var pending=new System.Text.StringBuilder();
        foreach(string line in sql.Split('\n')) {
            if(line.Trim().StartsWith("DELIMITER ")){delimiter=line.Trim()[10..];continue;}
            pending.AppendLine(line);if(line.TrimEnd().EndsWith(delimiter,StringComparison.Ordinal)){string statement=pending.ToString().TrimEnd();using var cmd=new MySqlCommand(statement[..^delimiter.Length],db);cmd.ExecuteNonQuery();pending.Clear();}
        }
        if(!string.IsNullOrWhiteSpace(pending.ToString())){using var cmd=new MySqlCommand(pending.ToString(),db);cmd.ExecuteNonQuery();}
    }
}
