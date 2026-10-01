using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor : Form
{
    private readonly Main main;
    private readonly CraftingRepository repository;
    private CraftingCatalog catalog = new(), original = new();
    private bool loaded, dirty, busy, binding;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView recipes = Grid(true), ranks = Grid(true);
    private readonly Panel recipeDetail = new() { Dock = DockStyle.Fill }, rankDetail = new() { Dock = DockStyle.Fill };
    private readonly TextBox search = new() { Width = 230, PlaceholderText = "Name, recipe, item or manual ID" };
    private readonly ComboBox profession = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox availability = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label recipeSummary = new() { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(850, 0) };
    private readonly TextBox bookRecipes = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly NumericUpDown bookItem = new() { Minimum = 0, Maximum = int.MaxValue, Width = 125 };
    private readonly Label bookName = new() { AutoSize = true, MaximumSize = new Size(850, 0) };
    private readonly List<DataGridView> detailGrids = [];
    private readonly Dictionary<int, Image?> icons = [];
    private Recipe? selectedRecipe;
    private MastersRank? selectedRank;
    private Label? rankSummary;
    private readonly int initialItem;

    public CraftingMasteryEditor(Main host, int teachingItem = 0)
    {
        main=host; initialItem=teachingItem;
        repository=new(ConnectionString(host), host.pSettings.WorkLocale);
        Name="CraftingMasteryEditor"; Text="Crafting & Mastery Editor";
        AutoScaleMode=AutoScaleMode.Dpi; MinimumSize=new(1100,720); Size=new(1420,900);
        Font=new Font("Segoe UI",9);
        StartPosition=FormStartPosition.CenterParent;
        BuildLayout();
        Load+=async (_,_)=> { await ReloadAsync(false); if(initialItem>0) { bookItem.Value=initialItem; tabs.SelectedIndex=1; } };
        FormClosing+=(_,e)=> { if(busy || (dirty && MessageBox.Show(this,"Discard unsaved crafting changes?","Crafting & Mastery",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)) e.Cancel=true; };
        FormClosed+=(_,_)=> { foreach(var image in icons.Values) image?.Dispose(); };
    }
    internal static string ConnectionString(Main host) => new MySqlConnectionStringBuilder {
        Server=host.pSettings.DBHost, Database=host.pSettings.DBData, UserID=host.pSettings.DBUsername,
        Password=host.pSettings.DBPassword, CharacterSet="utf8mb4", AllowUserVariables=false
    }.ConnectionString;
    private static FlowLayoutPanel Flow() => new() { Dock=DockStyle.Top, AutoSize=true, WrapContents=true, Padding=new(4) };
    private static Button Button(string text, Action click) { var b=new Button { Text=text, AutoSize=true, MinimumSize=new(82,30) }; b.Click+=(_,_)=>click(); return b; }
    private static Label Label(string text) => new() { Text=text, AutoSize=true, Margin=new(5,9,8,5) };
    private static DataGridView Grid(bool readOnly=false) => new() {
        Dock=DockStyle.Fill, AutoGenerateColumns=false, AllowUserToAddRows=false, AllowUserToDeleteRows=false,
        ReadOnly=readOnly, SelectionMode=DataGridViewSelectionMode.FullRowSelect, MultiSelect=readOnly,
        RowHeadersVisible=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor=SystemColors.Window, BorderStyle=BorderStyle.FixedSingle, RowTemplate={ Height=34 }
    };
    private static void Column(DataGridView g,string property,string title,int weight=100,bool readOnly=false) =>
        g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=property, Name=property, HeaderText=title, FillWeight=weight, ReadOnly=readOnly, SortMode=DataGridViewColumnSortMode.NotSortable });
    private static TableLayoutPanel Vertical(params Control[] controls)
    {
        var p=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=controls.Length, Padding=new(8) };
        for(int i=0;i<controls.Length;i++) { p.RowStyles.Add(new(i==controls.Length-1?SizeType.Percent:SizeType.AutoSize,i==controls.Length-1?100:0)); p.Controls.Add(controls[i],0,i); }
        return p;
    }
    private void BuildLayout()
    {
        var toolbar=Flow();
        toolbar.Controls.AddRange([Button("Save all changes",async ()=>await SaveAsync()),Button("Revert / Reload",async ()=>await ReloadAsync(true)),
            Button("Validate / Overview",()=>Overview()),Button("Export crafting seeds",async ()=>await ExportAsync())]);
        var root=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.Absolute,42));
        root.Controls.Add(toolbar,0,0); root.Controls.Add(tabs,0,1); root.Controls.Add(status,0,2); Controls.Add(root);
        var page=new TabPage("Recipes"); tabs.TabPages.Add(page);
        var split=new SplitContainer { Size=new(1350,750), Dock=DockStyle.Fill, FixedPanel=FixedPanel.Panel1, Panel1MinSize=330, Panel2MinSize=580, SplitterDistance=430 };
        Shown+=(_,_)=>split.SplitterDistance=Math.Max(330,Math.Min(450,split.Width-585));
        page.Controls.Add(split);
        profession.Items.AddRange(["All professions","Smithing","Alchemy","Cooking"]); profession.SelectedIndex=0;
        availability.Items.AddRange(["All entries","Enabled","Retired"]); availability.SelectedIndex=0;
        var filters=Flow(); filters.Controls.AddRange([Label("Search"),search,profession,availability]);
        var actions=Flow(); actions.Controls.AddRange([Button("New",()=>NewRecipe()),Button("Duplicate",()=>NewRecipe(true)),Button("Retire / Enable",()=>RetireRecipe()),Button("Bulk edit",()=>BulkEdit()),Button("Discard new",()=>DiscardNewRecipe())]);
        Column(recipes,"Id","ID",40); Column(recipes,"Name","Output",150); Column(recipes,"Profession","Profession",65); Column(recipes,"RequiredSkill","Skill",35);
        split.Panel1.Controls.Add(Vertical(filters,actions,recipes)); split.Panel2.Controls.Add(recipeDetail);
        search.TextChanged+=(_,_)=>FilterRecipes(); profession.SelectedIndexChanged+=(_,_)=>FilterRecipes(); availability.SelectedIndexChanged+=(_,_)=>FilterRecipes();
        recipes.SelectionChanged+=(_,_)=> { if(binding) return; if(!CommitGrids()) return; selectedRecipe=recipes.CurrentRow?.DataBoundItem as Recipe; ShowRecipe(); };

        page=new("Teaching Items"); tabs.TabPages.Add(page);
        BuildTeachingItems(page);

        page=new("Master's Ranks"); tabs.TabPages.Add(page);
        var rankSplit=new SplitContainer { Size=new(1350,750), Dock=DockStyle.Fill, FixedPanel=FixedPanel.Panel1, Panel1MinSize=330, Panel2MinSize=580, SplitterDistance=430 };
        Shown+=(_,_)=>rankSplit.SplitterDistance=Math.Max(330,Math.Min(450,rankSplit.Width-585));
        page.Controls.Add(rankSplit);
        var rankTools=Flow(); rankTools.Controls.AddRange([Button("New rank",()=>NewRank()),Button("Duplicate",()=>NewRank(true)),Button("Move up",()=>MoveRank(-1)),Button("Move down",()=>MoveRank(1)),Button("Discard new",()=>DiscardNewRank())]);
        Column(ranks,"Order","Order",40); Column(ranks,"Name","Rank",140); Column(ranks,"RequiredMastered","Mastered",65);
        rankSplit.Panel1.Controls.Add(Vertical(rankTools,ranks)); rankSplit.Panel2.Controls.Add(rankDetail);
        ranks.SelectionChanged+=(_,_)=> { if(binding || !CommitGrids()) return; selectedRank=ranks.CurrentRow?.DataBoundItem as MastersRank; ShowRank(); };
    }
    private void Changed()
    {
        if(binding) return;
        dirty=true; Text="Crafting & Mastery Editor *";
        catalog.UpdateNames(); recipes.Refresh(); ranks.Refresh(); RefreshTeachingItems(); UpdateRecipeSummary(); UpdateRankSummary();
        status.Text="Unsaved changes. Save validates and commits the complete draft. Retired definitions retain earned bonuses; edited bonuses affect existing masters.";
    }
    private void Error(Exception e) { status.Text=e.Message; MessageBox.Show(this,e.Message,"Crafting & Mastery",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    private async Task RunAsync(Func<Task> action)
    {
        if(busy) return;
        busy=true; UseWaitCursor=true; foreach(Control c in Controls) c.Enabled=false;
        try { await action(); } catch(Exception e) { Error(e); }
        finally { busy=false; UseWaitCursor=false; foreach(Control c in Controls) c.Enabled=true; }
    }
    private async Task ReloadAsync(bool ask)
    {
        if(ask && dirty && MessageBox.Show(this,"Discard this draft and reload the database?","Reload",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
        await RunAsync(async ()=> {
            var result=await Task.Run(repository.Load);
            foreach(var image in icons.Values) image?.Dispose(); icons.Clear();
            catalog=result; original=CraftingCatalog.Copy(result); loaded=true; dirty=false; Text="Crafting & Mastery Editor";
            FilterRecipes(); RefreshRanks(); RefreshTeachingItems();
            status.Text=$"Loaded {catalog.Recipes.Count} recipes and {catalog.Ranks.Count} ranks. Crafting changes need only close/reopen /craft; new items and artwork need their normal deployment.";
        });
    }
    private bool CommitGrids()
    {
        foreach(var grid in detailGrids.Where(g=>!g.IsDisposed)) if(!grid.EndEdit()) { status.Text="Correct the highlighted value before continuing."; return false; }
        return true;
    }
    private async Task SaveAsync()
    {
        if(!loaded || !CommitGrids()) return;
        var errors=CraftingRules.Validate(catalog).Where(i=>i.Error).ToList();
        if(errors.Count!=0) { ShowText("Correct these entries before saving",string.Join("\r\n",errors)); return; }
        if(!dirty) { status.Text="No unsaved changes."; return; }
        var draft=CraftingCatalog.Copy(catalog); var basis=CraftingCatalog.Copy(original);
        await RunAsync(async ()=> {
            await Task.Run(()=>repository.Save(basis,draft));
            // A successful COMMIT is final even if a later reload is unavailable.
            foreach(var r in draft.Recipes) r.Manuals=r.Manuals.OrderBy(m=>m.ItemId).ToList();
            foreach(var r in catalog.Recipes) r.Manuals.Sort((a,b)=>a.ItemId.CompareTo(b.ItemId));
            catalog.Items=draft.Items;
            original=draft; dirty=false; Text="Crafting & Mastery Editor";
            RefreshTeachingItems();
            status.Text="Saved all changes. Close and reopen /craft to refresh. Export crafting seeds to record this content in Git.";
        });
    }
    private void FilterRecipes(int? select=null)
    {
        if(binding || !loaded || !CommitGrids()) return;
        int id=select??selectedRecipe?.Id??0;
        string q=search.Text.Trim();
        var rows=catalog.Recipes.Where(r=>(profession.SelectedIndex<=0 || (int)r.Profession==profession.SelectedIndex-1)
            && (availability.SelectedIndex==0 || r.Enabled==(availability.SelectedIndex==1))
            && (q.Length==0 || r.Name.Contains(q,StringComparison.OrdinalIgnoreCase) || r.Id.ToString().Contains(q)
                || r.OutputItem.ToString().Contains(q) || r.Manuals.Any(m=>m.ItemId.ToString().Contains(q)))).OrderBy(r=>r.Profession).ThenBy(r=>r.SortOrder).ThenBy(r=>r.Id).ToList();
        binding=true; recipes.DataSource=new BindingList<Recipe>(rows); SelectRow(recipes,r=>((Recipe)r).Id==id); binding=false;
        selectedRecipe=recipes.CurrentRow?.DataBoundItem as Recipe; ShowRecipe();
    }
    private static void SelectRow(DataGridView grid,Func<object,bool> match)
    { foreach(DataGridViewRow row in grid.Rows) if(row.DataBoundItem!=null && match(row.DataBoundItem)) { grid.CurrentCell=row.Cells[0]; return; } }
    private void NewRecipe(bool copy=false)
    {
        if(!loaded || !CommitGrids() || (copy && selectedRecipe==null)) return;
        if(catalog.Recipes.Count>=CraftingRules.MaxRecipes) { status.Text="Catalogue capacity is 4096 recipes, including retired entries."; return; }
        var r=copy?CraftingCatalog.Copy(selectedRecipe!):new Recipe { Profession=profession.SelectedIndex>0?(Profession)(profession.SelectedIndex-1):Profession.Smithing };
        r.Id=checked(catalog.Recipes.Select(r=>r.Id).DefaultIfEmpty().Max()+1);
        catalog.Recipes.Add(r); Changed(); binding=true; search.Clear(); profession.SelectedIndex=0; availability.SelectedIndex=0; binding=false; FilterRecipes(r.Id);
    }
    private void RetireRecipe() { if(selectedRecipe==null) return; selectedRecipe.Enabled=!selectedRecipe.Enabled; Changed(); ShowRecipe(); }
    private void DiscardNewRecipe()
    {
        if(selectedRecipe==null) return;
        if(original.Recipes.Any(r=>r.Id==selectedRecipe.Id)) { status.Text="Published recipe IDs must be retired, not removed."; return; }
        catalog.Recipes.Remove(selectedRecipe); selectedRecipe=null; Changed(); FilterRecipes();
    }
    private void DiscardNewRank()
    {
        if(selectedRank==null) return;
        if(original.Ranks.Any(r=>r.Id==selectedRank.Id)) { status.Text="Published rank IDs must be retired, not removed."; return; }
        catalog.Ranks.Remove(selectedRank); selectedRank=null; Changed(); RefreshRanks();
    }
    private int? PickItem(int current)
    {
        using var picker=new ItemPicker(main,this,current,false);
        return picker.ShowDialog(this)==DialogResult.OK?Convert.ToInt32(picker.ReturnValues[0]):null;
    }
    private void OpenItem(int id) { if(id>0) new ItemEditor(main,id).Show(this); }
    private void CreateManual()
    {
        var templates=catalog.Items.Values.Where(i=>i.Enabled && i.Manual && !i.Timed).OrderBy(i=>i.Id).ToList();
        int? id=PickItem(templates.FirstOrDefault()?.Id??0);
        if(!id.HasValue) return;
        if(!catalog.Items.TryGetValue(id.Value,out var item) || !item.Manual || !item.Enabled || item.Timed) { status.Text="Choose an enabled, non-timed manual as the new book's template."; return; }
        new ItemEditor(main,id.Value,true).Show(this);
        status.Text="A copied manual draft is open in Item Editor. Save it there, deploy its item data, then Reload here and assign the new ID.";
    }
    private Image? ItemIcon(int id)
    {
        if(icons.TryGetValue(id,out var image)) return image;
        if(!catalog.Items.TryGetValue(id,out var item)) return null;
        // Reuse PNG resources when installed; otherwise read the configured
        // client's atlas directly so authors need no separate PNG conversion.
        if(!File.Exists(Path.Combine("Resources",$"ItemBtn{item.Texture}.png"))) {
            try {
                using var atlas=CraftingEmblem.Read(Path.Combine(main.pSettings.ClientPath,"Data","Interface",$"ItemBtn{item.Texture}.tex"));
                if(item.Row>=0 && item.Column>=0 && item.Row<atlas.Height/32 && item.Column<atlas.Width/32)
                    image=atlas.Clone(new Rectangle(item.Column*32,item.Row*32,32,32),PixelFormat.Format32bppArgb);
            } catch { /* Missing or unsupported atlases use the toolbox fallback. */ }
        }
        if(image==null) try { image=main.GetIcon("ItemBtn",item.Texture.ToString(),item.Row,item.Column); } catch { image=null; }
        icons[id]=image; return image;
    }
    private void ShowText(string title,string text)
    {
        using var f=new Form { Text=title, Size=new(880,620), StartPosition=FormStartPosition.CenterParent, MinimizeBox=false };
        f.Controls.Add(new TextBox { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Both, Dock=DockStyle.Fill, Text=text, Font=new Font("Consolas",10), WordWrap=false }); f.ShowDialog(this);
    }
}
