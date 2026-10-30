using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;
using LastChaos_ToolBoxNG.Professions;

internal static class Program
{
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static ProfessionRow Row(params (string,object)[] fields){var r=new ProfessionRow();foreach(var f in fields)r.Set(f.Item1,f.Item2);return r;}
    static string Run(string executable,IEnumerable<string> arguments,string? cwd=null)
    {var p=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=cwd??Environment.CurrentDirectory};foreach(var arg in arguments)p.ArgumentList.Add(arg);using var process=Process.Start(p)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(60000)){process.Kill(true);throw new Exception("Helper fixture timed out");}Task.WaitAll(output,error);if(process.ExitCode!=0)throw new Exception(error.Result+output.Result);return output.Result.Replace("\r\n","\n");}
    [STAThread] static int Main(string[] args)
    {
        if(args.Length<2){Console.Error.WriteLine("Usage: ProfessionHelpersVerification <game-checkout> <output-directory> [clang-cl]");return 2;}
        string game=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        var square=new[]{new PointF(10,10),new PointF(20,10),new PointF(20,20),new PointF(10,20)};
        Check(ProfessionMapEditor.ValidatePolygon(square)==null,"simple polygon accepted");
        Check(ProfessionMapEditor.ValidatePolygon(new[]{square[0],square[2],square[1],square[3]})!=null,"self-crossing polygon rejected");
        Check(ProfessionMapEditor.ValidatePolygon(new[]{new PointF(1,1),new PointF(2,2),new PointF(3,3)})!=null,"degenerate polygon rejected");
        Check(ProfessionMapEditor.ValidatePolygon(new[]{square[0],square[1],square[2],square[0]})!=null,"duplicate closing vertex rejected like server");
        Check(ProfessionMapEditor.ValidatePolygon(new[]{new PointF(float.NaN,1),square[1],square[2]})!=null,"nonfinite map point rejected");
        foreach(float ratio in new[]{.3333f,.6666f}){var map=ProfessionMapEditor.WorldToMap(1113,953,ratio);var world=ProfessionMapEditor.MapToWorld(map,ratio);Check(Math.Abs(world.X-1113)<.001&&Math.Abs(world.Y-953)<.001,"map transform roundtrip "+ratio);}
        Check(Math.Abs(ProfessionMapEditor.WorldUnitsPerMapPixel(.6666f)-6.0006f)<.001,"large-map NPC fourfold correction preserved");
        var settings=Row(("a_skill_cap",100),("a_min_chance_bp",100),("a_decline_levels",20),("a_yield_per_level_bp",50),("a_max_yield_bp",5000),("a_passive_speed_per_level_bp",25),("a_passive_max_speed_bp",2500),("a_passive_skill_rate_bp",3000));
        var node=Row(("a_profession",1),("a_count",3),("a_weak_spots",10),("a_required_skill",1),("a_skill_chance_bp",3500),("a_timeout_ms",20000),("a_passive_tick_ms",30000));
        var wave=Row(("a_profession",4),("a_count",4),("a_weak_spots",18),("a_timeout_ms",30000));
        foreach(var f in new[]{("count_in_ms",2000),("beats",18),("perfect_ms",70),("good_ms",180),("grace_ms",80),("click_cooldown_ms",250),("perfect_points",2),("good_points",1),("gap_start_ms",2100),("gap_end_ms",1200),("jitter_bp",1000),("hold_count",4),("hold_min_ms",500),("hold_max_ms",1000),("lead_start_ms",2000),("lead_end_ms",1100)})wave.Set("a_rhythm_"+f.Item1,f.Item2);
        string cpp=Path.Combine(output,"production-reference.cpp"),exe=Path.Combine(output,"production-reference.exe");
        File.WriteAllText(cpp,"""
#include "Gathering.h"
#include "GatheringPassive.h"
#include <iostream>
int main(){using namespace Gathering; Settings s;s.cap=100;s.floorBp=100;s.declineLevels=20;s.yieldPerLevelBp=50;s.maxYieldBp=5000;s.passiveSpeedPerLevelBp=25;s.passiveMaxSpeedBp=2500;s.passiveSkillRateBp=3000;Node n;n.count=3;n.spots=10;n.required=1;n.chanceBp=3500;n.timeoutMs=20000;n.passiveTickMs=30000;Tool t;t.yieldBp=123;
for(int level:{1,20,99,100})for(int hits:{5,10,20}){auto y=CalculateYield(s,n,t,level,0,hits);auto interval=PassiveInterval(s,n,level);std::cout<<level<<","<<hits<<","<<y.count*100000+y.remainder<<","<<Chance(s,n,level)<<","<<interval<<","<<PassiveChance(s,n,level,interval)<<"\n";}
RhythmConfig c;c.countInMs=2000;c.beats=18;c.perfectMs=70;c.goodMs=180;c.graceMs=80;c.clickCooldownMs=250;c.perfectPoints=2;c.goodPoints=1;c.gapStartMs=2100;c.gapEndMs=1200;c.jitterBp=1000;c.holdCount=4;c.holdMinMs=500;c.holdMaxMs=1000;c.leadStartMs=2000;c.leadEndMs=1100;
for(unsigned seed:{0u,1u,42u,0xffffffffu}){std::vector<RhythmEvent> events;if(!GenerateRhythmSchedule(c,30000,seed,events))return 2;std::cout<<"S"<<seed<<"\n";for(auto e:events)std::cout<<(e.kind==RhythmHold)<<","<<e.headMs<<","<<e.tailMs<<","<<e.leadMs<<"\n";} }
""");
        string compiled=Run(args.Length>2?args[2]:"clang-cl",new[]{"/nologo","/std:c++14","/EHsc","/MD","/I"+Path.Combine(game,"x64-server","src","Server","ShareLib"),cpp,"/Fo"+Path.Combine(output,"production-reference.obj"),"/Fe"+exe});File.WriteAllText(Path.Combine(output,"reference-build.log"),compiled);
        var reference=Run(exe,[]).Split('\n',StringSplitOptions.RemoveEmptyEntries);int index=0;
        foreach(int skill in new[]{1,20,99,100})foreach(int hits in new[]{5,10,20}){string expected=$"{skill},{hits},{(long)(ProfessionPreviews.ActiveYield(settings,node,skill,hits,123)*100000)},{ProfessionPreviews.SkillChance(settings,1,3500,skill)},{ProfessionPreviews.PassiveInterval(settings,node,skill)},{ProfessionPreviews.PassiveChance(settings,node,skill,ProfessionPreviews.PassiveInterval(settings,node,skill))}";Check(reference[index++]==expected,"production integer balance parity "+skill+" / "+hits);}
        foreach(uint seed in new uint[]{0,1,42,uint.MaxValue}){Check(reference[index++]=="S"+seed,"production seed marker");var events=ProfessionPreviews.GenerateWave(wave,seed);Check(events.Count==18&&events.Count(e=>e.Hold)==4,"authored wave and hold count");foreach(var e in events)Check(reference[index++]==$"{(e.Hold?1:0)},{e.HeadMs},{e.TailMs},{e.LeadMs}","production schedule parity "+seed+" @ "+e.HeadMs);}
        Check(ProfessionPreviews.ActiveYield(settings,wave,1,36)==8,"all-perfect Wave produces twice base yield");wave.Set("a_rhythm_hold_count",20);Check(ProfessionPreviews.GenerateWave(wave).Count==0,"invalid authored hold count rejected");
        string bundle=Path.Combine(output,"fixture.vkd"),sql=Path.Combine(output,"fixture.sql"),metadata=Path.Combine(output,"fixture.json");
        using(var w=new BinaryWriter(File.Create(bundle))){w.Write(Encoding.ASCII.GetBytes("LCVDIST2"));w.Write(2u);Text("Data\\Monster\\Product\\fixture.bm");w.Write(0x123456789abcdef0UL);w.Write(0u);w.Write(1u);Text("solid");w.Write(1UL);w.Write(0u);w.Write(0u);w.Write(3u);w.Write(3u);foreach(var p in new[]{new[]{0f,0,0},new[]{1f,0,0},new[]{0f,1,0}}){foreach(float n in p)w.Write(n);for(int j=0;j<15;j++)w.Write(0f);}w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)2);void Text(string s){var bytes=Encoding.ASCII.GetBytes(s);w.Write((uint)bytes.Length);w.Write(bytes);}}
        Check(ProfessionProfileImporter.ReadBundleSurfaces(bundle).SequenceEqual(new[]{"solid"}),"surface picker reads actual LOD0 bundle names");
        File.WriteAllText(Path.Combine(output,"importer.log"),Run("python",new[]{Path.Combine(game,"tools","gathering","ImportSurfaceProfile.py"),"--bundle",bundle,"--surface","solid","--profile","1","--output",sql,"--metadata",metadata}));
        string hex=ProfessionProfileImporter.ExtractHex(File.ReadAllText(sql));ProfessionProfileImporter.ValidateMetadata(hex,File.ReadAllText(metadata));Check(hex.Length>0,"actual Python LOD0 importer output and embedded hash/version agree");
        bool rejected=false;try{ProfessionProfileImporter.ValidateMetadata(hex,File.ReadAllText(metadata).Replace("123456789abcdef0","123456789abcdef1"));}catch(InvalidDataException){rejected=true;}Check(rejected,"wrong source hash metadata rejected");
        rejected=false;try{ProfessionProfileImporter.ExtractHex("SELECT 'not a profile';");}catch(InvalidDataException){rejected=true;}Check(rejected,"nonprofile output rejected without SQL execution");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks,liveDatabaseChanged=false}));Console.WriteLine(checks+" helper checks passed");return 0;
    }
}
