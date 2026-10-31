using LastChaos_ToolBoxNG.Professions;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private void AddTransmutationSummary(FlowLayoutPanel fields,FlowLayoutPanel quick,ProfessionBrowser browser,ProfessionRow row)
    {
        if(!TransmutationSchema.IsTable(browser.Table.Name))return;
        var summary=Label(TransmutationSummary(browser.Table.Name,row));
        summary.Name="TransmutationExplanation";summary.MaximumSize=new(650,0);
        fields.Controls.Add(summary);
        if(browser.Table.Name==TransmutationSchema.Offers)
            quick.Controls.Add(Button("Open upgrading item",()=>OpenItem(row.Int("a_output_item_index"))));
        if(browser.Table.Name==TransmutationSchema.Settings)
            quick.Controls.Add(Button("Equipment exceptions…",()=>NavigateProfessionPage(6,TransmutationSchema.ItemRules)));
    }

    private string TransmutationSummary(string table,ProfessionRow row)=>table switch {
        TransmutationSchema.Offers=>$"One operation spends {row.Int("a_point_cost"):N0} points immediately and produces {row.Int("a_output_count"):N0} × {catalog.ItemName(row.Int("a_output_item_index"))} after {ReadableTransmutationTime(row.Int("a_duration_sec"))}. Five queued operations cost {(long)row.Int("a_point_cost")*5:N0} points and take {ReadableTransmutationTime((long)row.Int("a_duration_sec")*5)}. Completed products wait safely without stopping the next operation.",
        TransmutationSchema.Settings=>$"Example: level-100 equipment gives {TransmutationRules.FormulaPoints(100,row.Int("a_points_numerator"),row.Int("a_points_denominator")):N0} points. The reservoir holds {row.Int("a_capacity"):N0} unspent points. A conversion that would overflow is refused in full. Lowering capacity does not erase existing points; spending from that balance remains possible. Paid work and uncollected products do not use reservoir capacity.",
        TransmutationSchema.ItemRules=>!row.Enabled?"This item cannot be converted. Remove this exception to return to the normal equipment safety checks and level formula.":row.Int("a_points_override")==0?"This item uses the configured level formula. Only ordinary safe weapons and armor are accepted; equipment still must be unequipped and free of instance restrictions.":$"Each eligible piece of this equipment gives exactly {row.Int("a_points_override"):N0} points. This does not bypass equipment safety checks. Enhancement is destroyed with the item and grants no extra points.",
        TransmutationSchema.Facility=>"Artisan Rank grants permission to construct the Crucible. Once built, the character keeps it after rank, construction-cost or requirement edits. The Alchemy station and its single-brew rule remain separate.",
        _=>"Construction materials are consumed once, together, after the player confirms. An empty cost list permits free construction."
    };
    private static string ReadableTransmutationTime(long seconds)
    {
        if(seconds<0)return "an invalid duration";
        long hours=seconds/3600,minutes=seconds%3600/60,remainder=seconds%60;
        return string.Join(" ",new[]{hours>0?$"{hours:N0} h":"",minutes>0?$"{minutes} min":"",remainder>0||seconds==0?$"{remainder} sec":""}.Where(part=>part.Length>0));
    }
}
