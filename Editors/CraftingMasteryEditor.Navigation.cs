using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using System.Text.Json;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private sealed class ContentPages : TabControl
    {
        public override Rectangle DisplayRectangle => ClientRectangle;
    }
    private readonly TreeView navigation = new() { Dock=DockStyle.Fill, HideSelection=false, FullRowSelect=true, ShowLines=false, ItemHeight=29, BorderStyle=BorderStyle.None };
    private readonly Label pageHeading = new() { Dock=DockStyle.Fill, Text="Professions", UseMnemonic=false, Font=new Font("Segoe UI",12,FontStyle.Bold), Padding=new(10,7,0,0) };
    private readonly Label homeSummary = new() { AutoSize=true, MaximumSize=new(950,0) };
    private readonly TextBox applyNotes = new() { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle };
    private TabPage? homePage;

    private Control BuildNavigationHost()
    {
        tabs.Appearance=TabAppearance.FlatButtons; tabs.ItemSize=new(1,1); tabs.SizeMode=TabSizeMode.Fixed; tabs.Multiline=true;
        var split=new SplitContainer { Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,Size=new(1350,750),SplitterDistance=230,Panel1MinSize=200,Panel2MinSize=700 };
        split.Panel1.Controls.Add(navigation);
        var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2 };
        body.ColumnStyles.Add(new(SizeType.Percent,100));
        body.RowStyles.Add(new(SizeType.Absolute,43)); body.RowStyles.Add(new(SizeType.Percent,100));
        body.Controls.Add(pageHeading,0,0); body.Controls.Add(tabs,0,1); split.Panel2.Controls.Add(body);
        navigation.BeforeSelect+=(_,e)=> { if(!CommitGrids())e.Cancel=true; };
        navigation.AfterSelect+=(_,e)=> {
            if(!CommitGrids())return;
            if(e.Node?.Tag is Action action) { action();pageHeading.Text=e.Node.Text; }
        };
        return split;
    }

    private void BuildNavigationTree()
    {
        TreeNode Entry(TreeNode parent,string text,Action action) { var node=parent.Nodes.Add(text);node.Name=text;node.Tag=action;return node; }
        var start=navigation.Nodes.Add("Overview & apply changes");start.Name="Overview";start.Tag=(Action)(()=>{if(homePage!=null)tabs.SelectedTab=homePage;RefreshHome();});
        var craft=navigation.Nodes.Add("Crafting");
        foreach(var name in new[]{"Smithing","Alchemy","Cooking"}) {
            int p=Array.IndexOf(new[]{"Smithing","Alchemy","Cooking"},name)+1;
            Entry(craft,name,()=>{tabs.SelectedIndex=0;profession.SelectedIndex=p;});
        }
        Entry(craft,"Teaching items",()=>tabs.SelectedIndex=1);
        Entry(craft,"Crafting skill cap",()=>tabs.SelectedIndex=4);
        var infusions=navigation.Nodes.Add("Infusions");infusions.Name="Infusions";
        infusions.Tag=(Action)(()=>NavigateProfessionPage(5,InfusionSchema.Offers));
        Entry(infusions,"Brewing recipes",()=>NavigateProfessionPage(5,InfusionSchema.Offers));
        Entry(infusions,"Extra ingredients",()=>NavigateProfessionPage(5,InfusionSchema.Ingredients));
        Entry(infusions,"Permanent potions",()=>NavigateProfessionPage(5,InfusionSchema.Consumables));
        Entry(infusions,"Permanent effects",()=>NavigateProfessionPage(5,InfusionSchema.Effects));
        Entry(infusions,"Alchemy station",()=>NavigateProfessionPage(5,InfusionSchema.Facility));
        Entry(infusions,"Construction costs",()=>NavigateProfessionPage(5,InfusionSchema.ConstructionCosts));
        var transmutation=navigation.Nodes.Add("Transmutation");transmutation.Name="Transmutation";
        transmutation.Tag=(Action)(()=>NavigateProfessionPage(6,TransmutationSchema.Offers));
        Entry(transmutation,"Upgrading items",()=>NavigateProfessionPage(6,TransmutationSchema.Offers));
        Entry(transmutation,"Reservoir and equipment value",()=>NavigateProfessionPage(6,TransmutationSchema.Settings));
        Entry(transmutation,"Equipment exceptions",()=>NavigateProfessionPage(6,TransmutationSchema.ItemRules));
        Entry(transmutation,"Transmutation Crucible",()=>NavigateProfessionPage(6,TransmutationSchema.Facility));
        Entry(transmutation,"Crucible construction costs",()=>NavigateProfessionPage(6,TransmutationSchema.ConstructionCosts));
        var display=navigation.Nodes.Add("Accessory Display");display.Name="Accessory Display";
        display.Tag=(Action)(()=>NavigateProfessionPage(7,AccessoryDisplaySchema.Facility));
        Entry(display,"Display and rank requirement",()=>NavigateProfessionPage(7,AccessoryDisplaySchema.Facility));
        Entry(display,"Display construction costs",()=>NavigateProfessionPage(7,AccessoryDisplaySchema.ConstructionCosts));
        var gather=navigation.Nodes.Add("Gathering");
        foreach(var pair in new[]{(1,"Mining"),(2,"Herbalism"),(3,"Fishing"),(4,"Wave Absorption")}) {
            int p=pair.Item1;Entry(gather,pair.Item2,()=>NavigateProfession(p));
        }
        var shared=navigation.Nodes.Add("Shared progression");
        Entry(shared,"Resources & mastery",()=>{tabs.SelectedIndex=3;gatheringProfession.SelectedIndex=0;});
        Entry(shared,"Artisan Ranks",()=>tabs.SelectedIndex=2);
        navigation.ExpandAll();navigation.SelectedNode=start;
    }

    private void BuildHomePage()
    {
        homePage=new("Overview");tabs.TabPages.Add(homePage);
        var welcome=Fields();welcome.Dock=DockStyle.Top;welcome.AutoSize=true;
        welcome.Controls.Add(Label("Create and balance profession content"));welcome.Controls.Add(homeSummary);
        welcome.Controls.Add(Label("1. Choose a profession on the left. Edit its rules, sources and rewards.\r\n2. Validate, then Save all changes. Save updates the database; it does not export files.\r\n3. Follow the apply steps below, then export profession content to record it in Git."));
        welcome.Controls.Add(Label("Sources define where and how resources are gathered. Resource mastery defines XP and permanent tier rewards.\r\nItems, NPC models, shops and world geometry are maintained in their existing editors."));
        var actions=Flow();actions.Controls.AddRange([Button("Validate draft",Overview),Button("Refresh apply guide",()=>ShowApplyGuide(original,catalog)),Button("Item Editor",()=>new ItemEditor(main).Show(this)),Button("NPC Editor",()=>new NPCEditor(main).Show(this)),Button("Shop Editor",()=>new ShopEditor(main).Show(this))]);
        homePage.Controls.Add(Vertical(welcome,actions,applyNotes));RefreshHome();
    }

    private void RefreshHome()
    {
        if(homePage==null)return;
        homeSummary.Text=loaded?$"{catalog.Recipes.Count:N0} recipes • {catalog.Gathering.Count:N0} resource masteries • {catalog.Ranks.Count:N0} Artisan Ranks\r\n{catalog.Professions.Rows("t_gathering_node").Count:N0} Mining/Wave sources • {catalog.Professions.Rows("t_herb_population").Count:N0} herb populations • {catalog.Professions.Rows("t_fishing_water").Count:N0} fishing waters\r\n{catalog.Professions.Rows(TransmutationSchema.Offers).Count:N0} Transmutation outputs • {catalog.Professions.Rows(TransmutationSchema.Settings).FirstOrDefault()?.Int("a_capacity")??0:N0} point reservoir capacity":"Loading profession content…";
        if(loaded)ShowApplyGuide(original,catalog);
    }

    private void ShowApplyGuide(CraftingCatalog before,CraftingCatalog after)
    {
        static string Snapshot(CraftingCatalog c,string table)=>JsonSerializer.Serialize(c.Professions.Rows(table));
        bool ChangedTable(string table)=>Snapshot(before,table)!=Snapshot(after,table);
        string Owners(CraftingCatalog c)=>string.Join(";",c.Professions.Rows("t_herb_population").Where(r=>r.Enabled)
            .Select(r=>$"{r.Int("a_index")}/{r.Int("a_zone_index")}/{r.Int("a_npc_index")}").Order());
        bool ownership=Owners(before)!=Owners(after);
        bool herbs=new[]{"t_herb_population","t_herb_spawn_point","t_herb_loot"}.Any(ChangedTable);
        applyNotes.Text="APPLYING SAVED CONTENT\r\n\r\n"+
            "Crafting recipes/settings: close and reopen /craft.\r\nResource mastery, Mining/Wave rules and Fishing: close and reopen /gather.\r\nArtisan Rank definitions: reopen /artisan.\r\nInfusions: reopen the Alchemy station. Station placement/model changes may require re-entering the private hall.\r\nTransmutation: reopen the Crucible or plot. Paid batches keep accepted terms; point-cap changes never delete stored points.\r\n\r\n"+
            (ownership?"THIS DRAFT changes enabled herb population ownership. A GameServer restart is required after Save.\r\n":
             herbs?"THIS DRAFT changes existing herb rules/sites/loot. After Save, an administrator must use /herbpop reload.\r\n":
             "Herb rules/sites/loot: an administrator uses /herbpop reload. Adding/removing an enabled population or changing its zone/NPC requires GameServer restart.\r\n")+
            "Initial herb count applies at server startup; it does not reset live plants on reload.\r\n\r\n"+
            "New item/NPC prototypes and static NPC placements follow the Item/NPC editor deployment process. New artwork and world geometry require client asset deployment.\r\n\r\n"+
            "Export profession content writes authored seeds into the chosen game checkout. Review and commit that diff. Character skill, XP, inventory and earned ranks are never exported.";
    }

    private void NavigateInitialItem(int item)
    {
        if(catalog.Items.TryGetValue(item,out var value)&&value.Manual) { SelectNavigation("Teaching items");bookItem.Value=item;tabs.SelectedIndex=1;pageHeading.Text="Teaching items"; }
        else if(catalog.Professions.Rows(InfusionSchema.Consumables).Any(r=>r.Int("a_item_index")==item)) {SelectNavigation("Permanent potions");NavigatePermanentPotion(item);pageHeading.Text="Permanent potions";}
        else if(catalog.Professions.Rows(InfusionSchema.Effects).FirstOrDefault(r=>r.Int("a_item_index")==item) is { } infusion) {SelectNavigation("Permanent effects");NavigateInfusionEffect(infusion.Int("a_index"));pageHeading.Text="Permanent infusion effects";}
        else if(catalog.Gathering.Any(r=>r.ItemId==item)) OpenGatheringMastery(item);
        else if(catalog.Professions.Rows("t_gathering_tool").FirstOrDefault(r=>r.Int("a_item_index")==item) is { } tool) { NavigateProfessionPage(tool.Int("a_profession"),"t_gathering_tool");pageHeading.Text="Gathering tools"; }
        else if(catalog.Professions.Rows("t_fishing_settings").Any(r=>r.Int("a_rod_item_index")==item)) {NavigateProfessionPage(3,"t_fishing_settings");pageHeading.Text="Fishing rod";}
        else if(catalog.Professions.Rows("t_herb_population").Any(r=>r.Int("a_visual_tool_index")==item)) {NavigateProfessionPage(2,"t_herb_population");pageHeading.Text="Herb harvesting";}
        else {tabs.SelectedIndex=0;search.Text=item.ToString();pageHeading.Text="Recipes using item "+item;}
    }

    private void EnsureGatheringResource(int item,int professionId)
    {
        if(item<=0||professionId is <1 or >4)throw new InvalidOperationException("Choose a valid resource and gathering profession.");
        var existing=catalog.Gathering.FirstOrDefault(r=>r.ItemId==item);
        if(existing!=null) {
            if(existing.Profession!=professionId)throw new InvalidOperationException("This item already has mastery in another profession. Choose another item; published resource identities must remain stable.");
            return;
        }
        if(!catalog.Items.TryGetValue(item,out var data)||!data.Enabled||!data.Stackable||data.Rebirth||item==19)
            throw new InvalidOperationException("Choose an enabled stackable resource item with a valid stack limit.");
        catalog.Gathering.Add(new(){ItemId=item,Profession=professionId,MasteryXp=10,BronzeRequired=50,SilverRequired=150,MasteryRequired=300});
        Changed();RefreshGathering();
    }
    private void SelectMasteryResource(int item,int professionId)
    { EnsureGatheringResource(item,professionId);OpenGatheringMastery(item); }
    private void OpenGatheringMastery(int item)
    {
        SelectNavigation("Resources & mastery");tabs.SelectedIndex=3;gatheringProfession.SelectedIndex=0;gatheringSearch.Text=item.ToString();
        selectedGathering=catalog.Gathering.FirstOrDefault(r=>r.ItemId==item);RefreshGathering();pageHeading.Text="Resource mastery";
    }
    private void SelectNavigation(string name)
    {
        var found=navigation.Nodes.Find(name,true).FirstOrDefault();if(found!=null)navigation.SelectedNode=found;
    }

}
