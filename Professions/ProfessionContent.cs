using System.Globalization;
using System.Text.Json.Serialization;

namespace LastChaos_ToolBoxNG.Professions;

public sealed class ProfessionRow
{
    public Dictionary<string,string> Values { get; set; } = new(StringComparer.Ordinal);
    public string Get(string column) => Values.GetValueOrDefault(column, "");
    public int Int(string column) => decimal.TryParse(Get(column),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&n>=int.MinValue&&n<=int.MaxValue&&n==decimal.Truncate(n)?(int)n:0;
    public decimal Decimal(string column) => decimal.TryParse(Get(column),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;
    public void Set(string column,object? value) => Values[column]=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    public bool Enabled => Values.ContainsKey("a_enabled") ? Int("a_enabled")==1 : !Values.ContainsKey("a_enable") || Int("a_enable")==1;
    public string Key(ProfessionTable table) => string.Join("/",table.Keys.Select(c=>Decimal(c).ToString("G29",CultureInfo.InvariantCulture)));
    public override string ToString() => Get("a_name") is {Length:>0} n?n:string.Join(" / ",Values.Where(p=>p.Key is "a_index" or "a_item_index" or "a_npc_index" or "a_population_index" or "a_water_index" or "a_slot" or "a_polygon_index" or "a_vertex_index").Select(p=>p.Value));
}
public sealed record ProfessionNpc(int Id,string Name,long Flags,bool Enabled);
public sealed class ProfessionContent
{
    public Dictionary<string,List<ProfessionRow>> Tables { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public Dictionary<int,ProfessionNpc> Npcs { get; set; } = [];
    [JsonIgnore] public Dictionary<int,string> Zones { get; set; } = [];
    [JsonIgnore] public InfusionMetadata Infusions { get; set; } = new();
    public List<ProfessionRow> Rows(string table)
    {
        if(!ProfessionSchema.Tables.Any(t=>t.Name==table))throw new ArgumentException("Unknown profession content table.",nameof(table));
        if(!Tables.TryGetValue(table,out var rows))Tables[table]=rows=[];
        return rows;
    }
    public string Canonical() => System.Text.Json.JsonSerializer.Serialize(ProfessionSchema.Tables.Select(t=>new {Table=t.Name,Rows=Rows(t.Name).OrderBy(r=>r.Key(t),StringComparer.Ordinal).Select(r=>t.Fields.Select(f=>new {f.Column,Value=Normalize(r.Get(f.Column),f,t.Name)}))}));
    public void NormalizeStorage() {foreach(var table in ProfessionSchema.Tables)foreach(var row in Rows(table.Name))foreach(var field in table.Fields)row.Set(field.Column,Normalize(row.Get(field.Column),field,table.Name));}
    private static string Normalize(string value,ProfessionField field,string table) {
        if(field.Kind is FieldKind.Text or FieldKind.Geometry || !decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))return value;
        if(field.Kind==FieldKind.Decimal) {
            string rounded=(table is "t_fishing_water" or "t_fishing_water_vertex" ? ((float)n).ToString("R",CultureInfo.InvariantCulture) : ((double)n).ToString("R",CultureInfo.InvariantCulture));
            if(decimal.TryParse(rounded,NumberStyles.Float,CultureInfo.InvariantCulture,out var storage))n=storage;
        }
        return n.ToString("G29",CultureInfo.InvariantCulture);
    }
}
