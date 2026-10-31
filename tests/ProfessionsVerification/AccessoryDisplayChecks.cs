using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using MySqlConnector;

internal static partial class Program
{
    static string AccessoryDisplayPlayers()=>Scalar("SELECT CONCAT_WS('|',"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_char_index,a_built,a_base_index,HEX(a_build_token)) ORDER BY a_char_index) FROM ep4_db.t_accessory_display),"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_char_index,a_item_index,a_plus,a_flag,a_used,a_used2,a_serial,a_socket_count,a_now_durability,a_max_durability,a_option0,a_option1,a_option2,a_option3,a_option4,a_origin0,a_origin1,a_origin2,a_origin3,a_origin4,a_origin5,a_socket0,a_socket1,a_socket2,a_socket3,a_socket4,a_socket5,a_socket6) ORDER BY a_char_index) FROM ep4_db.t_accessory_display_item),"
        +"(SELECT JSON_ARRAYAGG(JSON_ARRAY(a_char_index,HEX(a_token),a_kind,a_created_at) ORDER BY a_char_index,a_token) FROM ep4_db.t_accessory_display_receipt))");

    static void CheckAccessoryDisplayDatabase()
    {
        var before=Repository.Load();string players=Players();
        Check(!AccessoryDisplayRules.Validate(before).Any(i=>i.Error),"real Accessory Display SQL content satisfies editor contracts");
        Check(AccessoryDisplayPlayers().Contains("fixture-displayed-accessory"),"exact displayed item participates in player preservation snapshots");
        Check(before.Ranks.Single(r=>r.Id==4).RequiredMastered==15,"actual Rank IV threshold loads into the normal rank editor");
        var edit=CraftingCatalog.Copy(before);var display=Row(edit,AccessoryDisplaySchema.Facility,1);
        display.Set("a_required_artisan_rank",0);display.Set("a_name","Artisan’s 'display'");display.Set("a_x",4025);
        display.Set("a_enabled",0);display.Set("a_construction_enabled",1);display.Set("a_use_radius",425);
        edit.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts)[0].Set("a_count",3);
        Repository.Save(before,edit);var saved=Repository.Load();
        Check(saved.Fingerprint()==edit.Fingerprint(),"display name, native units, separate availability switches, rank and materials save together");
        Check(Players()==players,"display content save preserves exact item rolls, sockets, durability, serial, ownership and receipts");
        Check(CraftingItemReferences.Read(Connection,198).Any(s=>s.Contains("Display construction material")),"Item Editor reports Display construction references");
        Reject(()=>Repository.Save(before,before),"changed in another editor");
        Exec("ALTER TABLE t_accessory_display_facility_ingredient ADD CONSTRAINT fixture_late_display CHECK(a_count<>17)");
        edit=CraftingCatalog.Copy(saved);Row(edit,AccessoryDisplaySchema.Facility,1).Set("a_plot_name","Must roll back");
        edit.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts)[0].Set("a_count",17);
        Reject(()=>Repository.Save(saved,edit),"fixture_late_display");
        Check(Repository.Load().Fingerprint()==saved.Fingerprint()&&Players()==players,"late display material constraint rolls back earlier facility writes without touching escrow");
        Exec("ALTER TABLE t_accessory_display_facility_ingredient DROP CONSTRAINT fixture_late_display");
        edit=CraftingCatalog.Copy(saved);edit.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).Clear();Repository.Save(saved,edit);var free=Repository.Load();
        Check(free.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).Count==0&&Players()==players,"empty display costs persist as free construction while owned items remain intact");
        Repository.Save(free,before);saved=Repository.Load();
        Check(saved.Fingerprint()==before.Fingerprint()&&Players()==players,"restoring display terms restores exact content without rewinding stored equipment");
        Repository.WithLockedCatalog((snapshot,identity)=>{using var other=new MySqlConnection(Connection);other.Open();using var timeout=new MySqlCommand("SET innodb_lock_wait_timeout=1",other);timeout.ExecuteNonQuery();Reject(()=>{using var change=new MySqlCommand("UPDATE t_accessory_display_facility SET a_enabled=1-a_enabled",other);change.ExecuteNonQuery();},"Lock wait timeout");});
    }
}
