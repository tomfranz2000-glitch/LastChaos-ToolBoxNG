using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private readonly DataGridView gatheringRows=Grid(true);
    private readonly Panel gatheringDetail=new(){Dock=DockStyle.Fill};
    private readonly TextBox gatheringSearch=new(){Width=250,PlaceholderText="Resource name or item ID"};
    private readonly ComboBox gatheringProfession=new(){Width=155,DropDownStyle=ComboBoxStyle.DropDownList};
    private GatheringResource? selectedGathering;
    private Label? gatheringSummary;

    private void BuildGatheringMastery()
    {
        var page=new TabPage("Resources & mastery");tabs.TabPages.Add(page);
        var split=new SplitContainer{Size=new(1350,750),Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,Panel1MinSize=330,Panel2MinSize=580,SplitterDistance=430};
        Shown+=(_,_)=>split.SplitterDistance=Math.Max(330,Math.Min(450,split.Width-585));page.Controls.Add(split);
        gatheringProfession.Items.AddRange(["All professions","Mining","Herbalism","Fishing","Wave Absorption"]);gatheringProfession.SelectedIndex=0;
        var filters=Flow();filters.Controls.AddRange([gatheringSearch,gatheringProfession]);
        Column(gatheringRows,"ItemId","Item ID",55);Column(gatheringRows,"Name","Resource",150);Column(gatheringRows,"ProfessionName","Profession",90);
        var actions=Flow();actions.Controls.AddRange([Button("New resource",NewGatheringResource),Button("Open item",()=>{if(selectedGathering!=null)OpenItem(selectedGathering.ItemId);}),Button("Where used",()=>{if(selectedGathering!=null)ShowResourceUses(selectedGathering.ItemId);}),Button("Discard new",DiscardNewGatheringResource)]);
        split.Panel1.Controls.Add(Vertical(filters,actions,gatheringRows));split.Panel2.Controls.Add(gatheringDetail);
        gatheringSearch.TextChanged+=(_,_)=>RefreshGathering();gatheringProfession.SelectedIndexChanged+=(_,_)=>RefreshGathering();
        gatheringRows.CurrentCellChanged+=(_,_)=>{if(binding||!CommitGrids())return;selectedGathering=gatheringRows.CurrentRow?.DataBoundItem as GatheringResource;ShowGathering();};
    }
    private void RefreshGathering()
    {
        if(binding||!loaded||!CommitGrids())return;
        int id=selectedGathering?.ItemId??0;string q=gatheringSearch.Text.Trim();
        var rows=catalog.Gathering.Where(r=>(gatheringProfession.SelectedIndex<=0||r.Profession==gatheringProfession.SelectedIndex)
            &&(q.Length==0||r.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||r.ItemId.ToString().Contains(q))).OrderBy(r=>r.Profession).ThenBy(r=>r.ItemId).ToList();
        binding=true;gatheringRows.DataSource=new BindingList<GatheringResource>(rows);SelectRow(gatheringRows,r=>((GatheringResource)r).ItemId==id);binding=false;
        selectedGathering=gatheringRows.CurrentRow?.DataBoundItem as GatheringResource;ShowGathering();
    }
    private void ShowGathering()
    {
        ClearPanel(gatheringDetail);detailGrids.RemoveAll(g=>g.IsDisposed);gatheringSummary=null;
        if(selectedGathering is not GatheringResource r){gatheringDetail.Controls.Add(Label("No gathering resources match this filter."));return;}
        var fields=Fields();fields.Dock=DockStyle.Top;fields.AutoSize=true;
        fields.Controls.Add(Label($"{r.Name} • item {r.ItemId} • {r.ProfessionName}"));
        fields.Controls.Add(Label(GatheringSourceSummary(r.ItemId)));
        fields.Controls.Add(Label("Mastery is earned for resources actually awarded. One shared definition serves every source of this item.\r\nUse the profession pages to change where and how it is gathered."));
        Number(fields,"Mastery XP per awarded item",0,int.MaxValue,r.MasteryXp,v=>r.MasteryXp=(int)v);
        Number(fields,"Bronze — total mastery XP",1,int.MaxValue,r.BronzeRequired,v=>r.BronzeRequired=(int)v);
        Number(fields,"Silver — total mastery XP",1,int.MaxValue,r.SilverRequired,v=>r.SilverRequired=(int)v);
        Number(fields,"Gold — total mastery XP",1,int.MaxValue,r.MasteryRequired,v=>r.MasteryRequired=(int)v);
        gatheringSummary=Label("");fields.Controls.Add(gatheringSummary);UpdateGatheringSummary();
        gatheringDetail.Controls.Add(Vertical(fields,BonusGrid(r.Bonuses,true)));
    }
    private void UpdateGatheringSummary()
    {
        if(selectedGathering is not GatheringResource r||gatheringSummary is null||gatheringSummary.IsDisposed)return;
        gatheringSummary.Text=$"Additional XP: Bronze {r.BronzeRequired:N0}; Silver {(long)r.SilverRequired-r.BronzeRequired:N0}; Gold {(long)r.MasteryRequired-r.SilverRequired:N0}.\r\nEach tier contributes one Artisan Rank point. Rewards add to earlier tiers. Zero XP pauses progression.";
    }
}
