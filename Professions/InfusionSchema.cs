namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Authored station content; acquired character effects never belong here.</summary>
public static class InfusionSchema
{
    public const string Effects = "t_alchemy_effect", Offers = "t_alchemy_offer", Facility = "t_alchemy_facility", Consumables = "t_alchemy_consumable", Ingredients = "t_alchemy_ingredient", ConstructionCosts = "t_alchemy_facility_ingredient";
    static ProfessionField Number(string column,string label,int min,int max,string group="General",string help="",int scale=1,int places=0,string? initial=null)
        => new(column,label,Group:group,Help:help,Min:min,Max:max,Scale:scale,DecimalPlaces:places,Default:initial??min.ToString(System.Globalization.CultureInfo.InvariantCulture));
    static ProfessionField Id(int max=int.MaxValue) => new("a_index","Permanent ID",Min:1,Max:max,Identity:true,Default:"1");
    static ProfessionField Enabled(string label="Available for new brews") => new("a_enabled",label,FieldKind.Boolean,Min:0,Max:1,Default:"1");
    public static readonly ProfessionTable[] Tables = [
        new(Effects,"Permanent infusion effects","An effect preserves the exact existing potion skill and level. Published identity fields cannot change: acquired effects belong to characters forever. Disable its offers to stop new purchases.",["a_index"],[
            Id(),new("a_item_index","Original potion",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"1"),
            Number("a_skill_index","Exact potion skill ID",1,int.MaxValue,help:"Must match the original potion's supported beneficial skill; copied stat bonuses are not used."),
            Number("a_skill_level","Exact skill level",1,127,help:"The client/server legacy skill-level field supports levels 1–127, within the original skill's maximum.")],"Infusions"),
        new(Consumables,"Permanent potions","A tradable finished potion grants its mapped exact permanent effect when consumed. Published item/effect mappings cannot be removed or changed, including when an offer is retired.",["a_item_index"],[
            new("a_item_index","Finished potion",FieldKind.Item,Min:1,Max:int.MaxValue,Identity:true,Default:"1"),
            new("a_effect_index","Permanent effect",FieldKind.Parent,Min:1,Max:int.MaxValue,ReferenceTable:Effects,Default:"1")],"Infusions"),
        new(Offers,"Brewing recipes","Consume the original potions and any extra ingredients to start an offline brew. Finished potions are tradable; knowing the effect does not prevent brewing. Saved jobs keep the terms accepted at their start.",["a_index"],[
            Id(),new("a_effect_index","Permanent effect",FieldKind.Parent,Min:1,Max:int.MaxValue,ReferenceTable:Effects,Default:"1"),
            Number("a_required_count","Original potions consumed",1,1000000,help:"Consumed together with extra ingredients when brewing starts.",initial:"250"),
            new("a_output_item_index","Finished potion",FieldKind.Item,"Production","Must have an immutable permanent-effect mapping matching this recipe.",Min:1,Max:int.MaxValue,Default:"1"),
            Number("a_output_count","Finished quantity",1,1000000,"Production",initial:"1"),
            new("a_duration_sec","Brewing time",FieldKind.Duration,"Production","Real elapsed time, including while offline. Maximum seven days. This does not limit the permanent buff duration.",Min:1,Max:604800,Default:"21600"),
            Enabled(),Number("a_sort","Display order",0,1000000)],"Infusions"),
        new(Ingredients,"Extra ingredients","Optional costs in addition to the original potions. Each recipe supports up to eight different ordinary materials. Permanent potions and currency cannot be ingredients.",["a_offer_index","a_slot"],[
            new("a_offer_index","Brewing recipe",FieldKind.Parent,Min:1,Max:int.MaxValue,ReferenceTable:Offers,Default:"1"),
            Number("a_slot","Order (zero based)",0,7),new("a_item_index","Ingredient",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"1"),
            Number("a_count","Quantity",1,1000000,initial:"1")],"Infusions"),
        new(Facility,"Alchemy station","Build one permanent station per character in private hall template 1 (zone 8). Rank and costs apply only when building. Existing ownership remains saved when these settings change.",["a_index"],[
            Id(1),Enabled("Brewing enabled") with {Group="Availability",Help="Allows new brews at completed stations. Disabling this does not remove built stations or paid brewing jobs."},
            new("a_construction_enabled","Construction enabled",FieldKind.Boolean,"Availability","Allows new stations to be built. Turning this off does not remove stations already built.",Min:0,Max:1,Default:"1"),
            new("a_required_artisan_rank","Artisan Rank needed to build",FieldKind.ArtisanRank,"Availability",
                "One-time build permission, not a continuing brewing requirement. Select None to allow construction at any rank. Changing this does not revoke built stations. The permanent rank ID is stored independently of display order.",
                Min:0,Max:int.MaxValue,ReferenceTable:"t_crafting_master_rank",Default:"3"),new("a_name","Station name",FieldKind.Text,Help:"At most 64 characters (256 UTF-8 bytes).",Max:256,Default:"Alchemy station"),
            new("a_model","Model path (.smc)",FieldKind.Text,Help:"ASCII path below Data/, for example Data\\World\\Professions\\Alchemy\\alchemy_station.smc. No absolute paths or parent traversal.",Max:240,Default:@"Data\World\Professions\Alchemy\alchemy_station.smc"),
            new("a_plot_name","Unbuilt plot name",FieldKind.Text,"Unbuilt plot","At most 64 characters (256 UTF-8 bytes). Shown before the character builds the station.",Max:256,Default:"Alchemy plot"),
            new("a_plot_model","Unbuilt plot model (.smc)",FieldKind.Text,"Unbuilt plot","ASCII model path below Data/. This artwork is separate from the built station model and must be deployed to clients.",Max:240,Default:@"Data\World\Professions\Alchemy\alchemy_plot.smc"),
            Number("a_x","X (metres)",0,10000000,"Placement",scale:100,places:2,initial:"4400"),
            Number("a_y","Height (metres)",-1000000,1000000,"Placement",scale:100,places:2,initial:"0"),
            Number("a_z","Z (metres)",0,10000000,"Placement",scale:100,places:2,initial:"6300"),
            Number("a_heading","Heading (degrees)",0,35999,"Placement",scale:100,places:2,initial:"9000"),
            Number("a_use_radius","Use distance (metres)",100,1000,"Placement",scale:100,places:2,initial:"400")],"Infusions"),
        new(ConstructionCosts,"Construction costs","Materials consumed once to permanently build the station. Up to eight different ordinary materials; an empty list allows a free build. Changes affect future construction only and never remove built stations.",["a_facility_index","a_slot"],[
            new("a_facility_index","Station",FieldKind.Parent,Min:1,Max:int.MaxValue,ReferenceTable:Facility,Default:"1"),
            Number("a_slot","Order (zero based)",0,7),new("a_item_index","Material",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"1"),
            Number("a_count","Quantity",1,1000000,initial:"1")],"Infusions")
    ];
    public static bool IsTable(string table) => table is Effects or Offers or Facility or Consumables or Ingredients or ConstructionCosts;
}
