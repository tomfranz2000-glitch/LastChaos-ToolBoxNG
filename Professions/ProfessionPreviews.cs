using LastChaos_ToolBoxNG.Crafting;
using System.Globalization;
using System.Text;

namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Offline projections use the same integer formulae and deterministic schedule as the server.</summary>
public static class ProfessionPreviews
{
    public sealed record WaveEvent(bool Hold,uint HeadMs,uint TailMs,uint LeadMs);
    public static uint SkillChance(ProfessionRow settings,int required,int baseChance,int level)
    {
        if(level<required||level>=settings.Int("a_skill_cap"))return 0;
        int decline=settings.Int("a_decline_levels");if(decline<=0)return 0;
        return (uint)Math.Max(settings.Int("a_min_chance_bp"),(long)baseChance*decline/(decline+level-required));
    }
    public static decimal ActiveYield(ProfessionRow settings,ProfessionRow node,int level,int hits,int toolBonusBp=0)
    {
        long bonus=Math.Min(settings.Int("a_max_yield_bp"),(long)Math.Max(0,level-1)*settings.Int("a_yield_per_level_bp"));
        int reference=node.Int("a_profession")==4?node.Int("a_rhythm_beats")*node.Int("a_rhythm_perfect_points"):node.Int("a_weak_spots");
        if(reference<=0||hits<=0)return 0;
        long units=(long)node.Int("a_count")*(10000+bonus+toolBonusBp)*hits*(node.Int("a_profession")==4?2:1)*10/reference;
        return units/100000m;
    }
    public static uint PassiveInterval(ProfessionRow settings,ProfessionRow node,int level)
    {
        long speed=Math.Min(settings.Int("a_passive_max_speed_bp"),(long)Math.Max(0,level-1)*settings.Int("a_passive_speed_per_level_bp"));
        return (uint)((long)node.Int("a_passive_tick_ms")*10000/(10000+speed));
    }
    public static uint PassiveChance(ProfessionRow settings,ProfessionRow node,int level,uint interval)
    {int duration=node.Int("a_timeout_ms");return duration<=0?0:(uint)Math.Min(10000,(ulong)SkillChance(settings,node.Int("a_required_skill"),node.Int("a_skill_chance_bp"),level)*interval*(uint)settings.Int("a_passive_skill_rate_bp")/(uint)duration/10000);}
    public static IReadOnlyList<WaveEvent> GenerateWave(ProfessionRow node,uint seed=1)
    {
        uint C(string key)=>(uint)Math.Max(0,node.Int("a_rhythm_"+key));
        uint duration=(uint)Math.Max(0,node.Int("a_timeout_ms")),countIn=C("count_in_ms"),beats=C("beats"),perfect=C("perfect_ms"),good=C("good_ms"),grace=C("grace_ms"),cooldown=C("click_cooldown_ms"),perfectPoints=C("perfect_points"),goodPoints=C("good_points"),gapStart=C("gap_start_ms"),gapEnd=C("gap_end_ms"),jitter=C("jitter_bp"),holdCount=C("hold_count"),holdMin=C("hold_min_ms"),holdMax=C("hold_max_ms"),leadStart=C("lead_start_ms"),leadEnd=C("lead_end_ms");
        uint recovery=2*(good+grace)+(cooldown+1)/2;
        if(duration<1000||duration>120000||countIn>10000||beats<3||beats>64||perfect<1||perfect>good||good>500||grace>150||cooldown<1||cooldown>1000||(ulong)cooldown*beats>=duration||(ulong)(good+grace)*beats*2>=duration||goodPoints<1||goodPoints>perfectPoints||perfectPoints>100||gapEnd<600||gapEnd>gapStart||gapStart>6000||jitter>2000||holdCount>(beats-2)/2||holdMin<250||holdMin>holdMax||holdMax>3000||leadEnd<500||leadEnd>leadStart||leadStart>5000||holdMax>leadEnd||gapEnd<=recovery||gapStart/2<=good+grace||gapStart/2+gapEnd/2>=duration)return [];
        uint rng=seed!=0?seed:0x6d2b79f5u;
        uint Random(uint bound){unchecked{rng^=rng<<13;rng^=rng>>17;rng^=rng<<5;}return bound!=0?rng%bound:0;}
        for(int attempt=0;attempt<32;attempt++)
        {
            var events=new WaveEvent[beats];var weights=new ulong[beats-1];ulong total=0;
            for(uint i=0;i+1<beats;i++){ulong gap=gapStart-(ulong)(gapStart-gapEnd)*i/(beats-2);uint variation=attempt==31?10000:10000-jitter+Random(2*jitter+1);weights[i]=gap*variation;total+=weights[i];}
            uint start=countIn+gapStart/2,span=duration-gapStart/2-gapEnd/2;ulong cursor=0;
            for(uint i=0;i<beats;i++){if(i!=0)cursor+=weights[i-1];uint head=start+(uint)(cursor*span/total);events[i]=new(false,head,head,leadStart-(uint)((ulong)(leadStart-leadEnd)*i/(beats-1)));}
            var eligible=new bool[beats];var capacity=new uint[beats+2];
            for(uint i=2;i+1<beats;i++)eligible[i]=events[i+1].HeadMs-events[i].HeadMs>=holdMin+recovery;
            for(int i=(int)beats-1;i>=0;i--)capacity[i]=Math.Max(capacity[i+1],eligible[i]?1+capacity[i+2]:0);
            if(capacity[2]<holdCount)continue;uint remaining=holdCount;
            for(uint i=2;remaining!=0&&i+1<beats;i++){if(!eligible[i])continue;if(capacity[i+1]>=remaining&&Random(2)==0)continue;var e=events[i];uint maximum=Math.Min(holdMax,events[i+1].HeadMs-e.HeadMs-recovery);events[i]=e with{Hold=true,TailMs=e.HeadMs+holdMin+Random(maximum-holdMin+1)};remaining--;i++;}
            if(remaining!=0)continue;uint holds=0;bool valid=true;
            for(int i=0;i<events.Length;i++){var e=events[i];if(e.HeadMs>=countIn+duration||e.TailMs>=countIn+duration||e.HeadMs<=countIn+good+grace||e.TailMs<e.HeadMs||e.TailMs+good+grace>=countIn+duration||e.LeadMs<leadEnd||e.LeadMs>leadStart||(i>0&&(e.LeadMs>events[i-1].LeadMs||e.HeadMs<=events[i-1].TailMs||e.HeadMs-events[i-1].TailMs<recovery))){valid=false;break;}if(e.Hold){if(i<2||i+1==events.Length||events[i-1].Hold||e.TailMs-e.HeadMs<holdMin||e.TailMs-e.HeadMs>holdMax||e.TailMs-e.HeadMs>e.LeadMs){valid=false;break;}holds++;}}
            if(valid&&holds==holdCount)return events;
        }
        return seed!=1?GenerateWave(node,1):[];
    }
    public static void Show(IWin32Window owner,CraftingCatalog catalog,ProfessionRow? selected=null)
    {using var dialog=new PreviewDialog(catalog,selected);dialog.ShowDialog(owner);}
    sealed record Source(string Name,ProfessionRow Row,string Kind){public override string ToString()=>Name;}
    sealed class PreviewDialog:Form
    {
        readonly CraftingCatalog catalog;
        readonly ComboBox sources=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=350};
        readonly NumericUpDown level=new(){Minimum=1,Maximum=65535,Value=1,Width=85},score=new(){Minimum=0,Maximum=1000,Value=10,Width=70},tool=new(){Minimum=0,Maximum=1000,DecimalPlaces=2,Width=85},seed=new(){Minimum=0,Maximum=uint.MaxValue,Value=1,Width=110};
        readonly Button reroll=new(){Text="Reroll",AutoSize=true};
        readonly TextBox result=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Font=new Font(FontFamily.GenericMonospace,10)};
        readonly Timeline graph=new(){Dock=DockStyle.Bottom,Height=180};
        public PreviewDialog(CraftingCatalog catalog,ProfessionRow? selected)
        {
            this.catalog=catalog;Text="Profession balance and Wave timing preview";Size=new(1000,730);MinimumSize=new(780,580);StartPosition=FormStartPosition.CenterParent;
            var controls=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new(10)};
            void Field(string title,Control control){controls.Controls.Add(new Label{Text=title,AutoSize=true,Padding=new(2,6,0,0)});controls.Controls.Add(control);}
            Field("Source",sources);Field("Skill",level);Field("Hits / points",score);Field("Tool bonus%",tool);Field("Schedule seed",seed);reroll.Click+=(_,_)=>seed.Value=Random.Shared.Next(1,int.MaxValue);controls.Controls.Add(reroll);
            Controls.Add(result);Controls.Add(graph);Controls.Add(controls);
            foreach(var row in catalog.Professions.Rows("t_gathering_node"))sources.Items.Add(new Source($"{(row.Int("a_profession")==4?"Wave":"Mining")}: {catalog.Professions.Npcs.GetValueOrDefault(row.Int("a_npc_index"))?.Name??("NPC "+row.Int("a_npc_index"))} (#{row.Int("a_index")})",row,"node"));
            foreach(var row in catalog.Professions.Rows("t_herb_population"))sources.Items.Add(new Source($"{catalog.Professions.Npcs.GetValueOrDefault(row.Int("a_npc_index"))?.Name??"Herbs"} • {catalog.Professions.Zones.GetValueOrDefault(row.Int("a_zone_index"))??("Zone "+row.Int("a_zone_index"))} (#{row.Int("a_index")})",row,"herb"));
            foreach(var row in catalog.Professions.Rows("t_fishing_water"))sources.Items.Add(new Source($"Fishing: {row.Get("a_name")}",row,"fish"));
            int index=0;for(int i=0;i<sources.Items.Count;i++)if(ReferenceEquals(((Source)sources.Items[i]).Row,selected))index=i;
            sources.SelectedIndexChanged+=(_,_)=>UpdatePreview();level.ValueChanged+=(_,_)=>UpdatePreview();score.ValueChanged+=(_,_)=>UpdatePreview();tool.ValueChanged+=(_,_)=>UpdatePreview();seed.ValueChanged+=(_,_)=>UpdatePreview();if(sources.Items.Count>0)sources.SelectedIndex=index;else result.Text="Create a source to preview its authored behavior.";
        }
        void UpdatePreview()
        {
            try{UpdatePreviewCore();}
            catch(Exception e){result.Text="Cannot preview the current draft: "+e.Message+"\r\nCorrect the validation errors before balancing this source.";graph.Waves=[];graph.Invalidate();}
        }
        void UpdatePreviewCore()
        {
            if(sources.SelectedItem is not Source source)return;var row=source.Row;bool nodeSource=source.Kind=="node",waveSource=nodeSource&&row.Int("a_profession")==4;score.Enabled=tool.Enabled=nodeSource;seed.Enabled=reroll.Enabled=waveSource;score.Maximum=row.Int("a_profession")==4?Math.Clamp(row.Int("a_rhythm_beats")*row.Int("a_rhythm_perfect_points"),1,1000):1000;var text=new StringBuilder();int skill=(int)level.Value;graph.Waves=[];
            if(source.Kind=="node")
            {
                var settings=catalog.Professions.Rows("t_gathering_settings").FirstOrDefault(r=>r.Int("a_index")==row.Int("a_profession"));if(settings==null){result.Text="Missing profession settings.";return;}
                uint chance=SkillChance(settings,row.Int("a_required_skill"),row.Int("a_skill_chance_bp"),skill),interval=PassiveInterval(settings,row,skill);
                text.AppendLine($"{source.Name} • skill {skill}\r\nActive skill-up chance: {chance/100m:0.00}% per scored round\r\nActive base output at score {(int)score.Value}: {ActiveYield(settings,row,skill,(int)score.Value,(int)(tool.Value*100)):0.#####} item units\r\nRound duration: {row.Int("a_timeout_ms")/1000m:0.###} seconds");
                if(interval>0){int ticks=row.Int("a_passive_duration_ms")/(int)interval;decimal perTick=row.Int("a_passive_count")*(10000+tool.Value*100)/10000;text.AppendLine($"\r\nPassive interval: {interval/1000m:0.###} seconds\r\nFull-session ticks (fixed skill): {ticks}\r\nPassive base output (fixed skill): {ticks*perTick:0.#####} units\r\nPassive chance per tick: {PassiveChance(settings,row,skill,interval)/100m:0.00}%");}
                if(row.Int("a_profession")==4){var events=GenerateWave(row,(uint)seed.Value);graph.Waves=events;graph.Duration=(uint)(row.Int("a_timeout_ms")+row.Int("a_rhythm_count_in_ms"));text.AppendLine($"\r\nGenerated waves: {events.Count}; holds: {events.Count(e=>e.Hold)}\r\nPerfect/Good timing: ±{row.Int("a_rhythm_perfect_ms")} / ±{row.Int("a_rhythm_good_ms")} ms (network grace is late-only)");if(events.Count==0)text.AppendLine("INVALID: timing cannot form a supported round.");foreach(var e in events)text.AppendLine($"{(e.Hold?"HOLD":"TAP ")}  {e.HeadMs/1000m,6:0.000}s → {e.TailMs/1000m,6:0.000}s   approach {e.LeadMs/1000m:0.000}s");}
                Loot("t_gathering_node_loot","a_node_index",row.Int("a_index"));
            }
            else if(source.Kind=="herb")
            {
                var settings=catalog.Professions.Rows("t_gathering_settings").FirstOrDefault(r=>r.Int("a_index")==2);
                int capacity=catalog.Professions.Rows("t_herb_spawn_point").Count(r=>r.Int("a_population_index")==row.Int("a_index")&&r.Enabled);
                text.AppendLine($"Herb population {row.Int("a_index")} • skill {skill}\r\nCandidate capacity: {capacity}\r\nMinimum / initial alive: {row.Int("a_min_alive")} / {row.Int("a_initial_alive")}\r\nQuiet growth to full from initial: {Math.Max(0,capacity-row.Int("a_initial_alive"))*row.Int("a_growth_sec")} seconds\r\nReplacement delay: {row.Int("a_respawn_min_sec")}–{row.Int("a_respawn_max_sec")} seconds\r\nHarvest duration: {row.Int("a_harvest_ms")/1000m:0.###} seconds");
                text.AppendLine("Visual gathering tool only: no owned tool or inventory-tool yield bonus applies.");
                if(settings!=null){text.AppendLine($"Skill-up chance: {SkillChance(settings,row.Int("a_required_skill"),row.Int("a_skill_chance_bp"),skill)/100m:0.00}%\r\nYield bonus: {Math.Min(settings.Int("a_max_yield_bp"),(long)(skill-1)*settings.Int("a_yield_per_level_bp"))/100m:0.00}%");}
                Loot("t_herb_loot","a_population_index",row.Int("a_index"));
            }
            else
            {
                var settings=catalog.Professions.Rows("t_fishing_settings").FirstOrDefault();text.AppendLine("Fishing quantity is fixed by the selected loot outcome; score and tool-yield bonuses do not apply.\r\n");text.AppendLine($"{row.Get("a_name")} • skill {skill}\r\nCast: {row.Int("a_cast_ms")/1000m:0.###} seconds\r\nBite wait: {row.Int("a_bite_min_ms")/1000m:0.###}–{row.Int("a_bite_max_ms")/1000m:0.###} seconds\r\nReaction window: {row.Int("a_reaction_ms")/1000m:0.###} seconds");if(settings!=null)text.AppendLine($"Skill-up chance: {SkillChance(settings,1,row.Int("a_skill_chance_bp"),skill)/100m:0.00}%\r\nRequired carried rod: {settings.Int("a_rod_item_index")}");Loot("t_fishing_loot","a_water_index",row.Int("a_index"));
            }
            void Loot(string table,string key,int parent){var rows=catalog.Professions.Rows(table).Where(r=>r.Int(key)==parent&&r.Enabled).ToArray();decimal sum=rows.Sum(r=>(decimal)r.Int("a_weight"));if(sum<=0)return;text.AppendLine("\r\nExclusive resource roll:");foreach(var r in rows){int id=r.Int("a_item_index");text.AppendLine($"{100*r.Int("a_weight")/sum,6:0.00}%  {r.Int("a_item_count")} × {catalog.Items.GetValueOrDefault(id)?.Name??("Item "+id)}");}}
            text.AppendLine("\r\nOffline balance estimate, not a live gathering simulation. Passive totals hold skill and tool fixed; fractional carry, inventory failures, scheduling and later skill gains affect actual payouts. Wave output is base units before the selected resource's loot quantity.");result.Text=text.ToString();graph.Invalidate();
        }
        sealed class Timeline:Control
        {
            public IReadOnlyList<WaveEvent> Waves=[];public uint Duration=1;
            public Timeline(){DoubleBuffered=true;}
            protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.Clear(Color.FromArgb(25,30,35));if(Waves.Count==0)return;float width=Math.Max(1,Width-50);using var axis=new Pen(Color.Gray);e.Graphics.DrawLine(axis,25,Height/2,Width-25,Height/2);foreach(var wave in Waves){float x=25+width*wave.HeadMs/Math.Max(1,Duration),end=25+width*wave.TailMs/Math.Max(1,Duration);using var brush=new SolidBrush(wave.Hold?Color.Gold:Color.SkyBlue);if(wave.Hold)e.Graphics.FillRectangle(brush,x,Height/2-8,Math.Max(4,end-x),16);else e.Graphics.FillEllipse(brush,x-4,Height/2-4,8,8);}e.Graphics.DrawString("Timeline: blue taps, gold holds • milliseconds derived from the authored round",Font,Brushes.White,15,15);}
        }
    }
}
