namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Authored station content; acquired character effects never belong here.</summary>
public static class InfusionSchema
{
    public const string Effects = "t_alchemy_effect", Offers = "t_alchemy_offer", Facility = "t_alchemy_facility";
    static ProfessionField Number(string column,string label,int min,int max,string group="General",string help="",int scale=1,int places=0,string? initial=null)
        => new(column,label,Group:group,Help:help,Min:min,Max:max,Scale:scale,DecimalPlaces:places,Default:initial??min.ToString(System.Globalization.CultureInfo.InvariantCulture));
    static ProfessionField Id(int max=int.MaxValue) => new("a_index","Permanent ID",Min:1,Max:max,Identity:true,Default:"1");
    static ProfessionField Enabled(string label="Available to new purchases") => new("a_enabled",label,FieldKind.Boolean,Min:0,Max:1,Default:"1");
    public static readonly ProfessionTable[] Tables = [
        new(Effects,"Permanent infusion effects","An effect preserves the exact existing potion skill and level. Published identity fields cannot change: acquired effects belong to characters forever. Disable its offers to stop new purchases.",["a_index"],[
            Id(),new("a_item_index","Original potion",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"1"),
            Number("a_skill_index","Exact potion skill ID",1,int.MaxValue,help:"Must match the original potion's supported beneficial skill; copied stat bonuses are not used."),
            Number("a_skill_level","Exact skill level",1,127,help:"The client/server legacy skill-level field supports levels 1–127, within the original skill's maximum.")],"Infusions"),
        new(Offers,"Infusion offers","Exchange a quantity of one potion for its exact permanent effect. Retire published offers instead of removing them; changing an offer does not revoke earlier unlocks.",["a_index"],[
            Id(),new("a_effect_index","Permanent effect",FieldKind.Parent,Min:1,Max:int.MaxValue,ReferenceTable:Effects,Default:"1"),
            Number("a_required_count","Potions consumed",1,1000000,help:"All are consumed together only after the server confirms the purchase.",initial:"250"),
            Enabled(),Number("a_sort","Display order",0,1000000)],"Infusions"),
        new(Facility,"Alchemy station","One station in private hall template 1 (zone 8). Placement is relative to that native world; the server checks its map bounds. Model artwork must be deployed separately.",["a_index"],[
            Id(1),Enabled("Station enabled"),new("a_name","Station name",FieldKind.Text,Help:"At most 64 characters (256 UTF-8 bytes).",Max:256,Default:"Alchemy station"),
            new("a_model","Model path (.smc)",FieldKind.Text,Help:"ASCII path below Data/, for example Data\\World\\Professions\\Alchemy\\alchemy_station.smc. No absolute paths or parent traversal.",Max:240,Default:@"Data\World\Professions\Alchemy\alchemy_station.smc"),
            Number("a_x","X (metres)",0,10000000,"Placement",scale:100,places:2,initial:"4400"),
            Number("a_y","Height (metres)",-1000000,1000000,"Placement",scale:100,places:2,initial:"0"),
            Number("a_z","Z (metres)",0,10000000,"Placement",scale:100,places:2,initial:"6300"),
            Number("a_heading","Heading (degrees)",0,35999,"Placement",scale:100,places:2,initial:"9000"),
            Number("a_use_radius","Use distance (metres)",100,1000,"Placement",scale:100,places:2,initial:"400")],"Infusions")
    ];
    public static bool IsTable(string table) => table is Effects or Offers or Facility;
}
