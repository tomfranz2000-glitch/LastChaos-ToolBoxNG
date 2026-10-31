using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using LastChaos_ToolBoxNG.ContentRecording;

static partial class Program
{
    static void CheckAccessoryDisplayRules(CraftingCatalog c)
    {
        Check(!AccessoryDisplayRules.Validate(c).Any(i=>i.Error),"authored Accessory Display content passes validation");
        Check(c.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Int("a_required_artisan_rank")==4,"display construction uses permanent Artisan Rank IV identity");
        Check(AccessoryDisplaySchema.Tables.All(t=>ContentRecordingPolicy.AllowlistedTables.Any(p=>p.Key=="ep4_data/"+t.Name)),"display configuration and costs participate in content recording");
        foreach(string table in new[]{"t_accessory_display","t_accessory_display_item","t_accessory_display_receipt","t_accessory_display_future_progress"})
            Check(!ContentRecordingPolicy.AllowlistedTables.Any(p=>p.Table==table)&&ContentRecordingPolicy.IsPlayerOwnedProfessionTable(table),"recording protects character-owned "+table);
        CraftingCatalog Draft(){var copy=CraftingCatalog.Copy(c);copy.Items=new(c.Items);copy.Professions.Infusions=c.Professions.Infusions;return copy;}
        foreach(var mutation in new Action<CraftingCatalog>[] {
            d=>d.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Set("a_required_artisan_rank",int.MaxValue),
            d=>d.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Set("a_model",@"Data\..\bad.smc"),
            d=>d.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Set("a_name",new string('X',65)),
            d=>d.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).First().Set("a_item_index",19),
            d=>d.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).Last().Set("a_slot",7)}) {
            var draft=Draft();mutation(draft);Check(AccessoryDisplayRules.Validate(draft).Any(i=>i.Error),"invalid display content is rejected before Save");
        }
        var free=Draft();free.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).Clear();
        Check(!AccessoryDisplayRules.Validate(free).Any(i=>i.Error),"empty display cost list intentionally permits free construction");
        var unrestricted=Draft();unrestricted.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Set("a_required_artisan_rank",0);
        Check(!AccessoryDisplayRules.Validate(unrestricted).Any(i=>i.Error),"display construction can be unrestricted without changing its permanent identity");
        var retired=Draft();retired.Ranks.Add(new MastersRank {Id=900001,Order=900002,Name="Retired display milestone",Enabled=false});
        retired.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Set("a_required_artisan_rank",900001);
        Check(!AccessoryDisplayRules.Validate(retired).Any(i=>i.Error),"display construction references retired rank IDs independently of display order");
        var duplicate=Draft();var extra=CraftingCatalog.Copy(duplicate.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts)[0]);extra.Set("a_slot",2);duplicate.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).Add(extra);
        Check(AccessoryDisplayRules.Validate(duplicate).Any(i=>i.Error&&i.Message.Contains("duplicate")),"display cannot charge the same construction item twice");
        foreach(string column in new[]{"a_x","a_y","a_z","a_heading","a_use_radius"}) {
            var bad=Draft();bad.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Set(column,int.MaxValue);
            Check(ProfessionRules.Validate(bad).Any(i=>i.Error),"display native placement bounds enforced: "+column);
        }
    }
    static void CheckAccessoryDisplayControls(CraftingMasteryEditor editor,CraftingCatalog c,Action<int,string> navigate,Func<int,string,Control> detail,Action<string,Size> capture)
    {
        string initial=c.Fingerprint();navigate(7,AccessoryDisplaySchema.Facility);
        var panel=detail(7,AccessoryDisplaySchema.Facility);var x=(NumericUpDown)panel.Controls.Find("a_x",true).Single();decimal old=x.Value;x.Value=40.25m;
        Check(c.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Int("a_x")==4025,"display placement control presents metres and stores native hundredths");x.Value=old;
        var rank=(ComboBox)panel.Controls.Find("a_required_artisan_rank",true).Single();
        Check(rank.Text.Contains("Artisan Rank IV")&&rank.Text.Contains("ID 4"),"display construction uses the normal named Artisan rank picker");
        rank.SelectedIndex=0;Check(c.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Int("a_required_artisan_rank")==0,"display None selection edits the draft construction gate");
        rank.SelectedIndex=4;Check(c.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Int("a_required_artisan_rank")==4,"restoring Rank IV preserves its durable ID");
        var enabled=(CheckBox)panel.Controls.Find("a_enabled",true).Single();enabled.Checked=false;
        Check(!c.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Enabled&&c.Professions.Rows(AccessoryDisplaySchema.Facility).Single().Int("a_construction_enabled")==1,"display deposit and construction switches remain independent");enabled.Checked=true;
        navigate(7,AccessoryDisplaySchema.ConstructionCosts);panel=detail(7,AccessoryDisplaySchema.ConstructionCosts);
        var count=(NumericUpDown)panel.Controls.Find("a_count",true).Single();old=count.Value;count.Value=2;
        Check(c.Professions.Rows(AccessoryDisplaySchema.ConstructionCosts).Any(r=>r.Int("a_count")==2),"display material editor changes the real one-time cost");count.Value=old;
        foreach(var size in new[]{new Size(1420,900),new Size(1100,720)})foreach(string table in new[]{AccessoryDisplaySchema.Facility,AccessoryDisplaySchema.ConstructionCosts}) {
            navigate(7,table);capture("accessory-display-"+table+"-"+size.Width+"x"+size.Height,size);var fit=(Panel)detail(7,table);
            Check(!fit.HorizontalScroll.Visible&&fit.RectangleToScreen(fit.ClientRectangle).Right<=editor.RectangleToScreen(editor.ClientRectangle).Right,"display page fits viewport: "+table+" at "+size.Width+"×"+size.Height);
        }
        Call(editor,"SelectNavigation","Artisan Ranks");var tabs=Field<TabControl>(editor,"tabs");tabs.SelectedIndex=2;var ranks=Field<DataGridView>(editor,"ranks");ranks.CurrentCell=ranks.Rows[3].Cells[0];Application.DoEvents();
        var rankTabs=(TabControl)editor.Controls.Find("ArtisanRankDetails",true).Single();rankTabs.SelectedIndex=2;Application.DoEvents();
        Check(All(rankTabs).OfType<Label>().Any(l=>l.Text=="Allows construction of Accessory Display"),"Artisan Rank IV editor includes the real display construction reward");
        capture("accessory-display-rankIV-construction",new Size(1100,720));All(rankTabs).OfType<Button>().Single(b=>b.Text=="Edit Accessory Display").PerformClick();Application.DoEvents();
        Check(tabs.SelectedTab?.Name=="Profession7","rank reward shortcut opens Accessory Display settings");
        Check(c.Fingerprint()==initial,"display control checks preserve the entire original catalogue");
    }
}
