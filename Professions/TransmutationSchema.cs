namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Authored crucible settings only. Character points, buildings and paid orders are never content.</summary>
public static class TransmutationSchema
{
    public const string Settings="t_transmutation_settings", Facility="t_transmutation_facility",
        ConstructionCosts="t_transmutation_facility_ingredient", Offers="t_transmutation_offer", ItemRules="t_transmutation_item_rule";
    static ProfessionField Number(string column,string label,int min,int max,string group="General",string help="",int scale=1,int places=0,string? initial=null)
        =>new(column,label,Group:group,Help:help,Min:min,Max:max,Scale:scale,DecimalPlaces:places,Default:initial??min.ToString(System.Globalization.CultureInfo.InvariantCulture));
    static ProfessionField Id(int maximum=int.MaxValue)=>new("a_index","Permanent ID",Min:1,Max:maximum,Identity:true,Default:"1");
    public static readonly ProfessionTable[] Tables=[
        new(Settings,"Point reservoir and equipment value","Each character owns a separate reservoir. Equipment is destroyed immediately for points; queued orders spend those points immediately. Capacity limits unspent points, not paid work or finished products.",["a_index"],[
            Id(1),Number("a_capacity","Point capacity",1,1000000000,"Reservoir","If a conversion would overflow the reservoir, the entire conversion is refused. Lowering capacity never deletes points already stored.",initial:"10000"),
            Number("a_points_numerator","Points multiplier: numerator",1,1000000,"Equipment value","Points = floor(base equipment level × numerator / denominator), with a minimum of one point. Rarity, enhancement and reduced wearing requirements do not increase value.",initial:"1"),
            Number("a_points_denominator","Points multiplier: denominator",1,1000000,"Equipment value",initial:"1")],"Transmutation"),
        new(Offers,"Transmutation outputs","Queue any affordable quantity. One machine processes products sequentially, including while offline. Ready products do not block later work and remain until collected. Already-paid batches keep their original item, price and time.",["a_index"],[
            Id(),new("a_output_item_index","Upgrading item",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"85"),
            Number("a_output_count","Items per transmutation",1,1000000,"Production","One completed operation grants this many items. It must fit the item's stack limit.",initial:"1"),
            Number("a_point_cost","Points per transmutation",1,1000000000,"Production","Deducted when the order enters the queue, not when it completes.",initial:"1000"),
            new("a_duration_sec","Time per transmutation",FieldKind.Duration,"Production","Real elapsed time per operation. A quantity of five takes five times this duration; leaving the hall and logging out do not stop it.",Min:1,Max:604800,Default:"120"),
            new("a_enabled","Available for new orders",FieldKind.Boolean,Min:0,Max:1,Default:"1"),
            Number("a_sort","Display order",0,1000000)],"Transmutation"),
        new(ItemRules,"Equipment exceptions","Optional exceptions to the level-based formula. Exclude an enabled item, or give it a fixed point value. Without an entry, normal equipment uses the formula. Disabled item prototypes are not loaded by the game and cannot be referenced. An exception can never bypass equipment type, ownership or safety restrictions.",["a_item_index"],[
            new("a_item_index","Equipment item",FieldKind.Item,Min:1,Max:int.MaxValue,Identity:true,Default:"1"),
            new("a_enabled","Allow conversion",FieldKind.Boolean,Help:"No excludes this item. Yes still requires ordinary safe equipment; it does not allow tools, costumes or restricted items.",Min:0,Max:1,Default:"1"),
            Number("a_points_override","Fixed point value (0 = formula)",0,1000000000,help:"Zero uses the configured level formula. A positive value replaces that formula for this equipment item.")],"Transmutation"),
        new(Facility,"Transmutation Crucible","A separate permanent workshop in private hall template 1 (zone 8). Rank and material requirements apply once, when building. Existing ownership and paid work survive later content changes.",["a_index"],[
            Id(1),new("a_enabled","Conversion and new orders enabled",FieldKind.Boolean,"Availability","Disabling new activity preserves stored points, completed buildings and paid orders.",Min:0,Max:1,Default:"1"),
            new("a_construction_enabled","Construction enabled",FieldKind.Boolean,"Availability","Only controls future construction. Existing buildings are retained.",Min:0,Max:1,Default:"1"),
            new("a_required_artisan_rank","Artisan Rank needed to build",FieldKind.ArtisanRank,"Availability","One-time construction permission. Stored as a permanent rank ID, independently of display order. None permits construction at any rank.",Min:0,Max:int.MaxValue,ReferenceTable:"t_crafting_master_rank",Default:"2"),
            new("a_name","Facility name",FieldKind.Text,Help:"At most 64 characters (256 UTF-8 bytes).",Max:256,Default:"Transmutation Crucible"),
            new("a_model","Model path (.smc)",FieldKind.Text,Help:"Relative ASCII path below Data/. New artwork must be deployed to the client.",Max:240,Default:@"Data\World\Professions\Transmutation\transmutation_crucible.smc"),
            new("a_plot_name","Unbuilt plot name",FieldKind.Text,"Unbuilt plot","At most 64 characters (256 UTF-8 bytes).",Max:256,Default:"Transmutation plot"),
            new("a_plot_model","Unbuilt plot model (.smc)",FieldKind.Text,"Unbuilt plot","Relative ASCII path below Data/. Separate artwork shown before construction.",Max:240,Default:@"Data\World\Professions\Transmutation\transmutation_plot.smc"),
            Number("a_x","X (metres)",0,10000000,"Placement",scale:100,places:2,initial:"3200"),
            Number("a_y","Height (metres)",-1000000,1000000,"Placement",scale:100,places:2,initial:"0"),
            Number("a_z","Z (metres)",0,10000000,"Placement",scale:100,places:2,initial:"6300"),
            Number("a_heading","Heading (degrees)",0,35999,"Placement",scale:100,places:2,initial:"9000"),
            Number("a_use_radius","Use distance (metres)",100,1000,"Placement",scale:100,places:2,initial:"400")],"Transmutation"),
        new(ConstructionCosts,"Crucible construction costs","Materials consumed once to build the Crucible permanently. Use up to eight different ordinary materials. An empty list permits free construction; saved buildings are never removed by cost edits.",["a_facility_index","a_slot"],[
            new("a_facility_index","Crucible",FieldKind.Parent,Min:1,Max:1,ReferenceTable:Facility,Default:"1"),
            Number("a_slot","Order (zero based)",0,7),new("a_item_index","Material",FieldKind.Item,Min:1,Max:int.MaxValue,Default:"156"),
            Number("a_count","Quantity",1,1000000,initial:"1")],"Transmutation")
    ];
    public static bool IsTable(string table)=>Tables.Any(t=>t.Name==table);
}
