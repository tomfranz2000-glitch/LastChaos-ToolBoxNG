using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private void NewGatheringResource()
    {
        if(!loaded||!CommitGrids())return;
        int? item=PickItem(0);if(!item.HasValue)return;
        using var dialog=new Form{Text="Resource profession",Size=new(480,190),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
        var choice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=230};
        choice.Items.AddRange(["Mining","Herbalism","Fishing","Wave Absorption"]);
        choice.SelectedIndex=gatheringProfession.SelectedIndex>0?gatheringProfession.SelectedIndex-1:0;
        var fields=Fields();fields.Controls.Add(Label(catalog.ItemName(item.Value)));fields.Controls.Add(choice);
        fields.Controls.Add(Button("Create resource mastery",()=>dialog.DialogResult=DialogResult.OK));dialog.Controls.Add(fields);
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{SelectMasteryResource(item.Value,choice.SelectedIndex+1);}catch(Exception e){Error(e);}
    }

    private void DiscardNewGatheringResource()
    {
        if(selectedGathering==null)return;
        if(original.Gathering.Any(r=>r.ItemId==selectedGathering.ItemId)) {status.Text="Published resource mastery must remain to preserve earned rewards. Retire its sources instead.";return;}
        if(ProfessionItemUses(selectedGathering.ItemId).Any()) {status.Text="Remove this new resource from its draft sources before discarding it.";return;}
        catalog.Gathering.Remove(selectedGathering);selectedGathering=null;Changed();RefreshGathering();
    }

    private IEnumerable<string> ProfessionItemUses(int item)
    {
        var uses=new[]{("t_gathering_node","a_item_index","Base node output"),
            ("t_gathering_node_loot","a_item_index","Node weighted outcome"),
            ("t_gathering_tool","a_item_index","Gathering tool"),
            ("t_herb_population","a_visual_tool_index","Herb visual tool"),
            ("t_herb_loot","a_item_index","Herb outcome"),
            ("t_fishing_settings","a_rod_item_index","Required fishing rod"),
            ("t_fishing_loot","a_item_index","Fishing catch")};
        foreach(var (table,column,label) in uses)
            foreach(var row in catalog.Professions.Rows(table).Where(r=>r.Int(column)==item))
                yield return $"{label}: {row}{(row.Enabled?"":" (disabled)")}";
    }
    private string GatheringSourceSummary(int item)
    {
        var sources=ProfessionItemUses(item).ToList();
        return sources.Count==0?"No source currently awards this resource. Add it to a profession source or loot table.":
            "Configured sources:\r\n"+string.Join("\r\n",sources.Take(5))+(sources.Count>5?$"\r\n… {sources.Count-5} more; use Where used.":"");
    }
    private void ShowResourceUses(int item)=>ShowText("Where this item is used",string.Join("\r\n",catalog.WhereUsed(item).Concat(ProfessionItemUses(item)).Distinct()));
}
