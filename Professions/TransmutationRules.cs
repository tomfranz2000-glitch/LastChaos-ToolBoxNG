using System.Text;
using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG.Professions;

public static class TransmutationRules
{
    // Mirrors the game prototype's non-negotiable item lifecycle restrictions.
    const long UnsafeFlags=(1L<<7)|(1L<<12)|(1L<<16)|(1L<<18)|(1L<<22)|(1L<<26);
    static bool Ordinary(CraftingCatalog c,int id,out CraftingItem? item,bool equipment=false)
        =>c.Items.TryGetValue(id,out item)&&item.Enabled&&!item.Rebirth&&(item.Flags&(equipment?UnsafeFlags&~(1L<<12):UnsafeFlags))==0
            &&c.Professions.Infusions.MaxUses.TryGetValue(id,out int uses)&&uses==-1;
    static bool EligibleEquipmentType(CraftingItem item)=>item.Type==0&&item.Subtype is not (8 or 9 or 10)
        ||item.Type==1&&item.Subtype<=5;
    public static long FormulaPoints(int level,int numerator,int denominator)
        =>level>0&&numerator>0&&denominator>0?Math.Max(1,(long)level*numerator/denominator):0;
    public static List<CraftingIssue> Validate(CraftingCatalog c)
    {
        var issues=new List<CraftingIssue>();void Error(string target,string text)=>issues.Add(new(true,target,text));
        var p=c.Professions;var settings=p.Rows(TransmutationSchema.Settings);var facilities=p.Rows(TransmutationSchema.Facility);
        var offers=p.Rows(TransmutationSchema.Offers);var rules=p.Rows(TransmutationSchema.ItemRules);var costs=p.Rows(TransmutationSchema.ConstructionCosts);
        if(settings.Count!=1||settings[0].Int("a_index")!=1)Error("Transmutation rules","Exactly one settings definition with ID 1 is required.");
        if(facilities.Count!=1||facilities[0].Int("a_index")!=1)Error("Transmutation Crucible","Exactly one facility definition with ID 1 is required.");
        if(offers.Count>16)Error("Transmutation outputs","At most 16 output definitions are supported, including retired outputs.");
        if(rules.Count>4096)Error("Equipment exceptions","At most 4096 equipment exceptions are supported.");
        foreach(var offer in offers) {
            int id=offer.Int("a_output_item_index");string target="Transmutation output #"+offer.Int("a_index");
            if(!Ordinary(c,id,out var item)||item!.Type!=4||item.Subtype!=3||!item.Stackable||item.StackLimit<offer.Int("a_output_count"))
                Error(target,"Choose an enabled ordinary stackable upgrading material (type 4, subtype 3), with max use -1, no special lifecycle flags and a stack limit covering the output quantity.");
        }
        foreach(var rule in rules) {
            int id=rule.Int("a_item_index");string target="Equipment exception #"+id;
            if(!c.Items.TryGetValue(id,out var referenced)||!referenced.Enabled)Error(target,"Choose an existing enabled item. Exclusions may refer to any enabled item; disabled prototypes are not loaded by the game.");
            else if(rule.Enabled&&(!Ordinary(c,id,out var item,true)||!EligibleEquipmentType(item!)||(item!.Flags&(1L<<2))==0||(item.Flags&(1L<<8))!=0))
                Error(target,"Allow conversion requires ordinary upgradeable weapons or armor with max use -1. Tools, costumes, cash items and special lifecycle items cannot be enabled by an exception.");
        }
        foreach(var group in costs.GroupBy(r=>r.Int("a_facility_index"))) {
            if(group.Count()>8||group.Select(r=>r.Int("a_item_index")).Distinct().Count()!=group.Count())
                Error("Crucible construction costs","Use at most eight different materials with no duplicate items.");
            if(!group.OrderBy(r=>r.Int("a_slot")).Select(r=>r.Int("a_slot")).SequenceEqual(Enumerable.Range(0,group.Count())))
                Error("Crucible construction costs","Material order must be contiguous starting at zero; use Move up/down or remove and re-add the row.");
        }
        foreach(var cost in costs) {
            int id=cost.Int("a_item_index");
            if(!c.Items.TryGetValue(id,out var item)||!item.Enabled||!item.Stackable||item.Rebirth||id==19
                ||(item.Flags&((1L<<7)|(1L<<16)|(1L<<18)|(1L<<22)))!=0
                ||!p.Infusions.MaxUses.TryGetValue(id,out int uses)||uses!=-1
                ||p.Rows(InfusionSchema.Consumables).Any(r=>r.Int("a_item_index")==id))
                Error("Crucible construction material #"+id,"Choose an enabled ordinary stackable construction material, excluding gold and permanent potions.");
        }
        foreach(var facility in facilities) {
            int rank=facility.Int("a_required_artisan_rank");
            if(rank!=0&&!c.Ranks.Any(r=>r.Id==rank))Error("Transmutation Crucible","Required Artisan Rank must be None or an existing permanent rank ID; retired ranks remain valid.");
            foreach(string column in new[]{"a_name","a_plot_name"})
                if(facility.Get(column).EnumerateRunes().Count()>64)Error("Transmutation Crucible",(column=="a_name"?"Facility":"Plot")+" name must contain at most 64 characters.");
            foreach(string column in new[]{"a_model","a_plot_model"}) {
                string model=facility.Get(column).Replace('\\','/');
                if(!model.StartsWith("Data/",StringComparison.Ordinal)||!model.EndsWith(".smc",StringComparison.Ordinal)||model.Contains(':')||model.Any(ch=>ch<32||ch>126)||model.Split('/').Any(part=>part.Length==0||part is "." or ".."))
                    Error("Transmutation Crucible",(column=="a_model"?"Facility":"Plot")+" model must be an ASCII relative Data/ path ending in .smc, without parent traversal.");
            }
        }
        return issues;
    }
}
