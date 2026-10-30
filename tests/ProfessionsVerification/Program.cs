using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using MySqlConnector;
using System.Text.Json;

internal static class Program
{
    const string Endpoint="Server=127.0.0.1;Port=33318;User ID=root;Password=professions-fixture-only;Connection Timeout=4;Default Command Timeout=20;";
    const string Connection=Endpoint+"Database=ep4_data;Character Set=utf8mb4;";
    static readonly CraftingRepository Repository=new(Connection,"usa");
    static MySqlConnection db=null!;static int checks;static string output="";
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    static void Exec(string sql){using var cmd=new MySqlCommand(sql,db);cmd.ExecuteNonQuery();}
    static string Scalar(string sql){using var cmd=new MySqlCommand(sql,db);return Convert.ToString(cmd.ExecuteScalar())??"";}
    static void Reject(Action action,string contains){try{action();}catch(Exception e){Check(e.ToString().Contains(contains,StringComparison.OrdinalIgnoreCase),"Rejection: "+contains+" ["+e.Message+"]");return;}throw new Exception("Expected rejection: "+contains);}
    static string Players()=>Scalar("SELECT CONCAT((SELECT GROUP_CONCAT(CONCAT_WS(':',a_char_idx,a_slot,a_item_idx,a_count) ORDER BY a_slot) FROM ep4_db.t_inven01),'|',(SELECT GROUP_CONCAT(CONCAT_WS(':',a_char_index,a_recipe_index,a_exp,a_rank) ORDER BY a_recipe_index) FROM ep4_db.t_crafting_mastery),'|',(SELECT GROUP_CONCAT(CONCAT_WS(':',a_char_index,a_item_index,a_exp,a_rank) ORDER BY a_item_index) FROM ep4_db.t_gathering_mastery),'|',(SELECT GROUP_CONCAT(CONCAT_WS(':',a_char_index,a_rank_index)) FROM ep4_db.t_crafting_master_rank),'|',(SELECT GROUP_CONCAT(CONCAT_WS(':',a_char_index,a_effect_index,HEX(a_operation_token),a_acquired_at)) FROM ep4_db.t_alchemy_infusion_unlock),'|',(SELECT GROUP_CONCAT(CONCAT_WS(':',a_index,a_char_index,a_output_item_index,a_output_count,a_costs,a_started_at,a_ready_at,HEX(a_start_token),a_claimed_at)) FROM ep4_db.t_alchemy_work_order))");
    static ProfessionRow Row(CraftingCatalog c,string table,int id)=>c.Professions.Rows(table).Single(r=>r.Int("a_index")==id);
    static void Invalid(CraftingCatalog c,string needle)=>Check(CraftingRules.Validate(c).Any(i=>i.Error&&i.Message.Contains(needle,StringComparison.OrdinalIgnoreCase)),"Reject invalid "+needle);
    [STAThread]static int Main(string[] args)
    {
        if(args.Length!=2){Console.Error.WriteLine("Usage: ProfessionsVerification <game-checkout> <output-dir>");return 2;}
        output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        try {
            using(var admin=new MySqlConnection(Endpoint)){admin.Open();using var cmd=new MySqlCommand("DROP DATABASE IF EXISTS ep4_data;DROP DATABASE IF EXISTS ep4_db;CREATE DATABASE ep4_data CHARACTER SET utf8mb4;CREATE DATABASE ep4_db CHARACTER SET utf8mb4;",admin);cmd.ExecuteNonQuery();}
            using var database=new MySqlConnection(Connection);db=database;db.Open();FixtureContent.Setup(Path.GetFullPath(args[0]),db);
            var catalog=Repository.Load();string players=Players();
            Check(CraftingRepository.Tables.Length==28&&catalog.Professions.Tables.Count==17,"All28 content tables load atomically");
            foreach(var table in ProfessionSchema.Tables) {
                string sql="SELECT GROUP_CONCAT(COLUMN_NAME ORDER BY COLUMN_NAME) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='ep4_data' AND TABLE_NAME='"+table.Name+"'";
                Check(Scalar(sql)==string.Join(',',table.Fields.Select(f=>f.Column).Order(StringComparer.Ordinal)),"Metadata exactly covers "+table.Name);
            }
            Check(Row(catalog,InfusionSchema.Facility,1).Int("a_required_artisan_rank")==3,"station requirement loads the seeded durable Artisan Rank ID");
            var unrestricted=CraftingCatalog.Copy(catalog);Row(unrestricted,InfusionSchema.Facility,1).Set("a_required_artisan_rank",0);
            Repository.Save(catalog,unrestricted);var unrestrictedSaved=Repository.Load();
            Check(Row(unrestrictedSaved,InfusionSchema.Facility,1).Int("a_required_artisan_rank")==0,"unrestricted station requirement persists through normal save/reload");
            Repository.Save(unrestrictedSaved,catalog);catalog=Repository.Load();
            var missingRank=CraftingCatalog.Copy(catalog);Row(missingRank,InfusionSchema.Facility,1).Set("a_required_artisan_rank",900001);
            Reject(()=>Repository.Save(catalog,missingRank),"Artisan Rank");
            var issues=CraftingRules.Validate(catalog);Check(!issues.Any(i=>i.Error),"Canonical28-table content satisfies actual catalogue contracts: "+string.Join(";",issues.Where(i=>i.Error)));
            Check(catalog.Professions.Npcs.Count>0&&catalog.Professions.Zones.Count>0,"Loaded NPC flags and zone metadata");
            var availability=CraftingCatalog.Copy(catalog);var wave=Row(availability,"t_gathering_node",2);wave.Set("a_item_index",777099);
            Check(!availability.IsGatheringAvailable(777099),"Wave base/display item does not falsely create attainable mastery points");
            Check(ProfessionRules.ValidateProfile(catalog.Professions.Rows("t_gathering_profile")[0].Get("a_geometry_hex")) is null,"Canonical imported interaction geometry validates");
            Check(ProfessionRules.ValidateProfile("00000000") is not null,"Malformed imported geometry is rejected");
            var edit=CraftingCatalog.Copy(catalog);edit.Recipes[0].CraftTimeMs+=123;
            Row(edit,"t_gathering_settings",1).Set("a_decline_levels",25);
            Row(edit,"t_gathering_node",1).Set("a_target_radius_bp",1234);
            Row(edit,"t_herb_population",1).Set("a_growth_sec",21);
            Row(edit,"t_fishing_water",1).Set("a_surface_height",155.123456789m);
            Row(edit,"t_fishing_water",1).Set("a_name","Fisher’s 'moat' — quoted text");
            var preciseSite=edit.Professions.Rows("t_herb_spawn_point")[0];preciseSite.Set("a_pos_x",preciseSite.Decimal("a_pos_x")+.00000000123456789m);
            foreach(var vertex in edit.Professions.Rows("t_fishing_water_vertex"))vertex.Set("a_pos_x",vertex.Decimal("a_pos_x")+.123456789m);
            Repository.Save(catalog,edit);var saved=Repository.Load();
            File.WriteAllText(Path.Combine(output,"saved.json"),saved.Fingerprint());File.WriteAllText(Path.Combine(output,"draft.json"),edit.Fingerprint());
            Check(saved.Fingerprint()==edit.Fingerprint(),"Whole28-table save round-trips FLOAT coordinate values");
            Check(Row(edit,"t_fishing_water",1).Get("a_surface_height")==Row(saved,"t_fishing_water",1).Get("a_surface_height"),"Save returns the canonical database FLOAT representation without postcommit reload");
            var immediateSnapshot=CraftingCatalog.Copy(edit);edit.Recipes[0].CraftTimeMs++;Repository.Save(immediateSnapshot,edit);Check(Repository.Load().Fingerprint()==edit.Fingerprint(),"Second local save after fractional coordinate edits has no false conflict");
            var beforeConflict=Repository.Load();Exec("UPDATE t_gathering_settings SET a_decline_levels=a_decline_levels+1 WHERE a_index=1");
            Reject(()=>Repository.Save(beforeConflict,beforeConflict),"changed in another editor");
            saved=Repository.Load();edit=CraftingCatalog.Copy(saved);edit.Recipes[0].CraftTimeMs+=99;edit.Professions.Rows("t_fishing_loot")[0].Set("a_item_count",2);
            Exec("ALTER TABLE t_fishing_loot ADD CONSTRAINT fixture_mid_save CHECK(a_item_count<>2)");string fingerprint=saved.Fingerprint();
            Reject(()=>Repository.Save(saved,edit),"fixture_mid_save");
            Check(Repository.Load().Fingerprint()==fingerprint,"Late SQL failure rolls back recipe edits, all child rewrites and temporary NPC bindings");
            Check(Players()==players,"Failed content transaction preserves inventory, XP, tiers and ranks");Exec("ALTER TABLE t_fishing_loot DROP CONSTRAINT fixture_mid_save");
            saved=Repository.Load();edit=CraftingCatalog.Copy(saved);var a=Row(edit,"t_gathering_node",2);var b=Row(edit,"t_gathering_node",3);string npc=a.Get("a_npc_index");a.Set("a_npc_index",b.Get("a_npc_index"));b.Set("a_npc_index",npc);
            edit.Professions.Rows("t_fishing_loot").Reverse();int slot=0;foreach(var row in edit.Professions.Rows("t_fishing_loot"))row.Set("a_slot",slot++);
            Repository.Save(saved,edit);Check(Repository.Load().Fingerprint()==edit.Fingerprint(),"Unique NPC-binding swaps and reordered loot preserve valid content");
            Exec("INSERT INTO t_item(a_index,a_name_usa,a_enable,a_type_idx,a_subtype_idx,a_flag,a_weight,a_texture_id,a_texture_row,a_texture_col) VALUES(777001,'New mineral',1,4,0,1,9999,0,0,0);INSERT INTO t_npc VALUES(777002,'New node','New node',1024,1)");
            saved=Repository.Load();edit=CraftingCatalog.Copy(saved);edit.Gathering.Add(new(){ItemId=777001,Profession=1,MasteryXp=10,BronzeRequired=50,SilverRequired=150,MasteryRequired=300});
            var newNode=CraftingCatalog.Copy(Row(edit,"t_gathering_node",1));newNode.Set("a_index",10);newNode.Set("a_npc_index",777002);newNode.Set("a_item_index",777001);edit.Professions.Rows("t_gathering_node").Add(newNode);
            Repository.Save(saved,edit);saved=Repository.Load();Check(saved.IsGatheringAvailable(777001)&&saved.Gathering.Any(m=>m.ItemId==777001),"New source and resource mastery commit together");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_gathering_node",10).Set("a_enable",0);Repository.Save(saved,edit);saved=Repository.Load();
            Check(!saved.IsGatheringAvailable(777001)&&saved.Gathering.Any(m=>m.ItemId==777001),"Retirement removes availability while retaining mastery definition");
            edit=CraftingCatalog.Copy(saved);edit.Professions.Rows("t_gathering_node").RemoveAll(r=>r.Int("a_index")==10);Reject(()=>Repository.Save(saved,edit),"Published");
            edit=CraftingCatalog.Copy(saved);edit.Gathering.Single(m=>m.ItemId==777001).Profession=2;Reject(()=>Repository.Save(saved,edit),"profession");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_herb_population",1).Set("a_initial_alive",10000);Invalid(edit,"capacity");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_gathering_node",2).Set("a_rhythm_beats",64);Invalid(edit,"schedule");
            edit=CraftingCatalog.Copy(saved);edit.Professions.Rows("t_gathering_node_loot")[0].Set("a_item_index",777001);Invalid(edit,"matching");
            Check(ProfessionRules.ValidatePolygon(new[]{(0m,0m),(1m,1m),(0m,1m),(1m,0m)}) is not null,"Reject self-intersecting water polygons");
            Check(ProfessionRules.ValidatePolygon(new[]{(0m,0m),(.001m,.001m),(1m,0m)}) is not null,"Reject vertices collapsed by server centimetre quantization");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_fishing_water",1).Set("a_name",new string('界',40));Invalid(edit,"long");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_gathering_node",1).Set("a_hit_delay_ms",1);Invalid(edit,"Reserved");
            edit=CraftingCatalog.Copy(saved);var pool=Row(edit,"t_herb_population",1);pool.Set("a_min_alive",0);pool.Set("a_initial_alive",0);foreach(var site in edit.Professions.Rows("t_herb_spawn_point").Where(r=>r.Int("a_population_index")==1))site.Set("a_enable",0);Invalid(edit,"capacity");
            edit=CraftingCatalog.Copy(saved);var duplicateSite=CraftingCatalog.Copy(edit.Professions.Rows("t_herb_spawn_point")[0]);duplicateSite.Set("a_index",777100);duplicateSite.Set("a_enable",0);edit.Professions.Rows("t_herb_spawn_point").Add(duplicateSite);Invalid(edit,"unique");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_fishing_settings",1).Set("a_rod_item_index",193);Invalid(edit,"rod");
            saved=Repository.Load();edit=CraftingCatalog.Copy(saved);string recipesBefore=System.Text.Json.JsonSerializer.Serialize(saved.Recipes);
            Row(edit,"t_alchemy_offer",1).Set("a_duration_sec",23456);Row(edit,"t_alchemy_offer",1).Set("a_output_count",2);edit.Professions.Rows(InfusionSchema.Ingredients)[0].Set("a_count",21);Row(edit,"t_alchemy_offer",1).Set("a_required_count",275);Row(edit,"t_alchemy_facility",1).Set("a_x",4423);Row(edit,"t_alchemy_facility",1).Set("a_name","Alchemist’s 'station'");
            Repository.Save(saved,edit);saved=Repository.Load();Check(saved.Fingerprint()==edit.Fingerprint(),"infusion offer and native hundredth placement round-trip atomically");
            Check(Row(saved,InfusionSchema.Offers,1).Int("a_duration_sec")==23456&&Row(saved,InfusionSchema.Offers,1).Int("a_output_count")==2&&saved.Professions.Rows(InfusionSchema.Ingredients)[0].Int("a_count")==21,"brew duration, output quantity and secondary costs save/reload together");
            edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(InfusionSchema.Consumables)[0].Set("a_effect_index",2);Reject(()=>Repository.Save(saved,edit),"mapping cannot change");
            edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(InfusionSchema.Consumables).RemoveAt(0);Reject(()=>Repository.Save(saved,edit),"Published");
            edit=CraftingCatalog.Copy(saved);Row(edit,InfusionSchema.Offers,1).Set("a_output_item_index",640125);Invalid(edit,"matching permanent-effect mapping");
            edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(InfusionSchema.Ingredients)[0].Set("a_item_index",640124);Invalid(edit,"ordinary stackable material");
            Check(CraftingItemReferences.Read(Connection,640124).Any(s=>s.Contains("Brewed potion output"))&&CraftingItemReferences.Read(Connection,640124).Any(s=>s.Contains("Permanent potion effect")),"item editor links include brewing outputs and permanent-effect mappings");
            Reject(()=>Exec(CraftingItemReferences.DeleteGuard("ep4_data",640124)+"DELETE FROM t_item WHERE a_index=640124;"),"constraint");
            Check(System.Text.Json.JsonSerializer.Serialize(saved.Recipes)==recipesBefore,"infusion edits preserve every existing recipe and mastery definition");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_alchemy_effect",1).Set("a_skill_index",504);Reject(()=>Repository.Save(saved,edit),"cannot change");
            edit=CraftingCatalog.Copy(saved);edit.Professions.Rows("t_alchemy_offer").RemoveAt(0);Reject(()=>Repository.Save(saved,edit),"Published");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_alchemy_offer",1).Set("a_enabled",0);Repository.Save(saved,edit);saved=Repository.Load();Check(!Row(saved,"t_alchemy_offer",1).Enabled&&saved.Professions.Rows("t_alchemy_effect").Count==4,"retiring an offer retains permanent definitions and earlier unlocks");
            edit=CraftingCatalog.Copy(saved);Row(edit,"t_alchemy_offer",2).Set("a_required_count",276);Row(edit,"t_alchemy_facility",1).Set("a_y",123);
            Exec("ALTER TABLE t_alchemy_facility ADD CONSTRAINT fixture_late_infusion CHECK(a_y<>123)");string infusionBefore=saved.Fingerprint();
            Reject(()=>Repository.Save(saved,edit),"fixture_late_infusion");Check(Repository.Load().Fingerprint()==infusionBefore,"late station failure rolls back earlier offer and all profession writes");Exec("ALTER TABLE t_alchemy_facility DROP CONSTRAINT fixture_late_infusion");
            saved=Repository.Load();edit=CraftingCatalog.Copy(saved);Row(edit,"t_alchemy_offer",2).Set("a_required_count",277);Exec("UPDATE t_skill SET a_flag=a_flag|256 WHERE a_index=505");
            Reject(()=>Repository.Save(saved,edit),"lifecycle");Exec("UPDATE t_skill SET a_flag=a_flag&~256 WHERE a_index=505");Check(Repository.Load().Fingerprint()==saved.Fingerprint(),"save rechecks current read-only potion skill metadata before writing");
            Check(Players()==players,"All valid and invalid authoring leaves player state and acquired infusion tokens byte-equivalent");
            Repository.WithLockedCatalog((snapshot,identity)=> {using var other=new MySqlConnection(Connection);other.Open();using var timeout=new MySqlCommand("SET innodb_lock_wait_timeout=1",other);timeout.ExecuteNonQuery();Reject(()=>{using var change=new MySqlCommand("UPDATE t_fishing_water SET a_cast_ms=a_cast_ms+1 WHERE a_index=1",other);change.ExecuteNonQuery();},"Lock wait timeout");});
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks,tables=CraftingRepository.Tables.Length,live_database_changed=false}));Console.WriteLine($"{checks} profession backend checks passed");return 0;
        } catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=false,checks,error=e.ToString(),live_database_changed=false}));return 1;}
    }
}
