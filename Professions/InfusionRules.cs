using System.Globalization;
using System.Text;
using LastChaos_ToolBoxNG.Crafting;
using MySqlConnector;

namespace LastChaos_ToolBoxNG.Professions;

public sealed record InfusionPotion(int ItemId,int SkillId,int SkillLevel,string SkillName,bool Supported,string Explanation,int[] MagicIds);
public sealed class InfusionMetadata
{
    public Dictionary<int,InfusionPotion> Potions { get; } = [];
    public Dictionary<int,int> MaxUses { get; } = [];
    public static readonly IReadOnlyDictionary<string,string[]> Columns = new Dictionary<string,string[]> {
        ["t_item"]=["a_index","a_type_idx","a_subtype_idx","a_enable","a_num_0","a_num_1","a_max_use"],
        ["t_skill"]=["a_index","a_type","a_flag","a_targetType","a_maxLevel","a_useState","a_appState"],
        ["t_skilllevel"]=["a_index","a_level","a_durtime","a_use_count","a_magicIndex1","a_magicLevel1","a_magicIndex2","a_magicLevel2","a_magicIndex3","a_magicLevel3"],
        ["t_magic"]=["a_index","a_type","a_subtype","a_damagetype"],
        ["t_magiclevel"]=["a_index","a_level"]
    };
    public static InfusionMetadata Read(MySqlConnection db,MySqlTransaction tx,bool locked,string locale)
    {
        var tables=new Dictionary<string,List<Dictionary<string,string>>>();var names=new Dictionary<int,string>();
        foreach(var pair in Columns) {
            string[] fields=pair.Key=="t_skill"?[..pair.Value,"a_name_"+locale]:pair.Value;
            using var command=new MySqlCommand("SELECT "+string.Join(",",fields.Select(f=>"`"+f+"`"))+" FROM `"+pair.Key+"`"+(locked?" LOCK IN SHARE MODE":""),db,tx);
            using var reader=command.ExecuteReader();var rows=new List<Dictionary<string,string>>();
            while(reader.Read()) {var row=new Dictionary<string,string>();for(int i=0;i<fields.Length;i++)row[fields[i]]=Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture)??"";rows.Add(row);
                if(pair.Key=="t_skill")names[int.Parse(row["a_index"],CultureInfo.InvariantCulture)]=row["a_name_"+locale];}
            tables[pair.Key]=rows;
        }
        return FromRows(tables,names);
    }
    public static InfusionMetadata FromRows(IReadOnlyDictionary<string,List<Dictionary<string,string>>> tables,IReadOnlyDictionary<int,string>? names=null)
    {
        static int N(Dictionary<string,string> r,string c)=>int.TryParse(r.GetValueOrDefault(c),NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:0;
        var result=new InfusionMetadata();foreach(var item in tables["t_item"])result.MaxUses[N(item,"a_index")]=N(item,"a_max_use");var skills=tables["t_skill"].ToDictionary(r=>N(r,"a_index"));
        var levels=tables["t_skilllevel"].ToDictionary(r=>(N(r,"a_index"),N(r,"a_level")));
        var magics=tables["t_magic"].ToDictionary(r=>N(r,"a_index"));
        var magicLevels=tables["t_magiclevel"].Select(r=>(N(r,"a_index"),N(r,"a_level"))).ToHashSet();
        foreach(var item in tables["t_item"].Where(r=>N(r,"a_type_idx")==6&&N(r,"a_subtype_idx")==5)) {
            int id=N(item,"a_index"),skill=N(item,"a_num_0"),level=N(item,"a_num_1");string error="";var magicIds=new List<int>();
            if(N(item,"a_enable")!=1)error="The original potion must be enabled.";
            else if(!skills.TryGetValue(skill,out var s)||!levels.TryGetValue((skill,level),out var l)||level<1||level>127||level>N(s,"a_maxLevel"))error="The potion's exact skill level must exist, be within its prototype maximum, and use level 1–127.";
            else if((N(s,"a_flag")&2)==0||(N(s,"a_flag")&(4|64|128|256|512|1024|2048|4096|131072))!=0||N(s,"a_targetType")!=0||N(s,"a_useState")!=0||N(s,"a_appState")!=0||N(l,"a_durtime")<=0||N(l,"a_use_count")!=0)error="Requires an unconditional beneficial self-only buff without limited uses or special lifecycle flags.";
            else {
                for(int slot=1;slot<=3;slot++) {
                    int magic=N(l,"a_magicIndex"+slot),magicLevel=N(l,"a_magicLevel"+slot);if(magic<=0)continue;
                    magicIds.Add(magic);
                    if(!magics.TryGetValue(magic,out var m)||!magicLevels.Contains((magic,magicLevel)))error="A configured magic or magic level is missing.";
                    else if(!(id==2853&&skill==510&&level==1) && (N(m,"a_type")!=0 || N(m,"a_damagetype") is not (1 or 2)))error="Only stat additions/rates and the original Berserker potion can become permanent.";
                }
                if(magicIds.Count==0)error="The potion needs at least one supported persistent effect.";
            }
            result.Potions[id]=new(id,skill,level,names?.GetValueOrDefault(skill)??"Skill #"+skill,error.Length==0,error.Length==0?"Exact existing potion effect; no copied stats.":error,magicIds.ToArray());
        }
        return result;
    }
}

public static class InfusionRules
{
    public static List<CraftingIssue> Validate(CraftingCatalog c)
    {
        var issues=new List<CraftingIssue>();void Error(string target,string message)=>issues.Add(new(true,target,message));
        var p=c.Professions;var effects=p.Rows(InfusionSchema.Effects);var offers=p.Rows(InfusionSchema.Offers);var facilities=p.Rows(InfusionSchema.Facility);var products=p.Rows(InfusionSchema.Consumables);var ingredients=p.Rows(InfusionSchema.Ingredients);
        var productIds=products.Select(r=>r.Int("a_item_index")).ToHashSet();
        if(products.Count>64)Error("Permanent potions","At most 64 permanent potion mappings are supported, including retired outputs.");
        if(effects.Count>16||offers.Count>16)Error("Infusions","At most 16 permanent effects and 16 offers, including retired entries, are supported.");
        var skills=new HashSet<int>();var magics=new HashSet<int>();
        foreach(var effect in effects) {
            string target="Infusion effect #"+effect.Int("a_index");int item=effect.Int("a_item_index"),skill=effect.Int("a_skill_index"),level=effect.Int("a_skill_level");
            if(!c.Items.TryGetValue(item,out var data)||!data.Enabled||!data.Stackable||data.Rebirth||data.Type!=6||data.Subtype!=5)Error(target,"Choose an enabled stackable miscellaneous potion (type 6, subtype 5).");
            if(!p.Infusions.Potions.TryGetValue(item,out var potion))Error(target,"The original potion's skill metadata is unavailable. Reload before saving.");
            else {
                if(!potion.Supported)Error(target,potion.Explanation);
                if(potion.SkillId!=skill||potion.SkillLevel!=level)Error(target,"Skill and level must exactly match the original potion; select the potion to derive them.");
                foreach(int magic in potion.MagicIds.Distinct())if(!magics.Add(magic))Error(target,"Permanent effects must not overlap an existing effect's magic IDs.");
            }
            if(!skills.Add(skill))Error(target,"Each permanent effect must use a unique skill ID.");
        }
        foreach(var effect in effects)if(productIds.Contains(effect.Int("a_item_index")))Error("Infusion effect #"+effect.Int("a_index"),"A permanent finished potion cannot be an original source potion.");
        foreach(var product in products) {
            string target="Permanent potion #"+product.Int("a_item_index");
            var effect=effects.FirstOrDefault(e=>e.Int("a_index")==product.Int("a_effect_index"));
            if(effect==null){Error(target,"Choose an existing permanent effect.");continue;}
            int item=product.Int("a_item_index");
            if(!c.Items.TryGetValue(item,out var data)||!data.Enabled||!data.Stackable||data.Rebirth||data.Type!=6||data.Subtype!=5||(data.Flags&25)!=25||(data.Flags&~27L)!=0||p.Infusions.MaxUses.GetValueOrDefault(item,0)!=-1)
                Error(target,"The finished item must be an enabled ordinary stackable potion (type 6, subtype 5), tradable/exchangeable, unlimited-use metadata (-1), and use only Count/Drop/Exchange/Trade flags.");
            if(!p.Infusions.Potions.TryGetValue(item,out var potion)||potion.SkillId!=0||potion.SkillLevel!=0)
                Error(target,"Finished potion must have no ordinary timed skill (item num 0 and num 1 both zero). Its immutable effect mapping supplies the original potion effect.");
        }
        foreach(var offer in offers) {
            string target="Brewing recipe #"+offer.Int("a_index");
            var effect=effects.FirstOrDefault(e=>e.Int("a_index")==offer.Int("a_effect_index"));
            if(effect==null){Error(target,"Choose an existing permanent effect.");continue;}
            int output=offer.Int("a_output_item_index"),source=effect.Int("a_item_index");
            if(!c.Items.TryGetValue(source,out var sourceData)||!sourceData.Enabled||!sourceData.Stackable||sourceData.Rebirth||source==19||productIds.Contains(source)||(sourceData.Flags&((1L<<7)|(1L<<16)|(1L<<18)|(1L<<22)))!=0||p.Infusions.MaxUses.GetValueOrDefault(source,0)!=-1)
                Error(target,"Original source cost must be an ordinary stackable potion with max use -1, without mix, timed, zone-deletion or quest flags.");
            if(output==source)Error(target,"Finished potion must differ from the original source potion.");
            if(!products.Any(r=>r.Int("a_item_index")==output&&r.Int("a_effect_index")==offer.Int("a_effect_index")))Error(target,"Choose a finished potion with a matching permanent-effect mapping.");
            if(!c.Items.TryGetValue(output,out var data)||data.StackLimit<offer.Int("a_output_count"))Error(target,"The finished quantity must fit the output item's stack limit.");
            var extra=ingredients.Where(r=>r.Int("a_offer_index")==offer.Int("a_index")).ToList();
            if(extra.Count>8||extra.Select(r=>r.Int("a_item_index")).Distinct().Count()!=extra.Count)Error(target,"Use at most eight different extra ingredients, with no duplicate items.");
            if(extra.Any(r=>r.Int("a_item_index")==source))Error(target,"Extra ingredients cannot repeat the original potion; edit its original-potion quantity instead.");
        }
        foreach(var ingredient in ingredients) {
            int item=ingredient.Int("a_item_index");string target="Extra ingredient #"+ingredient.Int("a_offer_index")+" / "+ingredient.Int("a_slot");
            if(!offers.Any(r=>r.Int("a_index")==ingredient.Int("a_offer_index")))Error(target,"Choose an existing brewing recipe.");
            if(!c.Items.TryGetValue(item,out var data)||!data.Enabled||!data.Stackable||data.Rebirth||item==19||productIds.Contains(item)||(data.Flags&((1L<<7)|(1L<<16)|(1L<<18)|(1L<<22)))!=0||p.Infusions.MaxUses.GetValueOrDefault(item,0)!=-1)
                Error(target,"Choose an enabled ordinary stackable material with max use -1, excluding currency, permanent potions, mix, timed, zone-deletion and quest items.");
        }
        if(facilities.Count!=1||facilities[0].Int("a_index")!=1)Error("Alchemy station","Exactly one station definition with ID 1 is required.");
        foreach(var facility in facilities) {
            int requiredRank=facility.Int("a_required_artisan_rank");
            if(requiredRank!=0&&!c.Ranks.Any(rank=>rank.Id==requiredRank))
                Error("Alchemy station","Required Artisan Rank must be None or an existing permanent rank ID; retired ranks remain valid.");
            if(facility.Get("a_name").EnumerateRunes().Count()>64)Error("Alchemy station","Station name must contain at most 64 characters.");
            string model=facility.Get("a_model").Replace('\\','/');
            if(!model.StartsWith("Data/",StringComparison.Ordinal)||!model.EndsWith(".smc",StringComparison.OrdinalIgnoreCase)||model.Contains(':')||model.Any(ch=>ch<32||ch>126)||model.Split('/').Any(s=>s.Length==0||s is "." or ".."))Error("Alchemy station","Model must be an ASCII relative Data/ path ending in .smc, without parent traversal.");
        }
        return issues;
    }
}
