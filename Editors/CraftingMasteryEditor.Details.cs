using LastChaos_ToolBoxNG.Crafting;

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
        Number(fields,"Minimum skill to craft",1,50,r.RequiredSkill,v=>r.RequiredSkill=(int)v);
        Number(fields,"No skill gains at",2,1000000,r.NoSkillUp,v=>r.NoSkillUp=(int)v);
        Number(fields,"Mastery XP per craft",0,int.MaxValue,r.MasteryXp,v=>r.MasteryXp=(int)v);
        Number(fields,"Mastery XP required (0 = disabled)",0,int.MaxValue,r.MasteryRequired,v=>r.MasteryRequired=(int)v);
        fields.Controls.Add(recipeSummary);
        page.Controls.Add(Vertical(fields,BonusGrid(r.Bonuses))); detailTabs.TabPages.Add(page);
        page=new("Bonus outputs"); page.Controls.Add(Vertical(Label("One exclusive roll per successful craft. Chances total at most 100%; the remainder grants no bonus."),ItemGrid(r.BonusOutputs,()=>new BonusOutput(),8))); detailTabs.TabPages.Add(page);
        recipeDetail.Controls.Add(detailTabs); UpdateRecipeSummary();
    }
    private void UpdateRecipeSummary()
    {
        if(selectedRecipe is not Recipe r || recipeSummary.IsDisposed) return;
        recipeSummary.Text="Skill bands (cap 50): "+CraftingRules.Training(r)+"\r\n"+CraftingRules.MasteryEstimate(r)+
            "\r\nMastery rewards are permanent character bonuses. Zero required XP needs zero XP/craft and an empty bonus list.";
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
    private Control BonusGrid(List<Bonus> bonuses)
    {
        var source=new BindingList<Bonus>(bonuses); var grid=Grid(); detailGrids.Add(grid);
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name="Effect", DataPropertyName="Effect", HeaderText="Permanent bonus", DataSource=CraftingRules.Effects.ToList(), DisplayMember="Value", ValueMember="Key", FillWeight=160 });
        Column(grid,"Amount","Flat amount",70); grid.DataSource=source; ConfigureEditing(grid);
        var tools=Flow(); tools.Controls.AddRange([Button("Add bonus",()=> { if(bonuses.Count>=8) return; int effect=CraftingRules.Effects.Keys.First(k=>bonuses.All(b=>b.Effect!=k)); source.Add(new Bonus { Effect=effect }); Changed(); }),Button("Remove bonus",()=> { if(grid.CurrentRow?.DataBoundItem is Bonus b) { source.Remove(b); Changed(); } })]);
        return Vertical(tools,grid);
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
        ClearPanel(rankDetail); detailGrids.RemoveAll(g=>g.IsDisposed);
        if(selectedRank is not MastersRank r) { rankDetail.Controls.Add(Label("Add a rank to define its threshold and rewards.")); return; }
        var fields=Fields(); fields.Dock=DockStyle.Top; fields.AutoSize=true;
        fields.Controls.Add(Label($"Rank #{r.Id} — permanent identity; earned rank IDs are never revoked by changing thresholds."));
        TextField(fields,"Rank name",r.Name,v=>r.Name=v); Number(fields,"Display order",1,int.MaxValue,r.Order,v=>r.Order=(int)v);
        Number(fields,"Recipes mastered to unlock",1,16384,r.RequiredMastered,v=>r.RequiredMastered=(int)v);
        Check(fields,"Enabled — may be newly earned",r.Enabled,v=>r.Enabled=v);
        TextField(fields,"Emblem (.tex filename)",r.Emblem,v=>r.Emblem=v);
        var emblemRow=Flow(); emblemRow.Dock=DockStyle.None;
        emblemRow.Controls.Add(Button("Choose / preview emblem",()=>ChooseEmblem(r))); fields.Controls.Add(emblemRow);
        fields.Controls.Add(Label("Retiring prevents new unlocks and retains earned rewards. Current bonuses stack across all earned ranks."));
        rankSummary=new Label { AutoSize=true, MaximumSize=new(800,0) };
        UpdateRankSummary(); fields.Controls.Add(rankSummary);
        var bonuses=BonusGrid(r.Bonuses);
        rankDetail.Controls.Add(Vertical(fields,bonuses));
    }
    private void UpdateRankSummary()
    {
        if(selectedRank is MastersRank r && rankSummary is { IsDisposed:false })
            rankSummary.Text="Cumulative through this rank: "+CraftingRules.Rewards(catalog.Ranks.Where(n=>n.Order<=r.Order).SelectMany(n=>n.Bonuses));
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
        using var dialog=new Form { Text=$"Bulk edit {selected.Count} recipes", Size=new(540,390), StartPosition=FormStartPosition.CenterParent };
        var fields=Fields(); var field=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=420 };
        field.Items.AddRange(["Craft time (milliseconds)","Mastery XP per craft","Mastery XP required","Minimum skill","No skill gains at","Set / add mastery bonus"]); field.SelectedIndex=0;
        var value=new NumericUpDown { Maximum=int.MaxValue, Minimum=0, Value=10, Width=160 };
        var effect=new ComboBox { DataSource=CraftingRules.Effects.ToList(), DisplayMember="Value", ValueMember="Key", Width=270, DropDownStyle=ComboBoxStyle.DropDownList };
        var preview=new TextBox { Width=450, Height=140, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical };
        void Preview()=>preview.Text=string.Join("\r\n",selected.Select(r=>$"#{r.Id} {r.Name}: {field.Text} → {value.Value}"));
        value.ValueChanged+=(_,_)=>Preview(); field.SelectedIndexChanged+=(_,_)=>Preview(); Preview();
        fields.Controls.AddRange([field,Label("New value"),value,Label("Effect (for bonus changes)"),effect,preview,Button("Apply to draft",()=>dialog.DialogResult=DialogResult.OK)]); dialog.Controls.Add(fields);
        if(dialog.ShowDialog(this)!=DialogResult.OK) return;
        foreach(var r in selected) switch(field.SelectedIndex) {
            case 0:r.CraftTimeMs=(int)value.Value;break; case 1:r.MasteryXp=(int)value.Value;break; case 2:r.MasteryRequired=(int)value.Value;break;
            case 3:r.RequiredSkill=(int)value.Value;break;case 4:r.NoSkillUp=(int)value.Value;break;
            case 5:int key=(int)effect.SelectedValue!; var b=r.Bonuses.FirstOrDefault(b=>b.Effect==key); if(b==null) r.Bonuses.Add(new() { Effect=key,Amount=(int)value.Value }); else b.Amount=(int)value.Value;break;
        }
        Changed(); ShowRecipe();
    }
    private void Overview()
    {
        if(!loaded || !CommitGrids()) return;
        var text=new StringBuilder();
        foreach(var p in Enum.GetValues<Profession>()) {
            var rows=catalog.Recipes.Where(r=>r.Enabled && r.Profession==p).ToList();
            text.AppendLine($"{p}: {rows.Count} enabled recipes; {rows.Count(r=>r.MasteryRequired>0)} mastery recipes");
            var gaps=Enumerable.Range(1,49).Where(s=>!rows.Any(r=>CraftingRules.Chance(r,s)>0));
            text.AppendLine("  Skill levels without a training recipe: "+string.Join(", ",gaps));
        }
        text.AppendLine("\r\nAll obtainable recipe + rank bonuses (including retained retired rewards):");
        text.AppendLine(CraftingRules.Rewards(catalog.Recipes.SelectMany(r=>r.Bonuses).Concat(catalog.Ranks.SelectMany(r=>r.Bonuses))));
        text.AppendLine("\r\nValidation:"); var issues=CraftingRules.Validate(catalog);
        text.AppendLine(issues.Count==0?"No issues.":string.Join("\r\n",issues));
        ShowText("Crafting catalogue overview",text.ToString());
    }
}
