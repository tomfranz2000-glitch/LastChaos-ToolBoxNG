using System.Text;
using LastChaos_ToolBoxNG.Crafting;
namespace LastChaos_ToolBoxNG.Professions;
public static class AccessoryDisplayRules
{
    public static List<CraftingIssue> Validate(CraftingCatalog c)
    {
        var issues=new List<CraftingIssue>();void Error(string target,string text)=>issues.Add(new(true,target,text));
        var p=c.Professions;var facilities=p.Rows(AccessoryDisplaySchema.Facility);var costs=p.Rows(AccessoryDisplaySchema.ConstructionCosts);
        if(facilities.Count!=1||facilities[0].Int("a_index")!=1)Error("Accessory Display","Exactly one facility definition with ID 1 is required.");
        foreach(var group in costs.GroupBy(r=>r.Int("a_facility_index"))) {
            if(group.Count()>8||group.Select(r=>r.Int("a_item_index")).Distinct().Count()!=group.Count())
                Error("Display construction costs","Use at most eight different materials with no duplicate items.");
            if(!group.OrderBy(r=>r.Int("a_slot")).Select(r=>r.Int("a_slot")).SequenceEqual(Enumerable.Range(0,group.Count())))
                Error("Display construction costs","Material order must be contiguous starting at zero; use Move up/down or remove and re-add the row.");
        }
        foreach(var cost in costs) {
            int id=cost.Int("a_item_index");
            if(!c.Items.TryGetValue(id,out var item)||!item.Enabled||!item.Stackable||item.Rebirth||id==19
                ||(item.Flags&((1L<<7)|(1L<<16)|(1L<<18)|(1L<<22)))!=0
                ||!p.Infusions.MaxUses.TryGetValue(id,out int uses)||uses!=-1
                ||p.Rows(InfusionSchema.Consumables).Any(r=>r.Int("a_item_index")==id))
                Error("Display construction material #"+id,"Choose an enabled ordinary stackable construction material, excluding gold and permanent potions.");
        }
        foreach(var facility in facilities) {
            int rank=facility.Int("a_required_artisan_rank");
            if(rank!=0&&!c.Ranks.Any(r=>r.Id==rank))Error("Accessory Display","Required Artisan Rank must be None or an existing permanent rank ID; retired ranks remain valid.");
            foreach(string column in new[]{"a_name","a_plot_name"})
                if(facility.Get(column).EnumerateRunes().Count()>64)Error("Accessory Display",(column=="a_name"?"Facility":"Plot")+" name must contain at most 64 characters.");
            foreach(string column in new[]{"a_model","a_plot_model"}) {
                string model=facility.Get(column).Replace('\\','/');
                if(!model.StartsWith("Data/",StringComparison.Ordinal)||!model.EndsWith(".smc",StringComparison.Ordinal)||model.Contains(':')||model.Any(ch=>ch<32||ch>126)||model.Split('/').Any(part=>part.Length==0||part is "." or ".."))
                    Error("Accessory Display",(column=="a_model"?"Facility":"Plot")+" model must be an ASCII relative Data/ path ending in .smc, without parent traversal.");
            }
        }
        return issues;
    }
}
