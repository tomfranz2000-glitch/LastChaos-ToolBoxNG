using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using MySqlConnector;

internal static partial class Program
{
    static string TransmutationPlayers()=>Scalar("SELECT CONCAT_WS('|',"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_char_index,a_points,a_built,a_base_index,HEX(a_build_token)) ORDER BY a_char_index) FROM ep4_db.t_transmutation_wallet),"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_index,a_char_index,a_offer_index,a_output_item_index,a_output_count,a_point_cost,a_duration_sec,a_quantity,a_claimed,a_started_at,a_ready_at,HEX(a_start_token)) ORDER BY a_index) FROM ep4_db.t_transmutation_work_order),"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_char_index,HEX(a_token),a_kind,a_job_index,a_units,a_created_at) ORDER BY a_char_index,a_token) FROM ep4_db.t_transmutation_receipt),"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_char_index,HEX(a_token),a_slot,a_item_index,a_count,a_serial) ORDER BY a_char_index,a_token,a_slot) FROM ep4_db.t_transmutation_delivery))");

    static void CheckTransmutationDatabase()
    {
        var before=Repository.Load();string players=Players();
        Check(!TransmutationRules.Validate(before).Any(i=>i.Error),"loaded real SQL Transmutation catalogue satisfies matching editor rules");
        Check(!before.Ranks.Single(r=>r.Id==2).Bonuses.Any(b=>b.Effect==5&&b.Amount==50),"real migration replaced only shipped RankII MP placeholder");
        Check(TransmutationPlayers().Contains("fixture-paid-transmutation".Select(ch=>((int)ch).ToString("X2")).Aggregate("",(a,b)=>a+b)),"saved player queue sentinel is part of preservation snapshots");
        var edit=CraftingCatalog.Copy(before);Row(edit,TransmutationSchema.Settings,1).Set("a_capacity",12345);Row(edit,TransmutationSchema.Settings,1).Set("a_points_numerator",3);Row(edit,TransmutationSchema.Settings,1).Set("a_points_denominator",2);
        Row(edit,TransmutationSchema.Offers,1).Set("a_point_cost",1250);Row(edit,TransmutationSchema.Offers,1).Set("a_duration_sec",180);Row(edit,TransmutationSchema.Offers,1).Set("a_enabled",0);
        Row(edit,TransmutationSchema.Facility,1).Set("a_required_artisan_rank",0);Row(edit,TransmutationSchema.Facility,1).Set("a_plot_name","Crafter’s 'unbuilt plot'");Row(edit,TransmutationSchema.Facility,1).Set("a_use_radius",425);
        edit.Professions.Rows(TransmutationSchema.ConstructionCosts)[0].Set("a_count",2);
        var exception=ProfessionSchema.NewRow(TransmutationSchema.ItemRules);exception.Set("a_item_index",999991);exception.Set("a_points_override",300);edit.Professions.Rows(TransmutationSchema.ItemRules).Add(exception);
        Repository.Save(before,edit);var saved=Repository.Load();
        Check(saved.Fingerprint()==edit.Fingerprint(),"reservoir formula, output terms, exception, rank, Unicode name, native units and costs save together");
        Check(Players()==players,"successful Crucible content save preserves exact wallet, paid order, ownership, delivery and receipts");
        Check(CraftingCatalog.Copy(saved).Fingerprint()==saved.Fingerprint(),"all Transmutation fields survive canonical draft JSON roundtrip");
        Check(CraftingItemReferences.Read(Connection,85).Any(s=>s.Contains("Transmutation output"))&&CraftingItemReferences.Read(Connection,999991).Any(s=>s.Contains("Transmutation equipment exception")),"Item Editor reports new output and equipment override references");
        Check(CraftingItemReferences.Read(Connection,198).Any(s=>s.Contains("Crucible construction material")),"Item Editor reports Crucible construction references");
        Reject(()=>Repository.Save(before,before),"changed in another editor");
        string fingerprint=saved.Fingerprint();Exec("ALTER TABLE t_transmutation_facility_ingredient ADD CONSTRAINT fixture_late_transmutation CHECK(a_count<>17)");
        edit=CraftingCatalog.Copy(saved);Row(edit,TransmutationSchema.Settings,1).Set("a_capacity",1);Row(edit,TransmutationSchema.Offers,1).Set("a_duration_sec",240);edit.Professions.Rows(TransmutationSchema.ConstructionCosts)[0].Set("a_count",17);
        Reject(()=>Repository.Save(saved,edit),"fixture_late_transmutation");
        Check(Repository.Load().Fingerprint()==fingerprint&&Players()==players,"late actual SQL failure rolls back point settings and rewritten offers/rules/costs while preserving player state");
        Exec("ALTER TABLE t_transmutation_facility_ingredient DROP CONSTRAINT fixture_late_transmutation");
        edit=CraftingCatalog.Copy(saved);Row(edit,TransmutationSchema.Offers,1).Set("a_output_item_index",156);Reject(()=>Repository.Save(saved,edit),"upgrading material");
        edit=CraftingCatalog.Copy(saved);Row(edit,TransmutationSchema.Facility,1).Set("a_required_artisan_rank",999999);Reject(()=>Repository.Save(saved,edit),"Artisan Rank");
        edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(TransmutationSchema.ItemRules)[0].Set("a_item_index",156);Reject(()=>Repository.Save(saved,edit),"ordinary upgradeable");
        edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(TransmutationSchema.ItemRules)[0].Set("a_enabled",0);edit.Professions.Rows(TransmutationSchema.ItemRules)[0].Set("a_item_index",156);Repository.Save(saved,edit);saved=Repository.Load();
        Check(saved.Professions.Rows(TransmutationSchema.ItemRules).Single().Int("a_item_index")==156&&!saved.Professions.Rows(TransmutationSchema.ItemRules).Single().Enabled,"explicit exclusions can target non-equipment without granting conversion permission");
        edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(TransmutationSchema.Offers).Clear();edit.Professions.Rows(TransmutationSchema.ItemRules).Clear();edit.Professions.Rows(TransmutationSchema.ConstructionCosts).Clear();Repository.Save(saved,edit);saved=Repository.Load();
        Check(saved.Fingerprint()==edit.Fingerprint()&&Players()==players,"removing the final offer, override and build cost persists without touching the saved paid queue");
        Repository.Save(saved,before);saved=Repository.Load();
        Check(saved.Fingerprint()==before.Fingerprint()&&Players()==players,"restoring authored output definitions restores exact content without rewinding player progression");
        Repository.WithLockedCatalog((snapshot,identity)=>{using var other=new MySqlConnection(Connection);other.Open();using var timeout=new MySqlCommand("SET innodb_lock_wait_timeout=1",other);timeout.ExecuteNonQuery();Reject(()=>{using var change=new MySqlCommand("UPDATE t_transmutation_settings SET a_capacity=a_capacity+1",other);change.ExecuteNonQuery();},"Lock wait timeout");});
        Exec("UPDATE t_transmutation_settings SET a_capacity=9999");Reject(()=>Repository.Save(saved,saved),"changed in another editor");Exec("UPDATE t_transmutation_settings SET a_capacity=10000");
        Check(Repository.Load().Fingerprint()==before.Fingerprint()&&Players()==players,"external Transmutation edits participate in optimistic conflict detection and preserve all player sentinels");
    }
}
