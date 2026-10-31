using System.Windows.Forms;
using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using LastChaos_ToolBoxNG.ContentRecording;

static partial class Program
{
    static void CheckTransmutationRules(CraftingCatalog c)
    {
        CraftingCatalog Draft(){var copy=CraftingCatalog.Copy(c);copy.Items=new(c.Items);copy.Professions.Infusions=c.Professions.Infusions;return copy;}
        ProfessionRow Row(CraftingCatalog draft,string table)=>draft.Professions.Rows(table).First();
        void Invalid(Action<CraftingCatalog> change,string label){var draft=Draft();change(draft);Check(ProfessionRules.Validate(draft).Any(i=>i.Error),label);}
        Check(!TransmutationRules.Validate(c).Any(i=>i.Error),"actual Transmutation content satisfies the coordinated game eligibility rules");
        Check(!c.Ranks.Single(r=>r.Id==2).Bonuses.Any(b=>b.Effect==5&&b.Amount==50),"Rank II's exact MP placeholder is replaced by construction permission");
        Check(Row(c,TransmutationSchema.Settings).Int("a_capacity")==10000&&Row(c,TransmutationSchema.Facility).Int("a_required_artisan_rank")==2,"initial reservoir and named construction rank come from authored content");
        Check(Row(c,TransmutationSchema.Offers).Int("a_output_item_index")==85&&Row(c,TransmutationSchema.Offers).Int("a_point_cost")==1000&&Row(c,TransmutationSchema.Offers).Int("a_duration_sec")==120,"initial Heaven Stone terms are exactly1000 points and120seconds");
        Check(TransmutationRules.FormulaPoints(100,1,1)==100&&TransmutationRules.FormulaPoints(100,3,2)==150&&TransmutationRules.FormulaPoints(1,1,1000000)==1,"level formula is rational, floors and retains the one-point minimum");
        Check(TransmutationSchema.Tables.All(t=>ContentRecordingPolicy.AllowlistedTables.Any(p=>p.Key=="ep4_data/"+t.Name)),"all five authored Crucible tables are recordable");
        foreach(string player in new[]{"t_transmutation_wallet","t_transmutation_work_order","t_transmutation_receipt","t_transmutation_delivery","t_transmutation_future_player_state"})
            Check(ContentRecordingPolicy.IsPlayerOwnedProfessionTable(player)&&!ContentRecordingPolicy.AllowlistedTables.Any(p=>p.Table==player),"explicit recording boundary excludes "+player);
        foreach(int capacity in new[]{0,1000000001})Invalid(d=>Row(d,TransmutationSchema.Settings).Set("a_capacity",capacity),"capacity bounds rejected: "+capacity);
        foreach(string field in new[]{"a_points_numerator","a_points_denominator"})foreach(int value in new[]{0,1000001})Invalid(d=>Row(d,TransmutationSchema.Settings).Set(field,value),"formula bound rejected: "+field+"="+value);
        Invalid(d=>Row(d,TransmutationSchema.Offers).Set("a_output_item_index",156),"ordinary crafting resources cannot be offered as upgrading materials");
        Invalid(d=>Row(d,TransmutationSchema.Offers).Set("a_output_count",c.Items[85].StackLimit+1),"output quantity cannot exceed the actual Heaven Stone stack limit");
        foreach(string field in new[]{"a_point_cost","a_duration_sec"})Invalid(d=>Row(d,TransmutationSchema.Offers).Set(field,0),"zero price/time rejected: "+field);
        Invalid(d=>Row(d,TransmutationSchema.Facility).Set("a_required_artisan_rank",999999),"missing construction rank rejected");
        var retired=Draft();retired.Ranks.Single(r=>r.Id==2).Enabled=false;Check(!TransmutationRules.Validate(retired).Any(i=>i.Error),"retired stable rank identities remain valid construction references");
        foreach(string model in new[]{@"C:\outside.smc",@"Data\..\outside.smc",@"Data\World\ä.smc",@"Data\World\bad.SMC"})Invalid(d=>Row(d,TransmutationSchema.Facility).Set("a_plot_model",model),"unsafe/mismatched plot path rejected: "+model);
        Invalid(d=>{var cost=CraftingCatalog.Copy(Row(d,TransmutationSchema.ConstructionCosts));cost.Set("a_slot",2);d.Professions.Rows(TransmutationSchema.ConstructionCosts).Add(cost);},"duplicate construction material rejected");
        var free=Draft();free.Professions.Rows(TransmutationSchema.ConstructionCosts).Clear();Check(!TransmutationRules.Validate(free).Any(i=>i.Error),"free future construction does not remove previously owned buildings");
        var removed=Draft();removed.Professions.Rows(TransmutationSchema.Offers).Clear();removed.Professions.Rows(TransmutationSchema.ItemRules).Clear();ProfessionStorage.ValidatePublished(c.Professions,removed.Professions);
        Check(!TransmutationRules.Validate(removed).Any(i=>i.Error),"output and exception removal allowed because paid terms are pinned separately");
        var excluded=Draft();var rule=ProfessionSchema.NewRow(TransmutationSchema.ItemRules);rule.Set("a_item_index",156);rule.Set("a_enabled",0);excluded.Professions.Rows(TransmutationSchema.ItemRules).Add(rule);
        Check(!TransmutationRules.Validate(excluded).Any(i=>i.Error),"an exclusion can target any existing item");rule.Set("a_enabled",1);Check(TransmutationRules.Validate(excluded).Any(i=>i.Error),"an enabled exception cannot turn a resource into equipment");
        var disabled=Draft();disabled.Items[156]=disabled.Items[156] with {Enabled=false};var disabledRule=ProfessionSchema.NewRow(TransmutationSchema.ItemRules);disabledRule.Set("a_item_index",156);disabledRule.Set("a_enabled",0);disabled.Professions.Rows(TransmutationSchema.ItemRules).Add(disabledRule);Check(TransmutationRules.Validate(disabled).Any(i=>i.Error&&i.Message.Contains("disabled prototypes")),"exclusions cannot reference disabled prototypes absent from the live game catalogue");
        var ordinary=Draft();int synthetic=999991;ordinary.Items[synthetic]=new(synthetic,"Fixture linked-capable armor",true,1,0,4|4096,1,0,0,0,false);
        ordinary.Professions.Infusions=new InfusionMetadata();ordinary.Professions.Infusions.MaxUses[synthetic]=-1;
        rule=ProfessionSchema.NewRow(TransmutationSchema.ItemRules);rule.Set("a_item_index",synthetic);rule.Set("a_points_override",900);ordinary.Professions.Rows(TransmutationSchema.ItemRules).Add(rule);
        // Validate only the exception under test: the copied dictionary above deliberately omits unrelated output metadata.
        Check(!TransmutationRules.Validate(ordinary).Any(i=>i.Error&&i.Target.StartsWith("Equipment exception")),"COMPOSITE capability does not falsely reject ordinary unlinked equipment");
        ordinary.Items[synthetic]=ordinary.Items[synthetic] with {Flags=4|256};Check(TransmutationRules.Validate(ordinary).Any(i=>i.Error&&i.Target.StartsWith("Equipment exception")),"cash equipment cannot be force-enabled by a point override");
    }

    static void CheckTransmutationControls(CraftingMasteryEditor editor,CraftingCatalog c,Action<int,string> navigate,Func<int,string,Control> detail,Action<string,Size> capture)
    {
        string initial=c.Fingerprint();
        navigate(6,TransmutationSchema.Settings);var settings=detail(6,TransmutationSchema.Settings);var capacity=(NumericUpDown)settings.Controls.Find("a_capacity",true).Single();decimal oldCap=capacity.Value;capacity.Value=12345;
        Check(c.Professions.Rows(TransmutationSchema.Settings).Single().Int("a_capacity")==12345,"actual reservoir control updates the correct authored field");
        Check(All(settings).OfType<Label>().Any(l=>l.Name=="TransmutationExplanation"&&l.Text.Contains(12345.ToString("N0"))),"reservoir explanation refreshes with point-cap edits");capacity.Value=oldCap;
        var numerator=(NumericUpDown)settings.Controls.Find("a_points_numerator",true).Single();numerator.Value=3;
        Check(All(settings).OfType<Label>().Any(l=>l.Name=="TransmutationExplanation"&&l.Text.Contains("300")),"formula preview recalculates the level100 equipment value");numerator.Value=1;
        navigate(6,TransmutationSchema.Offers);var offers=detail(6,TransmutationSchema.Offers);var minutes=(NumericUpDown)offers.Controls.Find("a_duration_sec_minutes",true).Single();decimal oldMinutes=minutes.Value;minutes.Value=3;
        Check(c.Professions.Rows(TransmutationSchema.Offers).First().Int("a_duration_sec")==180,"minute control edits real per-operation seconds");
        Check(All(offers).OfType<Label>().Any(l=>l.Name=="TransmutationExplanation"&&l.Text.Contains("15 min")),"quantity preview shows sequential five-operation duration");minutes.Value=oldMinutes;
        var price=(NumericUpDown)offers.Controls.Find("a_point_cost",true).Single();decimal oldPrice=price.Value;price.Value=1200;
        Check(All(offers).OfType<Label>().Any(l=>l.Name=="TransmutationExplanation"&&l.Text.Contains(6000.ToString("N0"))),"price preview makes immediate total point spending explicit");price.Value=oldPrice;
        navigate(6,TransmutationSchema.Facility);var facility=detail(6,TransmutationSchema.Facility);var rank=(ComboBox)facility.Controls.Find("a_required_artisan_rank",true).Single();
        Check(rank.Text.Contains("Artisan Rank II")&&rank.Text.Contains("ID 2"),"Crucible uses a named stable rank selector rather than an unexplained number");
        rank.SelectedIndex=0;Check(c.Professions.Rows(TransmutationSchema.Facility).Single().Int("a_required_artisan_rank")==0,"None removes the construction rank requirement in the draft");
        rank.SelectedIndex=2;Check(c.Professions.Rows(TransmutationSchema.Facility).Single().Int("a_required_artisan_rank")==2,"selecting RankII restores its permanent identity");
        var radius=(NumericUpDown)facility.Controls.Find("a_use_radius",true).Single();radius.Value=4.25m;Check(c.Professions.Rows(TransmutationSchema.Facility).Single().Int("a_use_radius")==425,"Crucible range edits native hundredths through readable metres");radius.Value=4;
        var build=(CheckBox)facility.Controls.Find("a_construction_enabled",true).Single();build.Checked=false;
        Check(c.Professions.Rows(TransmutationSchema.Facility).Single().Int("a_construction_enabled")==0&&c.Professions.Rows(TransmutationSchema.Facility).Single().Enabled,"construction switch does not disable existing-machine activity");build.Checked=true;
        Call(editor,"SelectProfessionChild",6,TransmutationSchema.ConstructionCosts,1);Application.DoEvents();
        var costs=detail(6,TransmutationSchema.ConstructionCosts);var amount=(NumericUpDown)costs.Controls.Find("a_count",true).Single();decimal oldAmount=amount.Value;amount.Value=2;
        Check(c.Professions.Rows(TransmutationSchema.ConstructionCosts).Any(r=>r.Int("a_count")==2),"actual Crucible construction material control edits one-time cost");amount.Value=oldAmount;
        Check(c.Fingerprint()==initial,"editing and restoring Crucible controls returns the full catalogue fingerprint exactly");
        var exception=ProfessionSchema.NewRow(TransmutationSchema.ItemRules);exception.Set("a_item_index",156);exception.Set("a_enabled",0);c.Professions.Rows(TransmutationSchema.ItemRules).Add(exception);Call(editor,"RefreshProfessionPages");
        navigate(6,TransmutationSchema.ItemRules);var exceptions=detail(6,TransmutationSchema.ItemRules);var pointOverride=(NumericUpDown)exceptions.Controls.Find("a_points_override",true).Single();pointOverride.Value=777;
        Check(exception.Int("a_points_override")==777&&All(exceptions).OfType<Label>().Any(l=>l.Name=="TransmutationExplanation"&&l.Text.Contains("cannot be converted")),"exception controls keep exclusions explicit while editing fixed value");
        foreach(var size in new[]{new Size(1420,900),new Size(1100,720)})foreach(string table in new[]{TransmutationSchema.Settings,TransmutationSchema.Offers,TransmutationSchema.Facility,TransmutationSchema.ConstructionCosts,TransmutationSchema.ItemRules}) {
            navigate(6,table);capture("transmutation-"+table+"-"+size.Width+"x"+size.Height,size);var panel=(Panel)detail(6,table);
            Check(!panel.HorizontalScroll.Visible&&panel.RectangleToScreen(panel.ClientRectangle).Right<=editor.RectangleToScreen(editor.ClientRectangle).Right,"Crucible page fits viewport: "+table+" at "+size.Width+"×"+size.Height);
        }
        c.Professions.Rows(TransmutationSchema.ItemRules).Remove(exception);Call(editor,"RefreshProfessionPages");
        navigate(6,TransmutationSchema.ItemRules);capture("transmutation-empty-exceptions",new Size(1100,720));
        Check(!((Panel)detail(6,TransmutationSchema.ItemRules)).HorizontalScroll.Visible,"empty equipment exceptions wrap at the minimum supported width");
        Call(editor,"SelectNavigation","Artisan Ranks");var tabs=Field<TabControl>(editor,"tabs");tabs.SelectedIndex=2;var ranks=Field<DataGridView>(editor,"ranks");ranks.CurrentCell=ranks.Rows[1].Cells[0];Application.DoEvents();
        var rankTabs=(TabControl)editor.Controls.Find("ArtisanRankDetails",true).Single();rankTabs.SelectedIndex=2;Application.DoEvents();
        Check(All(rankTabs).OfType<Label>().Any(l=>l.Text=="Allows construction of Transmutation Crucible"),"RankII editor shows its real facility reward instead of an empty stat placeholder");
        capture("transmutation-rankII-construction",new Size(1100,720));
        All(rankTabs).OfType<Button>().Single(b=>b.Text=="Edit Transmutation Crucible").PerformClick();Application.DoEvents();
        Check(tabs.SelectedTab?.Name=="Profession6","rank facility action opens the actual Crucible settings page");
        Check(c.Fingerprint()==initial,"all Transmutation UI checks leave authored content exactly restored");
    }
}
