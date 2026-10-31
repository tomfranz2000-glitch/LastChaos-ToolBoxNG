using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private static void ClearPanel(Panel p) { foreach(Control c in p.Controls.Cast<Control>().ToArray()) { p.Controls.Remove(c); c.Dispose(); } }
    private static FlowLayoutPanel Fields() => new() { Dock=DockStyle.Fill, AutoScroll=true, FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new(8) };
    private void Number(FlowLayoutPanel parent,string text,decimal min,decimal max,decimal value,Action<decimal> set,int decimals=0)
    {
        var row=Flow(); row.Dock=DockStyle.None;
        var number=new NumericUpDown { Minimum=Math.Min(min,value), Maximum=Math.Max(max,value), DecimalPlaces=decimals, Increment=decimals>0?(decimal)Math.Pow(10,-decimals):1, Width=145, Value=value, ThousandsSeparator=true };
        row.Controls.Add(Label(text)); row.Controls.Add(number); parent.Controls.Add(row);
        number.ValueChanged+=(_,_)=> { set(number.Value); Changed(); };
    }
    private void Check(FlowLayoutPanel parent,string text,bool value,Action<bool> set)
    { var check=new CheckBox { Text=text, AutoSize=true, Checked=value, Margin=new(8) }; parent.Controls.Add(check); check.CheckedChanged+=(_,_)=> { set(check.Checked); Changed(); }; }
    private void TextField(FlowLayoutPanel parent,string text,string value,Action<string> set)
    { var row=Flow(); row.Dock=DockStyle.None; var input=new TextBox { Width=380, Text=value }; row.Controls.AddRange([Label(text),input]); parent.Controls.Add(row); input.TextChanged+=(_,_)=> { set(input.Text); Changed(); }; }
    private void ShowRecipe()
    {
        // The summary control belongs to this panel; detach it before disposing
        // the old detail tree so it can be reused without retaining old grids.
        recipeSummary.Parent?.Controls.Remove(recipeSummary);
        ClearPanel(recipeDetail); detailGrids.RemoveAll(g=>g.IsDisposed);
        if(selectedRecipe is not Recipe r) { recipeDetail.Controls.Add(Label("Choose a recipe or create one.")); return; }
        var detailTabs=new TabControl { Dock=DockStyle.Fill };
        var fields=Fields(); var page=new TabPage("Recipe"); page.Controls.Add(fields); detailTabs.TabPages.Add(page);
        fields.Controls.Add(Label($"Recipe #{r.Id} — permanent ID (retire instead of deleting)"));
        var professions=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=180 };
        professions.Items.AddRange(Enum.GetValues<Profession>().Cast<object>().ToArray()); professions.SelectedItem=r.Profession;
        var row=Flow(); row.Dock=DockStyle.None; row.Controls.AddRange([Label("Profession"),professions]); fields.Controls.Add(row);
        professions.SelectedIndexChanged+=(_,_)=> { r.Profession=(Profession)professions.SelectedItem!; Changed(); };
        Check(fields,"Enabled — available in the game",r.Enabled,v=>r.Enabled=v);
        var outputName=Label(catalog.ItemName(r.OutputItem));
        var outputIcon=new PictureBox { Image=ItemIcon(r.OutputItem), Size=new(36,36), SizeMode=PictureBoxSizeMode.Zoom };
        Number(fields,"Output item ID",0,int.MaxValue,r.OutputItem,v=> { r.OutputItem=(int)v; outputName.Text=catalog.ItemName(r.OutputItem); outputIcon.Image=ItemIcon(r.OutputItem); });
        var output=Flow(); output.Dock=DockStyle.None;
        output.Controls.Add(outputIcon);
        output.Controls.AddRange([outputName,Button("Choose output",()=> { int? id=PickItem(r.OutputItem); if(id.HasValue) { r.OutputItem=id.Value; Changed(); ShowRecipe(); } }),Button("Open item",()=>OpenItem(r.OutputItem)),Button("Where used",()=>ShowText("Where used",string.Join("\r\n",catalog.WhereUsed(r.OutputItem))))]); fields.Controls.Add(output);
        Number(fields,"Output quantity",1,int.MaxValue,r.OutputQuantity,v=>r.OutputQuantity=(int)v);
        Number(fields,"Craft time (seconds)",0.001m,600,r.CraftTimeMs/1000m,v=>r.CraftTimeMs=(int)(v*1000),3);
        Number(fields,"Display order",0,int.MaxValue,r.SortOrder,v=>r.SortOrder=(int)v);

        page=new("Ingredients"); page.Controls.Add(ItemGrid(r.Ingredients,()=>new Ingredient(),10)); detailTabs.TabPages.Add(page);
        page=new("Learning"); var learning=Fields();
        Check(learning,"Requires a teaching item (unchecked = automatically known)",r.RequiresUnlock,v=>r.RequiresUnlock=v);
        learning.Controls.Add(Label("One manual may teach many recipes; each recipe supports up to eight manuals.\r\nAutomatic recipes must have an empty teaching list. A successful first use consumes one manual."));
        page.Controls.Add(Vertical(learning,ItemGrid(r.Manuals,()=>new TeachingItem(),8))); learning.Dock=DockStyle.Top; learning.AutoSize=true; detailTabs.TabPages.Add(page);
        page=new("Skill & Mastery"); fields=Fields(); fields.Dock=DockStyle.Top; fields.AutoSize=true;
        Number(fields,"Minimum skill to craft",1,CraftingRules.MaxSkillLevel,r.RequiredSkill,v=>r.RequiredSkill=(int)v);
        Number(fields,"No skill gains at",2,1000000,r.NoSkillUp,v=>r.NoSkillUp=(int)v);
        Number(fields,"Mastery XP per craft",0,int.MaxValue,r.MasteryXp,v=>r.MasteryXp=(int)v);
        Number(fields,"Bronze — total mastery XP",1,int.MaxValue,r.BronzeRequired,v=>r.BronzeRequired=(int)v);
        Number(fields,"Silver — total mastery XP",1,int.MaxValue,r.SilverRequired,v=>r.SilverRequired=(int)v);
        Number(fields,"Gold — total mastery XP",1,int.MaxValue,r.MasteryRequired,v=>r.MasteryRequired=(int)v);
        fields.Controls.Add(recipeSummary);
        page.Controls.Add(Vertical(fields,BonusGrid(r.Bonuses,true))); detailTabs.TabPages.Add(page);
        page=new("Bonus outputs"); page.Controls.Add(Vertical(Label("One exclusive roll per successful craft. Chances total at most 100%; the remainder grants no bonus."),ItemGrid(r.BonusOutputs,()=>new BonusOutput(),8))); detailTabs.TabPages.Add(page);
        recipeDetail.Controls.Add(detailTabs); UpdateRecipeSummary();
    }
    private void UpdateRecipeSummary()
    {
        if(selectedRecipe is not Recipe r || recipeSummary.IsDisposed) return;
        recipeSummary.Text=$"Skill bands (cap {catalog.SkillCap}): "+CraftingRules.Training(r,catalog.SkillCap)+"\r\n"+CraftingRules.MasteryEstimate(r)+
            $"\r\nBronze {r.BronzeRequired:N0} XP; Silver {r.SilverRequired:N0} XP; Gold {r.MasteryRequired:N0} XP. Each tier contributes one mastery point.\r\nTargets are cumulative. Each next rank must cost more additional XP. Bonuses add to earlier tiers; zero XP/craft pauses progression.\r\nTotal earned by Gold: "+(r.Bonuses.Count==0?"No permanent bonus configured":CraftingRules.Rewards(r.Bonuses));
    }
    private Control ItemGrid<T>(List<T> values,Func<T> create,int limit) where T:class
    {
        var source=new BindingList<T>(values); var grid=Grid(); detailGrids.Add(grid);
        Column(grid,"ItemId","Item ID",55);
        grid.Columns.Add(new DataGridViewImageColumn { Name="Icon", HeaderText="", FillWeight=25, ImageLayout=DataGridViewImageCellLayout.Zoom });
        Column(grid,"ItemName","Item name",145,true);
        if(typeof(T)!=typeof(TeachingItem)) Column(grid,"Quantity","Quantity",60);
        if(typeof(T)==typeof(BonusOutput)) Column(grid,"ChancePercent","Chance %",55);
        int Id(T obj)=>(int)typeof(T).GetProperty("ItemId")!.GetValue(obj)!;
        void SetId(T obj,int id)=>typeof(T).GetProperty("ItemId")!.SetValue(obj,id);
        bool ValidManual(int id) {
            if(typeof(T)!=typeof(TeachingItem)) return true;
            if(catalog.Items.TryGetValue(id,out var item) && item.Enabled && item.Manual && !item.Timed) return true;
            status.Text="Teaching items must be enabled, non-timed Once manuals (subtype 1, 2 or 4)."; return false;
        }
        grid.CellFormatting+=(_,e)=> { if(e.RowIndex<0 || grid.Rows[e.RowIndex].DataBoundItem is not T item) return; string name=grid.Columns[e.ColumnIndex].Name;
            if(name=="ItemName") { e.Value=catalog.ItemName(Id(item)); e.FormattingApplied=true; }
            if(name=="Icon") { e.Value=ItemIcon(Id(item)); e.FormattingApplied=true; } };
        grid.DataSource=source; ConfigureEditing(grid);
        var tools=Flow();
        tools.Controls.AddRange([Button("Add item",()=> { if(values.Count>=limit) { status.Text=$"Maximum {limit} rows."; return; } int? id=PickItem(0); if(!id.HasValue || !ValidManual(id.Value)) return; var item=create(); SetId(item,id.Value); source.Add(item); Changed(); }),
            Button("Choose item",()=> { if(grid.CurrentRow?.DataBoundItem is not T item) return; int? id=PickItem(Id(item)); if(id.HasValue && ValidManual(id.Value)) { SetId(item,id.Value); source.ResetBindings(); Changed(); } }),
            Button("Remove",()=> { if(grid.CurrentRow?.DataBoundItem is T item) { source.Remove(item); Changed(); } }),
            Button("Open item",()=> { if(grid.CurrentRow?.DataBoundItem is T item) OpenItem(Id(item)); }),
            Button("Where used",()=> { if(grid.CurrentRow?.DataBoundItem is T item) ShowText("Where used",string.Join("\r\n",catalog.WhereUsed(Id(item)))); })]);
        return Vertical(tools,grid);
    }
    private Control BonusGrid(List<Bonus> bonuses,bool tiered=false)
    {
        var source=new BindingList<Bonus>(bonuses); var grid=Grid(); grid.Name="MasteryBonusGrid"; detailGrids.Add(grid);
        if(tiered) grid.Columns.Add(new DataGridViewComboBoxColumn { Name="Rank",DataPropertyName="Rank",HeaderText="Earned at",DataSource=new Dictionary<int,string>{{1,"Bronze"},{2,"Silver"},{3,"Gold"}}.ToList(),DisplayMember="Value",ValueMember="Key",FillWeight=75 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name="Effect", DataPropertyName="Effect", HeaderText="Permanent bonus", DataSource=CraftingRules.Effects.ToList(), DisplayMember="Value", ValueMember="Key", FillWeight=160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name="Amount",DataPropertyName=nameof(Bonus.DisplayAmount),HeaderText="Amount",FillWeight=65,SortMode=DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle=new DataGridViewCellStyle { Format="0.##" } });
        Column(grid,nameof(Bonus.DisplayUnit),"Units",105,true); grid.DataSource=source; ConfigureEditing(grid);
        grid.CellValidating+=(_,e)=> {
            if(e.RowIndex<0 || e.ColumnIndex<0 || grid.Columns[e.ColumnIndex].Name!="Amount" || grid.Rows[e.RowIndex].DataBoundItem is not Bonus bonus) return;
            try {
                MasteryBonusUnits.Store(bonus.Effect,MasteryBonusUnits.Parse(bonus.Effect,Convert.ToString(e.FormattedValue)??""));
                grid.Rows[e.RowIndex].ErrorText="";
            } catch(FormatException error) {e.Cancel=true;grid.Rows[e.RowIndex].ErrorText=error.Message;status.Text=error.Message;}
        };
        grid.CellParsing+=(_,e)=> {
            if(e.RowIndex<0 || e.ColumnIndex<0 || grid.Columns[e.ColumnIndex].Name!="Amount" || grid.Rows[e.RowIndex].DataBoundItem is not Bonus bonus) return;
            if(e.Value is string text) {e.Value=MasteryBonusUnits.Parse(bonus.Effect,text);e.ParsingApplied=true;}
        };
        bool refreshing=false;
        grid.CellValueChanged+=(_,e)=> {
            if(refreshing || e.RowIndex<0 || e.ColumnIndex<0 || grid.Columns[e.ColumnIndex].Name!="Effect") return;
            try {refreshing=true;source.ResetItem(e.RowIndex);} finally {refreshing=false;}
        };
        var tools=Flow(); tools.Controls.AddRange([Button("Add bonus",()=> {
            int rank=tiered?(grid.CurrentRow?.DataBoundItem as Bonus)?.Rank??1:3;
            if(bonuses.Count(b=>!tiered||b.Rank==rank)>=8) {status.Text="This rank already has eight bonus effects.";return;}
            int effect=CraftingRules.Effects.Keys.First(k=>bonuses.All(b=>(tiered&&b.Rank!=rank)||b.Effect!=k)); source.Add(new Bonus { Rank=rank,Effect=effect }); Changed();
        }),Button("Remove bonus",()=> { if(grid.CurrentRow?.DataBoundItem is Bonus b) { source.Remove(b); Changed(); } })]);
        var help=Label("Enter movement in speed units; interval reduction and PvE chance use percentages (up to two decimals)."+(tiered?"\r\nTier entries add together: the Gold total includes Bronze, Silver and Gold rewards.":"\r\nArtisan stat bonuses stack across earned ranks; perks are configured separately above."));
        help.MaximumSize=new(650,0);
        return Vertical(tools,help,grid);
    }
    private void ConfigureEditing(DataGridView grid)
    {
        grid.DataError+=(_,e)=> { e.ThrowException=false; e.Cancel=true; status.Text="Invalid value. Enter a whole quantity or a percentage with at most two decimals."; };
        grid.CellEndEdit+=(_,_)=> { Changed(); grid.Invalidate(); };
        grid.CurrentCellDirtyStateChanged+=(_,_)=> { if(grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
    }
    private void AssignBook()
    {
        int id=(int)bookItem.Value;
        if(!loaded || !catalog.Items.TryGetValue(id,out var i) || !i.Enabled || !i.Manual || i.Timed) { status.Text="Choose a usable manual first."; return; }
        using var dialog=new Form { Text=$"Recipes taught by {i.Name}", Size=new(720,620), StartPosition=FormStartPosition.CenterParent };
        var list=new CheckedListBox { Dock=DockStyle.Fill, CheckOnClick=true, DisplayMember="Name" };
        var ordered=catalog.Recipes.OrderBy(r=>r.Profession).ThenBy(r=>r.Id).ToList();
        foreach(var r in ordered) list.Items.Add($"{r.Profession} • #{r.Id} {catalog.ItemName(r.OutputItem)}",r.Manuals.Any(m=>m.ItemId==id));
        var filter=new TextBox { PlaceholderText="Find name or recipe ID (selects the first match)", Dock=DockStyle.Top };
        filter.TextChanged+=(_,_)=> { for(int n=0;n<list.Items.Count;n++) if(list.Items[n]!.ToString()!.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)) { list.SelectedIndex=n; break; } };
        var apply=Button("Apply to draft",()=>dialog.DialogResult=DialogResult.OK);
        dialog.Controls.Add(Vertical(Label("Checked recipes require this manual. Unchecking the last manual leaves a validation error until you assign another or choose automatic learning."),filter,apply,list));
        if(dialog.ShowDialog(this)!=DialogResult.OK) return;
        for(int n=0;n<ordered.Count;n++) {
            var r=ordered[n]; bool wanted=list.GetItemChecked(n), exists=r.Manuals.Any(m=>m.ItemId==id);
            if(wanted && !exists && r.Manuals.Count>=8) { status.Text=$"Recipe {r.Id} already has eight teaching items. Nothing changed."; return; }
        }
        for(int n=0;n<ordered.Count;n++) {
            var r=ordered[n]; bool wanted=list.GetItemChecked(n);
            if(wanted) { r.RequiresUnlock=true; if(!r.Manuals.Any(m=>m.ItemId==id)) r.Manuals.Add(new() { ItemId=id }); }
            else r.Manuals.RemoveAll(m=>m.ItemId==id);
        }
        Changed(); ShowRecipe();
    }
    private void RefreshRanks(int? select=null)
    {
        int id=select??selectedRank?.Id??0;
        binding=true; ranks.DataSource=new BindingList<MastersRank>(catalog.Ranks.OrderBy(r=>r.Order).ToList()); SelectRow(ranks,r=>((MastersRank)r).Id==id); binding=false;
        selectedRank=ranks.CurrentRow?.DataBoundItem as MastersRank; ShowRank();
    }
    private void ShowRank()
    {
        int selectedPage=rankDetail.Controls.OfType<TabControl>().FirstOrDefault()?.SelectedIndex??0;
        ClearPanel(rankDetail); detailGrids.RemoveAll(g=>g.IsDisposed);
        if(selectedRank is not MastersRank r) { rankDetail.Controls.Add(Label("Add an Artisan Rank to define its threshold and rewards.")); return; }
        var pages=new TabControl {Name="ArtisanRankDetails",Dock=DockStyle.Fill};
        var settings=new TabPage("Rank settings");var rewards=new TabPage("Rewards");pages.TabPages.AddRange([settings,rewards]);rankDetail.Controls.Add(pages);
        var fields=Fields();settings.Controls.Add(fields);
        fields.Controls.Add(Label($"Artisan Rank #{r.Id} — permanent identity.\r\nChanging thresholds never revokes earned rank IDs."));
        TextField(fields,"Artisan Rank name",r.Name,v=>r.Name=v); Number(fields,"Display order",1,int.MaxValue,r.Order,v=>r.Order=(int)v);
        Number(fields,"Artisan Rank points to unlock",1,98304,r.RequiredMastered,v=>r.RequiredMastered=(int)v);
        Check(fields,"Enabled — may be newly earned",r.Enabled,v=>r.Enabled=v);
        TextField(fields,"Emblem (.tex filename)",r.Emblem,v=>r.Emblem=v);
        var emblemRow=Flow(); emblemRow.Dock=DockStyle.None;
        emblemRow.Controls.Add(Button("Choose / preview emblem",()=>ChooseEmblem(r))); fields.Controls.Add(emblemRow);
        fields.Controls.Add(Label("Retiring prevents new unlocks and retains earned rewards.\r\nCurrent bonuses stack across all earned ranks."));
        rankSummary=new Label { AutoSize=true, MaximumSize=new(430,0) };
        UpdateRankSummary(); fields.Controls.Add(rankSummary);
        var perks=Fields();perks.Dock=DockStyle.Top;perks.AutoSize=true;
        perks.Controls.Add(Label("PERKS — permanent abilities, separate from stat bonuses"));
        foreach (var definition in ArtisanPerks.Names) {
            int type=definition.Key;
            var perk=new CheckBox { Name="ArtisanPerk_"+type,Text=definition.Value,AutoSize=true,
                Checked=r.Perks.Any(p=>p.Type==type && p.Enabled),Margin=new(8) };
            perk.CheckedChanged+=(_,_)=> {
                var mapping=r.Perks.SingleOrDefault(p=>p.Type==type);
                if (mapping==null) r.Perks.Add(new() {Type=type,Enabled=perk.Checked});
                else mapping.Enabled=perk.Checked;
                Changed();UpdateRankSummary();
            };
            perks.Controls.Add(perk);
        }
        perks.Controls.Add(Label("Checked perks apply to characters who earned this rank.\r\nUnchecking removes that benefit; retiring only stops new unlocks."));
        perks.Controls.Add(Label("Failed upgrades may still lower the upgrade level.\r\nUpgrade costs and success chances are unchanged."));
        var bonuses=BonusGrid(r.Bonuses);
        rewards.Controls.Add(Vertical(perks,Label("STAT BONUSES"),bonuses));
        var facilities=RankFacilities(r.Id).ToList();
        if(facilities.Count>0) {
            var facilityPage=new TabPage("Facility construction");var facilityFields=Fields();facilityPage.Controls.Add(facilityFields);pages.TabPages.Add(facilityPage);
            facilityFields.Controls.Add(Label("Earning this rank grants permission to construct these permanent workshops. Construction materials are configured on each facility page. Built ownership is retained after later rank or cost edits."));
            foreach(var facility in facilities) {int profession=facility.Profession;string table=facility.Table;facilityFields.Controls.Add(Label("Allows construction of "+facility.Name));facilityFields.Controls.Add(Button("Edit "+facility.Name,()=>NavigateProfessionPage(profession,table)));}
        }
        pages.SelectedIndex=Math.Clamp(selectedPage,0,pages.TabPages.Count-1);
    }
    private void UpdateRankSummary()
    {
        if(selectedRank is MastersRank r && rankSummary is { IsDisposed:false })
            rankSummary.Text="Cumulative through this rank: "+CraftingRules.Rewards(catalog.Ranks.Where(n=>n.Order<=r.Order).SelectMany(n=>n.Bonuses))
                +"\r\nPerks through this rank: "+(string.Join("; ",ArtisanPerks.Active(catalog.Ranks.Where(n=>n.Order<=r.Order))) is {Length:>0} perks?perks:"None")
                +"\r\nConstruction unlocked by this rank: "+(string.Join("; ",RankFacilities(r.Id).Select(f=>f.Name)) is {Length:>0} facilities?facilities:"None");
    }
    private IEnumerable<(int Profession,string Table,string Name)> RankFacilities(int rank)
    {
        foreach(var definition in new[]{(5,InfusionSchema.Facility),(6,TransmutationSchema.Facility),(7,AccessoryDisplaySchema.Facility)})
            foreach(var facility in catalog.Professions.Rows(definition.Item2).Where(row=>row.Int("a_required_artisan_rank")==rank))
                yield return (definition.Item1,definition.Item2,facility.Get("a_name"));
    }
    private void NewRank(bool copy=false)
    {
        if(!loaded || !CommitGrids() || (copy && selectedRank==null) || catalog.Ranks.Count>=64) return;
        var r=copy?CraftingCatalog.Copy(selectedRank!):new MastersRank();
        r.Id=checked(catalog.Ranks.Select(n=>n.Id).DefaultIfEmpty().Max()+1);
        r.Order=checked(catalog.Ranks.Select(n=>n.Order).DefaultIfEmpty().Max()+1);
        r.RequiredMastered=catalog.Ranks.Select(n=>n.RequiredMastered).DefaultIfEmpty().Max()+1;
        catalog.Ranks.Add(r); Changed(); RefreshRanks(r.Id);
    }
    private void MoveRank(int direction)
    {
        if(selectedRank==null || !CommitGrids()) return;
        var ordered=catalog.Ranks.OrderBy(r=>r.Order).ToList(); int i=ordered.IndexOf(selectedRank), j=i+direction;
        if(j<0 || j>=ordered.Count) return;
        (ordered[i].Order,ordered[j].Order)=(ordered[j].Order,ordered[i].Order);
        // Keep progression thresholds attached to positions; permanent IDs and
        // reward definitions move. Existing earned rank identities stay intact.
        (ordered[i].RequiredMastered,ordered[j].RequiredMastered)=(ordered[j].RequiredMastered,ordered[i].RequiredMastered);
        Changed(); RefreshRanks(selectedRank.Id);
        status.Text="Moved rank identity and rewards. Thresholds stay with their progression positions; existing earned IDs remain intact.";
    }
    private void BulkEdit()
    {
        if(!CommitGrids()) return;
        var selected=recipes.SelectedRows.Cast<DataGridViewRow>().Select(row=>row.DataBoundItem).OfType<Recipe>().ToList();
        if(selected.Count==0) return;
        using var dialog=new Form { Name="BulkRecipeEditor",Text=$"Bulk edit {selected.Count} recipes", Size=new(590,570), StartPosition=FormStartPosition.CenterParent };
        var fields=Fields(); var field=new ComboBox { Name="BulkField",DropDownStyle=ComboBoxStyle.DropDownList, Width=460 };
        field.Items.AddRange(["Craft time (milliseconds)","Mastery XP per craft","Gold total mastery XP","Minimum skill","No skill gains at","Set / add mastery bonus","Bronze total mastery XP","Silver total mastery XP"]); field.SelectedIndex=0;
        var value=new NumericUpDown { Name="BulkValue",Maximum=int.MaxValue, Minimum=0, Value=10, Width=160 };
        var effect=new ComboBox { Name="BulkBonusEffect",DataSource=CraftingRules.Effects.ToList(), DisplayMember="Value", ValueMember="Key", Width=360, DropDownStyle=ComboBoxStyle.DropDownList };
        var tier=new ComboBox { Name="BulkBonusTier",DataSource=new Dictionary<int,string>{{1,"Bronze"},{2,"Silver"},{3,"Gold"}}.ToList(),DisplayMember="Value",ValueMember="Key",Width=150,DropDownStyle=ComboBoxStyle.DropDownList };tier.SelectedValue=3;
        var units=Label("New value"); var help=Label("Bonus entries add to earlier tiers. Attack interval reduction lowers the time between attacks."); help.MaximumSize=new(490,0);
        var preview=new TextBox { Width=510, Height=120, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical };
        void Preview() {
            string display=field.SelectedIndex==5 && effect.SelectedValue is int key
                ? MasteryBonusUnits.Format(key,MasteryBonusUnits.Store(key,value.Value))+" at "+tier.Text : value.Value.ToString("0.##");
            preview.Text=string.Join("\r\n",selected.Select(r=>$"#{r.Id} {r.Name}: {field.Text} → {display}"));
        }
        bool configuring=false;
        void ConfigureValue() {
            if(configuring)return;
            configuring=true;
            try {
            bool bonus=field.SelectedIndex==5;effect.Enabled=tier.Enabled=bonus;
            int key=effect.SelectedValue is int id?id:0;
            decimal amount=value.Value,min=bonus?MasteryBonusUnits.Display(key,1):0,max=bonus?MasteryBonusUnits.Display(key,MasteryBonusUnits.Maximum(key)):int.MaxValue;
            value.Minimum=0;value.Maximum=int.MaxValue;value.DecimalPlaces=bonus?MasteryBonusUnits.Decimals(key):0;
            value.Increment=value.DecimalPlaces>0?0.01m:1;value.Value=Math.Clamp(amount,min,max);value.Minimum=min;value.Maximum=max;
            units.Text=bonus?"New amount ("+MasteryBonusUnits.Unit(key)+")":"New value";
            } finally {configuring=false;}
            Preview();
        }
        value.ValueChanged+=(_,_)=> {if(!configuring && effect.SelectedValue is not null && (field.SelectedIndex!=5 || value.Value>=MasteryBonusUnits.Display((int)effect.SelectedValue,1)))Preview();};
        field.SelectedIndexChanged+=(_,_)=>ConfigureValue(); effect.SelectedValueChanged+=(_,_)=>ConfigureValue();tier.SelectedValueChanged+=(_,_)=>Preview();
        fields.Controls.AddRange([field,units,value,Label("Effect and earned tier (for bonus changes)"),effect,tier,help,preview,Button("Apply to draft",()=>dialog.DialogResult=DialogResult.OK)]); dialog.Controls.Add(fields);
        dialog.Shown+=(_,_)=> {tier.SelectedValue=3;ConfigureValue();};
        if(dialog.ShowDialog(this)!=DialogResult.OK) return;
        foreach(var r in selected) switch(field.SelectedIndex) {
            case 0:r.CraftTimeMs=(int)value.Value;break; case 1:r.MasteryXp=(int)value.Value;break; case 2:r.MasteryRequired=(int)value.Value;break;
            case 3:r.RequiredSkill=(int)value.Value;break;case 4:r.NoSkillUp=(int)value.Value;break;
            case 5:int key=(int)effect.SelectedValue!,rank=(int)tier.SelectedValue!,stored=MasteryBonusUnits.Store(key,value.Value); var b=r.Bonuses.FirstOrDefault(b=>b.Effect==key&&b.Rank==rank); if(b==null) r.Bonuses.Add(new() { Rank=rank,Effect=key,Amount=stored }); else b.Amount=stored;break;
            case 6:r.BronzeRequired=(int)value.Value;break;case 7:r.SilverRequired=(int)value.Value;break;
        }
        Changed(); ShowRecipe();
    }
    private void Overview()
    {
        if(!loaded || !CommitGrids()) return;
        var text=new StringBuilder();
        foreach(var p in Enum.GetValues<Profession>()) {
            var rows=catalog.Recipes.Where(r=>r.Enabled && r.Profession==p).ToList();
            text.AppendLine($"{p}: {rows.Count} enabled recipes; {3*rows.Count(r=>r.MasteryXp>0)} obtainable Artisan Rank points");
            var gaps=Enumerable.Range(1,catalog.SkillCap-1).Where(s=>!rows.Any(r=>CraftingRules.Chance(r,s,catalog.SkillCap)>0));
            text.AppendLine("  Skill levels without a training recipe: "+CompactSkillRanges(gaps));
        }
        foreach(var group in catalog.Gathering.GroupBy(r=>r.ProfessionName))text.AppendLine($"{group.Key}: {group.Count()} resources; {3*group.Count(r=>r.MasteryXp>0)} obtainable Artisan Rank points");
        text.AppendLine("\r\nAll obtainable crafting, gathering and Artisan Rank bonuses (including retained retired rewards):");
        text.AppendLine(CraftingRules.Rewards(catalog.Recipes.SelectMany(r=>r.Bonuses).Concat(catalog.Gathering.SelectMany(r=>r.Bonuses)).Concat(catalog.Ranks.SelectMany(r=>r.Bonuses))));
        text.AppendLine("\r\nValidation:"); var issues=CraftingRules.Validate(catalog);
        text.AppendLine(issues.Count==0?"No issues.":string.Join("\r\n",issues));
        ShowText("Professions overview",text.ToString());
    }
}
