using System.Data;
using System.Globalization;
using MySqlConnector;
using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG.Professions;

public static class ProfessionStorage
{
    public static void CheckSchema(MySqlConnection db,MySqlTransaction tx)
    {
        try {
            foreach(var table in ProfessionSchema.Tables) {
                using var command=new MySqlCommand("SELECT "+string.Join(",",table.Fields.Select(f=>f.Kind==FieldKind.Decimal?"CAST(`"+f.Column+"` AS DOUBLE)":"`"+f.Column+"`"))+" FROM `"+table.Name+"` LIMIT 0",db,tx);
                using var reader=command.ExecuteReader();
            }
        } catch(MySqlException e) when(e.Number is 1054 or 1146) {
            throw new InvalidOperationException("Profession content schema is incomplete. Apply the game's migrations through 0043; this editor never creates or changes database schema.",e);
        }
    }
    public static ProfessionContent Read(MySqlConnection db,MySqlTransaction tx,bool locked,string locale="usa")
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(locale,"^[a-z]+$"))throw new ArgumentException("Invalid NPC locale.");
        var content=new ProfessionContent();
        foreach(var table in ProfessionSchema.Tables) {
            using var command=new MySqlCommand("SELECT "+string.Join(",",table.Fields.Select(f=>f.Kind==FieldKind.Decimal?"CAST(`"+f.Column+"` AS DOUBLE)":"`"+f.Column+"`"))+" FROM `"+table.Name+"` ORDER BY "+string.Join(",",table.Keys.Select(k=>"`"+k+"`"))+(locked?" FOR UPDATE":""),db,tx);
            using var reader=command.ExecuteReader();
            while(reader.Read()) {
                var row=new ProfessionRow();
                for(int i=0;i<table.Fields.Length;i++)row.Set(table.Fields[i].Column,reader.GetValue(i));
                content.Rows(table.Name).Add(row);
            }
        }
        using(var command=new MySqlCommand("SELECT a_index,COALESCE(NULLIF(a_name_"+locale+",''),a_name,''),a_flag,a_enable FROM t_npc ORDER BY a_index"+(locked?" LOCK IN SHARE MODE":""),db,tx))
        using(var reader=command.ExecuteReader())while(reader.Read()) {
            int id=reader.GetInt32(0);content.Npcs[id]=new(id,reader.GetString(1),reader.GetInt64(2),reader.GetBoolean(3));
        }
        using(var command=new MySqlCommand("SELECT a_zone_index,COALESCE(a_name,'') FROM t_zonedata ORDER BY a_zone_index"+(locked?" LOCK IN SHARE MODE":""),db,tx))
        using(var reader=command.ExecuteReader())while(reader.Read())content.Zones[reader.GetInt32(0)]=reader.GetString(1);
        content.NormalizeStorage();
        content.Infusions=InfusionMetadata.Read(db,tx,locked,locale);
        return content;
    }
    private static void Execute(MySqlConnection db,MySqlTransaction tx,string sql,params object[] values)
    {
        using var command=new MySqlCommand(sql,db,tx);
        for(int i=0;i<values.Length;i++)command.Parameters.AddWithValue("@p"+i,values[i]);
        command.ExecuteNonQuery();
    }
    public static void Save(MySqlConnection db,MySqlTransaction tx,ProfessionContent original,ProfessionContent edited)
    {
        if(original.Canonical()==edited.Canonical())return;
        string[] children=["t_gathering_node_loot","t_herb_spawn_point","t_herb_loot","t_fishing_water_vertex","t_fishing_loot",InfusionSchema.Ingredients,InfusionSchema.ConstructionCosts];
        foreach(var table in children)Execute(db,tx,"DELETE FROM `"+table+"`");
        // Temporary unreferenced NPC numbers permit unique-binding swaps inside
        // this transaction. They are never published to the game or seed export.
        foreach(var name in new[]{"t_gathering_node","t_herb_population"}) {
            var table=ProfessionSchema.Find(name);
            var occupied=original.Rows(name).Concat(edited.Rows(name)).Select(r=>r.Int("a_npc_index")).ToHashSet();
            int temporary=int.MaxValue;
            foreach(var row in original.Rows(name)) {
                while(occupied.Contains(temporary))--temporary;
                Execute(db,tx,"UPDATE `"+name+"` SET a_npc_index=@p0 WHERE a_index=@p1",temporary,row.Int("a_index"));occupied.Add(temporary--);
            }
        }
        foreach(var table in ProfessionSchema.Tables) {
            var columns=table.Fields.Select(f=>f.Column).ToArray();
            var sql="INSERT INTO `"+table.Name+"` ("+string.Join(",",columns.Select(c=>"`"+c+"`"))+") VALUES("+string.Join(",",columns.Select((_,i)=>"@p"+i))+")";
            if(!children.Contains(table.Name))sql+=" ON DUPLICATE KEY UPDATE "+string.Join(",",columns.Where(c=>!table.Keys.Contains(c)).Select(c=>"`"+c+"`=VALUES(`"+c+"`)"));
            foreach(var row in edited.Rows(table.Name))Execute(db,tx,sql,columns.Select(c=>(object)row.Get(c)).ToArray());
        }
    }
    public static void ValidatePublished(ProfessionContent original,ProfessionContent edited)
    {
        foreach(var name in new[]{"t_gathering_settings","t_gathering_profile","t_gathering_node","t_gathering_tool","t_herb_population","t_fishing_settings","t_fishing_water",InfusionSchema.Effects,InfusionSchema.Offers,InfusionSchema.Facility,InfusionSchema.Consumables}) {
            var table=ProfessionSchema.Find(name);var keys=edited.Rows(name).Select(r=>r.Key(table)).ToHashSet(StringComparer.Ordinal);
            if(original.Rows(name).Any(r=>!keys.Contains(r.Key(table))))throw new InvalidOperationException("Published "+table.Title+" IDs cannot be removed or renumbered. Disable source/tool definitions instead; retain imported profiles.");
        }
        foreach(var mapping in original.Rows(InfusionSchema.Consumables)) {
            var current=edited.Rows(InfusionSchema.Consumables).First(r=>r.Int("a_item_index")==mapping.Int("a_item_index"));
            if(current.Int("a_effect_index")!=mapping.Int("a_effect_index"))
                throw new InvalidOperationException("A published permanent potion's effect mapping cannot change. Create a new item for a different effect; existing potions and brewing jobs retain their identity.");
        }
        foreach(var effect in original.Rows(InfusionSchema.Effects)) {
            var current=edited.Rows(InfusionSchema.Effects).First(r=>r.Int("a_index")==effect.Int("a_index"));
            if(new[]{"a_item_index","a_skill_index","a_skill_level"}.Any(column=>effect.Get(column)!=current.Get(column)))
                throw new InvalidOperationException("A published permanent infusion effect's potion, skill and level cannot change. Add a new effect and retire the old offer; acquired character effects must retain their identity.");
        }
    }
}
