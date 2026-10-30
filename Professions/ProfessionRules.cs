using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG.Professions;

// Authored catalogue rules mirror the coordinated game loaders. Runtime terrain
// validation is separate: the editor cannot promise water/walkability from SQL.
public static class ProfessionRules
{
    public static List<CraftingIssue> Validate(CraftingCatalog c)
    {
        var issues=new List<CraftingIssue>();var p=c.Professions;
        void Error(string target,string text)=>issues.Add(new(true,target,text));
        void Note(string target,string text)=>issues.Add(new(false,target,text));
        foreach(var unknown in p.Tables.Keys.Where(n=>!ProfessionSchema.Tables.Any(t=>t.Name==n)))Error(unknown,"Unknown content table.");
        foreach(var table in ProfessionSchema.Tables) {
            var keys=new HashSet<string>();
            if(!p.Tables.ContainsKey(table.Name)){Error(table.Title,"Missing profession catalogue snapshot. Reload the editor.");continue;}
            foreach(var row in p.Rows(table.Name)) {
                string target=table.Title+" "+row.Key(table);
                foreach(var extra in row.Values.Keys.Where(column=>!table.Fields.Any(f=>f.Column==column)))Error(target,"Unknown column "+extra);
                foreach(var field in table.Fields) {
                    string value=row.Get(field.Column);
                    if(!row.Values.ContainsKey(field.Column)){Error(target,"Missing "+field.Label);continue;}
                    if(field.Kind is FieldKind.Text or FieldKind.Geometry) {
                        try {if(new UTF8Encoding(false,true).GetByteCount(value)>field.Max || (field.Kind==FieldKind.Text&&(value.Length==0||value.Any(ch=>char.IsControl(ch)||ch is '\u2028' or '\u2029'))))Error(target,field.Label+" is empty, too long or contains invalid/control text.");}
                        catch(EncoderFallbackException){Error(target,field.Label+" has invalid UTF-8 text.");}
                    } else if(!decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||n<field.Min||n>field.Max||(field.Kind!=FieldKind.Decimal&&n!=decimal.Truncate(n)))Error(target,field.Label+" is outside its supported stored range "+field.Min+"–"+field.Max+".");
                }
                if(!keys.Add(row.Key(table)))Error(target,"Duplicate permanent/composite key.");
            }
        }
        if(issues.Any(i=>i.Error))return issues;
        List<ProfessionRow> Rows(string table)=>p.Rows(table);
        List<ProfessionRow> Active(string table)=>Rows(table).Where(r=>r.Enabled).ToList();
        ProfessionRow? Find(string table,int id)=>Rows(table).FirstOrDefault(r=>r.Int("a_index")==id);
        bool Material(int item,int profession)=>c.Gathering.Any(r=>r.ItemId==item&&r.Profession==profession);
        void Resource(string target,int item,long count,int profession) {
            if(!Material(item,profession))Error(target,$"Item {item} needs a matching {Name(profession)} resource mastery definition.");
            if(!c.Items.TryGetValue(item,out var data)||!data.Enabled||!data.Stackable||data.Rebirth||item==19||count>data.StackLimit)Error(target,$"Reward item {item} must be enabled, stackable, not gold/rebirth, and support the maximum quantity {count}.");
        }
        void Tool(string target,int item,int subtype) {
            if(!c.Items.TryGetValue(item,out var data)||!data.Enabled||data.Type!=0||data.Subtype!=subtype)Error(target,$"Tool {item} must be an enabled weapon of gathering subtype {subtype}.");
        }
        void Npc(string target,int id,long flags) {
            if(!p.Npcs.TryGetValue(id,out var data)||!data.Enabled||(data.Flags&flags)!=flags)Error(target,$"NPC {id} is missing, disabled or has the wrong source flags. Open NPC Editor.");
        }
        void Zone(string target,int id) {if(!p.Zones.ContainsKey(id))Error(target,$"Zone {id} does not exist.");}
        if(!Rows("t_gathering_settings").Select(r=>r.Int("a_index")).Order().SequenceEqual(new[]{1,2,4}))Error("Gathering settings","Exactly Mining (1), Herbalism (2) and Wave Absorption (4) settings rows are required.");
        if(Rows("t_fishing_settings").Count!=1||Rows("t_fishing_settings")[0].Int("a_index")!=1)Error("Fishing settings","Exactly one settings row with ID 1 is required.");
        var settings=Rows("t_gathering_settings").ToDictionary(r=>r.Int("a_index"));
        if(issues.Any(i=>i.Error))return issues;
        var fish=Rows("t_fishing_settings")[0];int rod=fish.Int("a_rod_item_index");
        if(!c.Items.TryGetValue(rod,out var rodItem)||!rodItem.Enabled||rodItem.Rebirth||rodItem.Type!=4||rodItem.Wearing!=-1||rod==19||c.Gathering.Any(m=>m.ItemId==rod&&m.Profession==3))Error("Fishing settings","Carried rod must be an enabled, unequippable miscellaneous item, not rebirth, not gold or a mastered material.");
        var nodes=Active("t_gathering_node");var tools=Active("t_gathering_tool");var waters=Active("t_fishing_water");var pools=Active("t_herb_population");
        if(nodes.Count>16)Error("Nodes","At most 16 enabled Mining/Wave nodes fit the shared game catalogue.");
        if(tools.Count>64)Error("Tools","At most 64 enabled Mining/Wave tools are supported.");
        if(waters.Count>128)Error("Fishing","At most 128 enabled waters are supported.");
        if(pools.Count>64)Error("Herbalism","At most 64 enabled populations are supported.");
        foreach(var duplicate in Rows("t_gathering_node").GroupBy(r=>r.Int("a_npc_index")).Where(g=>g.Count()>1))Error("Nodes","NPC source binding must be unique: "+duplicate.Key);
        foreach(var duplicate in Rows("t_herb_population").GroupBy(r=>(r.Int("a_zone_index"),r.Int("a_npc_index"))).Where(g=>g.Count()>1))Error("Herbalism","Population zone/NPC binding must be unique.");
        foreach(var table in ProfessionSchema.Tables) foreach(var row in Rows(table.Name)) foreach(var field in table.Fields.Where(f=>f.Kind==FieldKind.Parent))
            if(Find(field.ReferenceTable,row.Int(field.Column)) is null)Error(table.Title+" "+row.Key(table),"Missing parent "+field.ReferenceTable+" #"+row.Int(field.Column));
        foreach(var tool in Rows("t_gathering_tool")) {
            int profession=tool.Int("a_profession");if(profession is not(1 or 4))Error("Tools","Only Mining (1) or Wave (4) tools are supported.");
            if(tool.Enabled)Tool("Tool "+tool.Int("a_item_index"),tool.Int("a_item_index"),profession==4?10:8);
        }
        foreach(var profile in Rows("t_gathering_profile"))if(ValidateProfile(profile.Get("a_geometry_hex")) is string error)Error("Profile "+profile.Int("a_index"),error);
        var usedProfiles=nodes.Where(r=>r.Int("a_profession")==1).Select(r=>r.Int("a_interaction_profile")).Distinct().ToList();
        if(usedProfiles.Count>4)Error("Mining","At most four imported interaction profiles can be used at once.");
        foreach(var node in Rows("t_gathering_node")) {
            string target="Node "+node.Int("a_index");int profession=node.Int("a_profession");
            if(profession is not(1 or 4)){Error(target,"Only Mining (1) or Wave Absorption (4) nodes are supported.");continue;}
            var rule=settings[profession];int id=node.Int("a_index");
            if(node.Int("a_passive_tick_ms")>node.Int("a_passive_duration_ms"))Error(target,"Passive tick interval cannot exceed session duration.");
            if(node.Int("a_hit_delay_ms")!=0)Error(target,"Reserved strike delay must remain zero.");
            if(profession==4) {
                if(node.Int("a_interaction_profile")!=0)Error(target,"Wave rhythm requires profile zero.");
                if(ProfessionPreviews.GenerateWave(node).Count!=node.Int("a_rhythm_beats"))Error(target,"Wave timing, hold and grading settings cannot generate a valid complete schedule.");
            } else if(Find("t_gathering_profile",node.Int("a_interaction_profile")) is null)Error(target,"Mining requires an imported interaction profile.");
            if(!node.Enabled)continue;
            Npc(target,node.Int("a_npc_index"),1L<<(profession==4?12:10));
            if(node.Int("a_skill_chance_bp")<rule.Int("a_min_chance_bp"))Error(target,"Base chance cannot be below the profession minimum chance.");
            if(node.Int("a_required_skill")>rule.Int("a_skill_cap"))Note(target,"Required skill exceeds the configured cap.");
            long maxTool=tools.Where(t=>t.Int("a_profession")==profession).Select(t=>(long)t.Int("a_yield_bonus_bp")).DefaultIfEmpty().Max();
            long maxActive=((long)node.Int("a_count")*(10000+rule.Int("a_max_yield_bp")+maxTool)+9999)/10000;
            long maxPassive=((long)node.Int("a_passive_count")*(10000+maxTool)+9999)/10000;
            Resource(target,node.Int("a_item_index"),Math.Max(maxActive,maxPassive),profession);
            var loot=Active("t_gathering_node_loot").Where(l=>l.Int("a_node_index")==id).ToList();
            ValidateLoot(loot,target,profession==4);
            foreach(var row in loot) {
                long baseCount=row.Int("a_item_count");
                if(baseCount*node.Int("a_passive_count")>1000||(profession==4&&baseCount*node.Int("a_count")>1000))Error(target,"Weighted base payout exceeds 1000 items.");
                long quantity=((long)node.Int("a_passive_count")*baseCount*(10000+maxTool)+9999)/10000;
                if(profession==4)quantity=Math.Max(quantity,((long)node.Int("a_count")*baseCount*(10000+rule.Int("a_max_yield_bp")+maxTool)*2+9999)/10000);
                Resource(target,row.Int("a_item_index"),quantity,profession);
            }
        }
        int sourceCount=0;var positions=new HashSet<(int,int,float,float)>();int activeSites=0;
        foreach(var pool in Rows("t_herb_population")) {
            string target="Herb population "+pool.Int("a_index");int id=pool.Int("a_index");
            if(pool.Int("a_respawn_max_sec")<pool.Int("a_respawn_min_sec")||pool.Int("a_initial_alive")<pool.Int("a_min_alive"))Error(target,"Respawn range and initial/minimum population are inconsistent.");
            var sites=Active("t_herb_spawn_point").Where(r=>r.Int("a_population_index")==id).ToList();
            if(pool.Enabled) {
                Zone(target,pool.Int("a_zone_index"));Npc(target,pool.Int("a_npc_index"),(1L<<11)|(1L<<4));Tool(target,pool.Int("a_visual_tool_index"),9);
                if(sites.Count==0||pool.Int("a_min_alive")>sites.Count||pool.Int("a_initial_alive")>sites.Count)Error(target,"Minimum/initial population exceeds enabled candidate site capacity.");
                if(pool.Int("a_skill_chance_bp")<settings[2].Int("a_min_chance_bp"))Error(target,"Base chance is below the Herbalism minimum.");
                activeSites+=sites.Count;
                foreach(var point in sites)if(!positions.Add((pool.Int("a_zone_index"),point.Int("a_y_layer"),(float)point.Decimal("a_pos_x"),(float)point.Decimal("a_pos_z"))))Error(target,"An enabled site overlaps another population's site in the same zone/layer.");
                var loot=Active("t_herb_loot").Where(l=>l.Int("a_population_index")==id).ToList();ValidateLoot(loot,target,true);
                foreach(var row in loot)Resource(target,row.Int("a_item_index"),((long)row.Int("a_item_count")*(10000+settings[2].Int("a_max_yield_bp"))+9999)/10000,2);
                if(sites.Count>0)sourceCount+=loot.Count;
            }
        }
        foreach(var duplicate in Rows("t_herb_spawn_point").GroupBy(r=>(r.Int("a_population_index"),r.Int("a_y_layer"),r.Decimal("a_pos_x"),r.Decimal("a_pos_z"))).Where(g=>g.Count()>1))Error("Herb sites","Coordinates must be unique within a population, including disabled candidates.");
        foreach(var site in Rows("t_herb_spawn_point"))if(site.Int("a_y_layer")%2!=0)Error("Herb site "+site.Int("a_index"),"Only even terrain Y layers are supported.");
        if(activeSites>4096)Error("Herbalism","At most 4096 enabled candidate sites are supported.");
        if(sourceCount>64)Error("Herbalism","The /gather catalogue supports at most 64 enabled population/outcome source rows.");
        int allVertices=0,fishBytes=24+c.Gathering.Where(m=>m.Profession==3).Sum(m=>36+m.Bonuses.Count*12);
        foreach(var water in Rows("t_fishing_water")) {
            string target="Water "+water.Int("a_index");int id=water.Int("a_index");
            if(water.Int("a_y_layer")%2!=0)Error(target,"Fishing requires an even terrain layer.");
            if(water.Decimal("a_cast_min")>water.Decimal("a_cast_max")||water.Int("a_bite_min_ms")>water.Int("a_bite_max_ms"))Error(target,"Cast/bite minimum cannot exceed maximum.");
            var polygons=Rows("t_fishing_water_vertex").Where(v=>v.Int("a_water_index")==id).GroupBy(v=>v.Int("a_polygon_index")).ToList();
            foreach(var polygon in polygons) {
                var vertices=polygon.OrderBy(v=>v.Int("a_vertex_index")).ToList();
                if(!vertices.Select(v=>v.Int("a_vertex_index")).SequenceEqual(Enumerable.Range(0,vertices.Count)))Error(target,"Vertex order must be contiguous from zero within each polygon.");
                if(ValidatePolygon(vertices.Select(v=>(v.Decimal("a_pos_x"),v.Decimal("a_pos_z")))) is string error)Error(target,error);
                if(water.Enabled)allVertices+=vertices.Count;
            }
            if(!water.Enabled)continue;
            Zone(target,water.Int("a_zone_index"));
            if(polygons.Count is <1 or >32)Error(target,"An enabled water needs 1–32 valid polygon pieces.");
            if(water.Int("a_skill_chance_bp")<fish.Int("a_min_chance_bp"))Error(target,"Base chance is below the Fishing minimum.");
            var loot=Active("t_fishing_loot").Where(l=>l.Int("a_water_index")==id).ToList();ValidateLoot(loot,target,true);
            foreach(var row in loot)Resource(target,row.Int("a_item_index"),row.Int("a_item_count"),3);
            fishBytes+=56+Encoding.UTF8.GetByteCount(water.Get("a_name"))+polygons.Sum(g=>4+g.Count()*8)+loot.Count*12;
        }
        if(allVertices>8192)Error("Fishing","At most 8192 active boundary vertices are supported.");
        if(fishBytes>256*1024)Error("Fishing","Fishing content exceeds the 256 KiB catalogue limit.");
        foreach(var tableName in new[]{"t_herb_loot","t_gathering_node_loot","t_fishing_loot"}) {
            string owner=ProfessionSchema.Find(tableName).Keys[0];
            foreach(var group in Rows(tableName).GroupBy(r=>r.Int(owner)))if(group.Select(r=>r.Int("a_item_index")).Distinct().Count()!=group.Count())Error(tableName,"A source cannot repeat the same item, including disabled rows.");
        }
        if(activeSites>0)Note("Herbalism","SQL validation does not establish site accessibility. Check terrain/walkability and peace areas on the map; the server validates the deployed world again.");
        if(waters.Count>0)Note("Fishing","Boundaries mark existing water; they do not create water or prove shore reachability. Check the deployed world and use the map overlay.");
        issues.AddRange(InfusionRules.Validate(c));
        return issues;
        void ValidateLoot(List<ProfessionRow> rows,string target,bool required) {
            if(rows.Count>32||(required&&rows.Count==0))Error(target,"Use 1–32 enabled weighted outcomes.");
            if(rows.Sum(l=>(long)l.Int("a_weight"))>32000000)Error(target,"Total loot weight exceeds 32000000.");
        }
    }
    public static string Name(int profession)=>profession switch{1=>"Mining",2=>"Herbalism",3=>"Fishing",4=>"Wave Absorption",_=>"Unknown"};
    private static decimal ServerCm(decimal value) => decimal.Round(decimal.Parse(((float)value).ToString("G6",CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture)*100,0,MidpointRounding.AwayFromZero);
    public static string? ValidatePolygon(IEnumerable<(decimal X,decimal Z)> coordinates)
    {
        var points=coordinates.Select(p=>(X:ServerCm(p.X),Z:ServerCm(p.Z))).ToList();
        if(points.Count is <3 or >64)return"A polygon needs 3–64 vertices.";
        if(points.Any(p=>p.X<0||p.Z<0||p.X>10000000||p.Z>10000000))return"Water coordinates exceed the native 0–100000 metre range.";
        if(points.Distinct().Count()!=points.Count)return"Polygon vertices duplicate after server centimetre quantization.";
        decimal Cross((decimal X,decimal Z)a,(decimal X,decimal Z)b,(decimal X,decimal Z)d)=>(b.X-a.X)*(d.Z-a.Z)-(b.Z-a.Z)*(d.X-a.X);
        bool On((decimal X,decimal Z)a,(decimal X,decimal Z)b,(decimal X,decimal Z)d)=>Cross(a,b,d)==0&&d.X>=Math.Min(a.X,b.X)&&d.X<=Math.Max(a.X,b.X)&&d.Z>=Math.Min(a.Z,b.Z)&&d.Z<=Math.Max(a.Z,b.Z);
        bool Hit((decimal X,decimal Z)a,(decimal X,decimal Z)b,(decimal X,decimal Z)d,(decimal X,decimal Z)e) {var x=Cross(a,b,d);var y=Cross(a,b,e);var z=Cross(d,e,a);var w=Cross(d,e,b);return(x<0&&y>0||x>0&&y<0)&&(z<0&&w>0||z>0&&w<0)||x==0&&On(a,b,d)||y==0&&On(a,b,e)||z==0&&On(d,e,a)||w==0&&On(d,e,b);}
        decimal area=0;
        for(int i=0;i<points.Count;i++) {var a=points[i];var b=points[(i+1)%points.Count];area+=a.X*b.Z-b.X*a.Z;for(int j=i+1;j<points.Count;j++){if(j==i+1||i==0&&j==points.Count-1)continue;if(Hit(a,b,points[j],points[(j+1)%points.Count]))return"Water polygon self-intersects or touches a nonadjacent edge.";}}
        return area==0?"Water polygon is degenerate (zero area).":null;
    }
    readonly record struct V(double X,double Y,double Z)
    {
        public static V operator+(V a,V b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V operator-(V a,V b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V operator*(V a,double n)=>new(a.X*n,a.Y*n,a.Z*n);
        public double Length=>Math.Sqrt(Dot(this,this));
        public static double Dot(V a,V b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static V Cross(V a,V b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public bool Finite=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Z);
    }
    public static string? ValidateProfile(string hex)
    {
        try {
            if(hex.Length is <80 or >8192||hex.Length%2!=0||hex.Any(c=>!(c is >= '0' and <= '9' or >= 'A' and <= 'F')))return"Profile must be uppercase generated hex, 40–4096 bytes.";
            byte[] data=Convert.FromHexString(hex);int offset=0;
            uint U(){if(offset+4>data.Length)throw new InvalidDataException();var n=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset,4));offset+=4;return n;}
            double F()=>BitConverter.Int32BitsToSingle(unchecked((int)U()));
            V Vec()=>new(F(),F(),F());
            if(U()!=1)return"Unsupported surface profile version.";
            int length=checked((int)U());if(length is <1 or >192||offset+length>data.Length)return"Invalid profile model name.";
            if(data.AsSpan(offset,length).ToArray().Any(b=>b<32||b>126))return"Profile model path is not printable ASCII.";
            string model=Encoding.ASCII.GetString(data,offset,length);offset+=length;
            if(model.Contains("..")||model.Contains(':')||model[0] is '/' or '\\'||model.Any(c=>c<32||c>126))return"Unsafe profile model path.";
            uint high=U(),low=U();var center=Vec();double radius=F();int count=checked((int)U()),triangles=checked((int)U());
            if((high==0&&low==0)||!center.Finite||!double.IsFinite(radius)||radius<.0001||radius>100000||count is <3 or >128||triangles is <1 or >128)return"Invalid profile source identity, bounds or geometry count.";
            var vertices=new V[count];for(int i=0;i<count;i++){vertices[i]=Vec();if(!vertices[i].Finite||vertices[i].Length>1.0001)return"Profile vertices are not finite normalized geometry.";}
            var mesh=new List<(int A,int B,int C,bool Eligible)>();
            for(int i=0;i<triangles;i++) {int a=checked((int)U()),b=checked((int)U()),d=checked((int)U()),eligible=checked((int)U());if(a>=count||b>=count||d>=count||eligible>1||V.Cross(vertices[b]-vertices[a],vertices[d]-vertices[a]).Length<1e-8)return"Invalid or degenerate profile triangle.";mesh.Add((a,b,d,eligible==1));}
            if(offset!=data.Length||!mesh.Any(t=>t.Eligible))return"Profile has trailing bytes or no eligible weakspot surface.";
            foreach(var target in mesh.Where(t=>t.Eligible)) {
                V position=vertices[target.A]*.3334+vertices[target.B]*.3333+vertices[target.C]*.3333,origin=new(0,0,3),direction=position-origin;direction=direction*(1/direction.Length);double nearest=double.MaxValue;
                double depth=3-position.Z,x=500*(1+position.X/(depth*.36397023426620234*(370d/228))),y=500*(1-position.Y/(depth*.36397023426620234));if(depth<=.0001||x<0||x>1000||y<0||y>1000)continue;
                foreach(var triangle in mesh){V a=vertices[triangle.A],e1=vertices[triangle.B]-a,e2=vertices[triangle.C]-a,h=V.Cross(direction,e2);double det=V.Dot(e1,h);if(Math.Abs(det)<1e-10)continue;V delta=origin-a;double u=V.Dot(delta,h)/det;if(u< -1e-7||u>1.0000001)continue;V q=V.Cross(delta,e1);double v=V.Dot(direction,q)/det;if(v< -1e-7||u+v>1.0000001)continue;double distance=V.Dot(e2,q)/det;if(distance>1e-6&&distance<nearest)nearest=distance;}
                if(nearest<double.MaxValue&&(origin+direction*nearest-position).Length<.002)return null;
            }
            return"Profile has no initially visible weakspot surface.";
        } catch(Exception e)when(e is FormatException or OverflowException or InvalidDataException or ArgumentException or IndexOutOfRangeException){return"Malformed profile geometry payload.";}
    }
}
