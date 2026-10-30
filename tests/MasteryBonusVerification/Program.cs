using System.Globalization;
using System.Reflection;
using System.Text.Json;
using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.Crafting;

static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool condition,string name) {if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name) {try{action();}catch(FormatException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static object? Call(object owner,string name,params object?[] args)
    {
        var method=owner.GetType().GetMethod(name,Private)!;
        if(args.Length==0&&method.GetParameters().Length>0)args=method.GetParameters().Select(p=>p.DefaultValue).ToArray();
        try{return method.Invoke(owner,args);}catch(TargetInvocationException error){throw error.InnerException!;}
    }
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    static IEnumerable<Control> All(Control owner)
    {foreach(Control child in owner.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    static CraftingCatalog Catalog()
    {
        var c=new CraftingCatalog();
        c.Items[200001]=new(200001,"Fixture potion",true,6,3,1,1000,0,0,0,false);
        c.Items[200002]=new(200002,"Fixture ingredient",true,4,0,1,1000,0,0,0,false);
        c.Items[200003]=new(200003,"Fixture resource",true,4,0,1,1000,0,0,0,false);
        c.Recipes.Add(new(){Id=1,Profession=Profession.Alchemy,OutputItem=200001,Ingredients=[new(){ItemId=200002}],Enabled=false});
        c.Gathering.Add(new(){ItemId=200003,Profession=1,MasteryXp=1,BronzeRequired=1,SilverRequired=3,MasteryRequired=7});
        c.Ranks.Add(new(){Id=1,Order=1,RequiredMastered=1,Name="Fixture rank"});
        return c;
    }
    static bool BonusError(CraftingCatalog c)=>CraftingRules.Validate(c).Any(i=>i.Error&&(i.Target=="Bonuses"||i.Message.StartsWith("Bonuses need",StringComparison.Ordinal)));
    static bool Edit(DataGridView grid,int row,string text)
    {
        grid.CurrentCell=grid.Rows[row].Cells["Amount"];grid.BeginEdit(true);Application.DoEvents();
        ((TextBox)grid.EditingControl!).Text=text;
        bool result=grid.EndEdit();Application.DoEvents();return result;
    }
    [STAThread]static int Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;CultureInfo.CurrentUICulture=CultureInfo.InvariantCulture;
        string output=Path.GetFullPath(args.FirstOrDefault()??Path.Combine(Path.GetTempPath(),"lc-mastery-bonus-fixture-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":false,\"liveDatabaseChanged\":false}");
        foreach(int effect in Enumerable.Range(0,24).Concat(new[]{102,103})) {
            Check(MasteryBonusUnits.Store(effect,25)==25&&MasteryBonusUnits.Display(effect,25)==25,"legacy whole-point roundtrip "+effect);
            Reject(()=>MasteryBonusUnits.Store(effect,1.5m),"legacy fractional points rejected "+effect);
        }
        foreach(var sample in new[]{(1000,0.6m,60),(1001,6m,600),(1002,4m,400)}) {
            Check(MasteryBonusUnits.Store(sample.Item1,sample.Item2)==sample.Item3&&MasteryBonusUnits.Display(sample.Item1,sample.Item3)==sample.Item2,"readable scaled roundtrip "+sample.Item1);
            Reject(()=>MasteryBonusUnits.Store(sample.Item1,.001m),"excess precision rejected "+sample.Item1);
            Reject(()=>MasteryBonusUnits.Store(sample.Item1,0),"zero rejected "+sample.Item1);
            Reject(()=>MasteryBonusUnits.Store(sample.Item1,MasteryBonusUnits.Display(sample.Item1,MasteryBonusUnits.Maximum(sample.Item1))+.01m),"effect limit enforced "+sample.Item1);
        }
        Check(MasteryBonusUnits.Parse(1000,"0.6",CultureInfo.GetCultureInfo("de-DE"))==.6m&&MasteryBonusUnits.Parse(1000,"0,6",CultureInfo.GetCultureInfo("de-DE"))==.6m,"decimal point and local decimal separator both accepted without grouping confusion");
        Check(MasteryBonusUnits.Parse(1001,"6%")==6,"percentage suffix accepted");
        Reject(()=>MasteryBonusUnits.Store(1001,decimal.MaxValue),"huge pasted values rejected without overflow");
        var scaled=new Bonus{Effect=1000,Amount=60,Rank=1};string json=JsonSerializer.Serialize(scaled);var copied=CraftingCatalog.Copy(scaled);
        Check(copied.Amount==60&&copied.DisplayAmount==.6m&&!json.Contains("DisplayAmount")&&!json.Contains("DisplayUnit"),"raw database/JSON contract preserved; display properties are never serialized");
        var tiers=new List<Bonus>{new(){Effect=1000,Amount=15,Rank=1},new(){Effect=1000,Amount=15,Rank=2},new(){Effect=1000,Amount=30,Rank=3},
            new(){Effect=1001,Amount=150,Rank=1},new(){Effect=1001,Amount=150,Rank=2},new(){Effect=1001,Amount=300,Rank=3},new(){Effect=1002,Amount=400,Rank=3}};
        string rewards=CraftingRules.Rewards(tiers);
        Check(rewards.Contains("+0.6 Movement Speed")&&rewards.Contains("6% shorter Attack Interval")&&rewards.Contains("+4% PvE Double Damage Chance"),"cumulative tier summaries use readable speed and percentages");
        Check(CraftingRules.Rewards([new(){Effect=6,Amount=25}])=="+25 Physical Attack","legacy summary meaning unchanged");
        foreach(int effect in new[]{1000,1001,1002}) {
            var c=Catalog();int cap=MasteryBonusUnits.Maximum(effect);
            c.Recipes[0].Bonuses=[new(){Effect=effect,Amount=cap-2,Rank=1}];c.Gathering[0].Bonuses=[new(){Effect=effect,Amount=1,Rank=2}];c.Ranks[0].Bonuses=[new(){Effect=effect,Amount=1}];
            Check(!BonusError(c),"aggregate at cap accepted across retired recipe, resource and Artisan Rank "+effect);
            c.Ranks[0].Bonuses[0].Amount=2;Check(BonusError(c),"aggregate over cap rejected across all ownership domains "+effect);
            c=Catalog();c.Recipes[0].Bonuses=[new(){Effect=effect,Amount=cap+1}];Check(BonusError(c),"per-bonus over-cap value rejected "+effect);
        }
        var alias=Catalog();alias.Recipes[0].Bonuses=[new(){Effect=20,Amount=32767}];alias.Gathering[0].Bonuses=[new(){Effect=7,Amount=1}];Check(BonusError(alias),"legacy overlapping All/Melee channel protection retained");
        var unknown=Catalog();unknown.Recipes[0].Bonuses=[new(){Effect=1003,Amount=1}];Check(BonusError(unknown),"unknown mastery effects rejected");

        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using var host=new LastChaos_ToolBoxNG.Main();host.pSettings.WorkLocale="usa";host.pSettings.DBHost="invalid.offline.fixture";host.pSettings.DBData="not_a_database";
        using var editor=new CraftingMasteryEditor(host);var catalog=Catalog();Set(editor,"catalog",catalog);Set(editor,"loaded",true);
        using(var frame=new Form {Name="OffscreenMasteryFixture",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new(-20000,-20000),Size=new(1050,640)}) {
            var pages=new TabControl{Dock=DockStyle.Fill};frame.Controls.Add(pages);
            foreach(string context in new[]{"Recipe","Resource","Artisan Rank"}) {
                var values=new List<Bonus>{new(){Effect=1000,Amount=15,Rank=1},new(){Effect=1001,Amount=150,Rank=2},new(){Effect=1002,Amount=100,Rank=3},new(){Effect=6,Amount=25,Rank=3}};
                var page=new TabPage(context);page.Controls.Add((Control)Call(editor,"BonusGrid",values,context!="Artisan Rank")!);pages.TabPages.Add(page);
                pages.SelectedTab=page;frame.Show();Application.DoEvents();
                var grid=All(page).OfType<DataGridView>().Single();
                Check(grid.Rows.Count==4&&grid.Columns["Amount"]!.DataPropertyName==nameof(Bonus.DisplayAmount),context+" actual grid binds readable values");
                Check(Edit(grid,0,"0.6")&&values[0].Amount==60,context+" grid edit0.6 stores60");
                Check(Edit(grid,1,"6%")&&values[1].Amount==600,context+" grid edit6% stores600");
                Check(Edit(grid,2,"4")&&values[2].Amount==400,context+" grid edit4% stores400");
                Check(!Edit(grid,3,"1.5")&&values[3].Amount==25,context+" rejects fractional flat amounts without changing stored value");grid.CancelEdit();
                Check(!Edit(grid,1,"90.01")&&values[1].Amount==600,context+" rejects over-cap attack interval");grid.CancelEdit();
                grid.Rows[0].Cells["Effect"].Value=1002;Application.DoEvents();
                Check(values[0].Amount==60&&grid.Rows[0].Cells[nameof(Bonus.DisplayUnit)].FormattedValue?.ToString()=="% chance (PvE)",context+" changing effect refreshes units without silently changing stored amount");
                using var image=new Bitmap(frame.Width,frame.Height);frame.DrawToBitmap(image,new(0,0,image.Width,image.Height));image.Save(Path.Combine(output,context.Replace(' ','-')+".png"));
            }
        }
        // Exercise the actual modal bulk form offscreen, without sending input to any desktop window.
        var recipes=(DataGridView)editor.GetType().GetField("recipes",Private)!.GetValue(editor)!;
        catalog.Recipes[0].Enabled=true;catalog.Recipes.Add(CraftingCatalog.Copy(catalog.Recipes[0]));catalog.Recipes[1].Id=2;
        Call(editor,"FilterRecipes");foreach(DataGridViewRow row in recipes.Rows)row.Selected=true;
        Check(recipes.SelectedRows.Count==2,"bulk fixture selects both real recipe rows");
        bool bulkSeen=false;
        using(var timer=new System.Windows.Forms.Timer{Interval=30}) {
            timer.Tick+=(_,_)=> {
                var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Name=="BulkRecipeEditor");if(dialog is null)return;
                dialog.ShowInTaskbar=false;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new(-20000,-20000);
                ((ComboBox)dialog.Controls.Find("BulkField",true).Single()).SelectedIndex=5;
                ((ComboBox)dialog.Controls.Find("BulkBonusEffect",true).Single()).SelectedValue=1001;
                var amount=(NumericUpDown)dialog.Controls.Find("BulkValue",true).Single();
                Check(amount.DecimalPlaces==2&&amount.Maximum==90,"bulk bonus uses human percentages and90% maximum");amount.Value=6;
                bulkSeen=true;timer.Stop();dialog.DialogResult=DialogResult.OK;dialog.Close();
            };
            timer.Start();Call(editor,"BulkEdit");
        }
        Check(bulkSeen&&catalog.Recipes.All(r=>r.Bonuses.Any(b=>b.Effect==1001&&b.Amount==600&&b.Rank==3)),"actual bulk edit stores6% as600 for each selected recipe: "+JsonSerializer.Serialize(catalog.Recipes.Select(r=>new{r.Id,r.Bonuses})));
        Set(editor,"dirty",false);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks,liveDatabaseChanged=false,gameLaunched=false,toolboxVisible=false}));
        Console.WriteLine($"PASS {checks} checks; output {output}");return 0;
    }
}
