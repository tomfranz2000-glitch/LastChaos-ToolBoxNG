namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Authored display and construction settings only. Stored equipment and ownership are never content.</summary>
public static class AccessoryDisplaySchema
{
    public const string Facility="t_accessory_display_facility", ConstructionCosts="t_accessory_display_facility_ingredient";
    static ProfessionField Number(string column,string label,int min,int max,string group="General",string help="",int scale=1,int places=0,string? initial=null)
        =>new(column,label,Group:group,Help:help,Min:min,Max:max,Scale:scale,DecimalPlaces:places,Default:initial??min.ToString(System.Globalization.CultureInfo.InvariantCulture));
    static ProfessionField Id(int maximum=int.MaxValue)=>new("a_index","Permanent ID",Min:1,Max:maximum,Identity:true,Default:"1");
    public static readonly ProfessionTable[] Tables=[
        new(Facility,"Accessory Display","A separate permanent workshop in private hall template 1 (zone 8). Rank and material requirements apply once, when building. Depositing one equippable accessory grants its full equipment stats everywhere until retrieved. Stored items and ownership survive later content changes.",["a_index"],[
            Id(1),new("a_enabled","New deposits enabled",FieldKind.Boolean,"Availability","Disabling new deposits preserves the building and stored accessory; retrieval remains possible.",Min:0,Max:1,Default:"1"),
            new("a_construction_enabled","Construction enabled",FieldKind.Boolean,"Availability","Only controls future construction. Existing buildings are retained.",Min:0,Max:1,Default:"1"),
            new("a_required_artisan_rank","Artisan Rank needed to build",FieldKind.ArtisanRank,"Availability","One-time construction permission. Stored as a permanent rank ID, independently of display order. None permits construction at any rank.",Min:0,Max:int.MaxValue,ReferenceTable:"t_crafting_master_rank",Default:"4"),
            new("a_name","Facility name",FieldKind.Text,Help:"At most 64 characters (256 UTF-8 bytes).",Max:256,Default:"Accessory Display"),
            new("a_model","Model path (.smc)",FieldKind.Text,Help:"Relative ASCII path below Data/. New artwork must be deployed to the client.",Max:240,Default:@"Data\World\Professions\AccessoryDisplay\accessory_display.smc"),
            new("a_plot_name","Unbuilt plot name",FieldKind.Text,"Unbuilt plot","At most 64 characters (256 UTF-8 bytes).",Max:256,Default:"Accessory Display plot"),
            new("a_plot_model","Unbuilt plot model (.smc)",FieldKind.Text,"Unbuilt plot","Relative ASCII path below Data/. Separate artwork shown before construction.",Max:240,Default:@"Data\World\Professions\AccessoryDisplay\accessory_display_plot.smc"),
            Number("a_x","X (metres)",0,10000000,"Placement",scale:100,places:2,initial:"4000"),
            Number("a_y","Height (metres)",-1000000,1000000,"Placement",scale:100,places:2,initial:"0"),
            Number("a_z","Z (metres)",0,10000000,"Placement",scale:100,places:2,initial:"6800"),
            Number("a_heading","Heading (degrees)",0,35999,"Placement",scale:100,places:2,initial:"0"),
            Number("a_use_radius","Use distance (metres)",100,1000,"Placement",scale:100,places:2,initial:"400")],"Accessory Display"),
        new(ConstructionCosts,"Accessory Display construction costs","Materials consumed once to build the Accessory Display permanently. Use up to eight different ordinary materials. An empty list permits free construction; saved buildings are never removed by cost edits.",["a_facility_index","a_slot"],[
            new("a_facility_index","Accessory Display",FieldKind.Parent,Min:1,Max:1,ReferenceTable:Facility,Default:"1"),
            Number("a_slot","Order (zero based)",0,7),new("a_item_index","Material",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"156"),
            Number("a_count","Quantity",1,1000000,initial:"1")],"Accessory Display")
    ];
    public static bool IsTable(string table)=>Tables.Any(t=>t.Name==table);
}
