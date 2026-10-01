using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.Crafting;
using MySqlConnector;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    // Intentionally fixed disposable endpoint. Never use the toolbox settings.
    const string Endpoint = "Server=127.0.0.1;Port=33317;User ID=root;Password=crafting-fixture-only;Connection Timeout=4;Default Command Timeout=10;";
    const string Connection = Endpoint + "Database=ep4_data;Character Set=utf8mb4;";
    static readonly CraftingRepository Repository = new(Connection, "usa");
    static readonly List<object> Results = [];
    static MySqlConnection db = null!;
    static int failures;
    static string game = "", output = "";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: CraftingVerification <game-checkout> <output-directory>"); return 2; }
        game = Path.GetFullPath(args[0]); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        using (var admin = new MySqlConnection(Endpoint)) {
            admin.Open(); using var cmd = new MySqlCommand("DROP DATABASE IF EXISTS ep4_data; DROP DATABASE IF EXISTS ep4_db; CREATE DATABASE ep4_data CHARACTER SET utf8mb4; CREATE DATABASE ep4_db CHARACTER SET utf8mb4;", admin); cmd.ExecuteNonQuery();
        }
        using var connection = new MySqlConnection(Connection); db = connection; db.Open();
        Exec("CREATE TABLE ep4_db.t_characters(a_index INT PRIMARY KEY,a_crafting_stat_points INT NOT NULL,CONSTRAINT chk_crafting_stat_points CHECK(a_crafting_stat_points BETWEEN 0 AND 98)) ENGINE=InnoDB;");
        foreach (string filename in Directory.GetFiles(Path.Combine(game, "x64-server/db/migrations"), "*.sql").Where(p => int.TryParse(Path.GetFileName(p)[..4], out int n) && n is >= 13 and <= 17).Order()) Exec(File.ReadAllText(filename));
        Exec("USE ep4_data; CREATE TABLE t_item(a_index INT PRIMARY KEY,a_name_usa VARCHAR(100),a_enable INT,a_type_idx INT,a_subtype_idx INT,a_flag BIGINT,a_weight INT,a_texture_id INT,a_texture_row INT,a_texture_col INT) ENGINE=InnoDB;");
        foreach (string table in CraftingRepository.Tables) Exec(File.ReadAllText(Path.Combine(game, "x64-server/db/seeds/ep4_data", table + ".sql")));
        Exec("INSERT INTO t_item SELECT item,CONCAT('Fixture item ',item),1,4,0,1,2147483647,0,0,0 FROM (SELECT a_output_item item FROM t_crafting_recipe UNION SELECT a_item_index FROM t_crafting_ingredient UNION SELECT a_manual_item FROM t_crafting_manual UNION SELECT a_item_index FROM t_crafting_bonus_output) refs; UPDATE t_item SET a_type_idx=2,a_subtype_idx=1 WHERE a_index IN(SELECT a_manual_item FROM t_crafting_manual);");
        Run("current authored catalogue loads and validates", () => {
            var c = Repository.Load(); Check(c.Recipes.Count > 600, "Expected the converted manuals.");
            Check(!CraftingRules.Validate(c).Any(i => i.Error), string.Join("\n", CraftingRules.Validate(c)));
            File.WriteAllText(Path.Combine(output, "catalogue-counts.txt"), $"{c.Recipes.Count} recipes; {c.Ranks.Count} ranks\n");
        });
        Exec("INSERT INTO t_item VALUES (200001,'Healing meal',1,4,0,1,9999,0,0,0),(200002,'Herb',1,4,0,1,9999,0,0,0),(200003,'Recipe book',1,2,1,1,9999,0,0,0),(200004,'Bonus powder',1,4,0,1,9999,0,0,0),(200005,'Second recipe book',1,2,4,1,9999,0,0,0);");
        Exec("INSERT INTO ep4_db.t_crafting_mastery VALUES(7,1,20,1); INSERT INTO ep4_db.t_crafting_master_rank VALUES(7,1); INSERT INTO ep4_db.t_crafting_recipe_unlock VALUES(7,1);");

        Run("all eight content tables round-trip including Cooking", () => {
            Reset(); var empty = Repository.Load(); var c = Sample(); Repository.Save(empty, c);
            Check(Repository.Load().Fingerprint() == c.Fingerprint(), "Round-trip differs.");
            Check(Scalar("SELECT a_chance FROM t_crafting_bonus_output") == "1234", "Percentage precision lost.");
        });
        Run("exclusive output chance and item collisions are rejected", () => {
            var c = Sample(); c.Recipes[0].BonusOutputs.Add(new() { ItemId = 200005, ChancePercent = 99 });
            Invalid(c, "Bonus"); c = Sample(); c.Recipes[0].BonusOutputs[0].ChancePercent = 0.001m; Invalid(c, "Bonus");
            c = Sample(); c.Recipes[0].BonusOutputs[0].ItemId = 200002; Invalid(c, "Bonus");
        });
        Run("derived bands use endpoints and handle short intervals/cap", () => {
            var r = Sample().Recipes[0]; r.RequiredSkill = 10; r.NoSkillUp = 40;
            Check(new[] {9,10,19,20,29,30,39,40,50}.Select(s => CraftingRules.Chance(r,s)).SequenceEqual(new[] {0,100,100,60,60,20,20,0,0}), "Chance curve changed.");
            Check(CraftingRules.Training(r).StartsWith("0% at 1–9") && CraftingRules.Training(r).EndsWith("0% at 40–50"), "Disconnected zero bands merged.");
            r.NoSkillUp = 11; Check(CraftingRules.Chance(r,10) == 100 && CraftingRules.Chance(r,11) == 0, "One-level interval.");
            r.RequiredSkill = 49; r.NoSkillUp = 1000000; Check(CraftingRules.Chance(r,49) == 100 && CraftingRules.Chance(r,50) == 0, "Cap.");
        });
        Run("manual validity and many-to-many assignments", () => {
            Reset(); var c = Sample(); var copy = CraftingCatalog.Copy(c.Recipes[0]); copy.Id = 2; c.Recipes.Add(copy);
            c.Recipes[0].Manuals.Add(new() { ItemId = 200005 }); Repository.Save(Repository.Load(), c);
            Check(CraftingItemReferences.Read(Connection,200003).Count == 2, "Manual cannot teach two recipes.");
            c.Items[200003] = c.Items[200003] with { Flags = 65537 }; Invalid(c, "Teaching");
        });
        Run("retired rewards count toward overlapping stat limit", () => {
            var c = Sample(); c.Recipes[0].Enabled = false; c.Recipes[0].Bonuses = [new() {Effect=20,Amount=32767}];
            c.Ranks[0].Bonuses = [new() {Effect=7,Amount=1}]; Invalid(c,"Combined");
        });
        Run("UTF-8 byte limits and unsafe emblem paths", () => {
            var c=Sample(); c.Ranks[0].Name = new string('界',22); Invalid(c,"UTF-8");
            c=Sample(); c.Ranks[0].Emblem="../other.tex"; Invalid(c,"Emblem");
        });
        Run("second editor cannot overwrite a newer save", () => {
            SeedSample(); var basis=Repository.Load(); var first=CraftingCatalog.Copy(basis); first.Recipes[0].CraftTimeMs=5555; Repository.Save(basis,first);
            var second=CraftingCatalog.Copy(basis); second.Recipes[0].CraftTimeMs=7777;
            Reject(()=>Repository.Save(basis,second),"another editor"); Check(Repository.Load().Recipes[0].CraftTimeMs==5555,"Lost update.");
        });
        Run("late SQL failure rolls back parent and children", () => {
            SeedSample(); var basis=Repository.Load(); var draft=CraftingCatalog.Copy(basis); draft.Recipes[0].CraftTimeMs=8888; draft.Recipes[0].Ingredients[0].Quantity=13;
            Exec("ALTER TABLE t_crafting_ingredient ADD CONSTRAINT fixture_failure CHECK(a_count<>13)");
            try { Reject(()=>Repository.Save(basis,draft),"fixture_failure"); Check(Repository.Load().Fingerprint()==basis.Fingerprint(),"Partial save remained."); }
            finally { Exec("ALTER TABLE t_crafting_ingredient DROP CONSTRAINT fixture_failure"); }
        });
        Run("rank reordering preserves stable IDs and swaps unique orders", () => {
            SeedSample(); var basis=Repository.Load(); var draft=CraftingCatalog.Copy(basis);
            (draft.Ranks[0].Order,draft.Ranks[1].Order)=(draft.Ranks[1].Order,draft.Ranks[0].Order);
            (draft.Ranks[0].RequiredMastered,draft.Ranks[1].RequiredMastered)=(draft.Ranks[1].RequiredMastered,draft.Ranks[0].RequiredMastered);
            Repository.Save(basis,draft); Check(Repository.Load().Fingerprint()==draft.Fingerprint(),"Rank swap failed.");
        });
        Run("published recipe and rank IDs cannot be removed", () => {
            SeedSample(); var basis=Repository.Load(); var draft=CraftingCatalog.Copy(basis); draft.Recipes.Clear(); Reject(()=>Repository.Save(basis,draft),"Published IDs");
            draft=CraftingCatalog.Copy(basis); draft.Ranks.Clear(); Reject(()=>Repository.Save(basis,draft),"Published IDs");
        });
        Run("retirement and bonus edits retain all player progression", () => {
            SeedSample(); var basis=Repository.Load(); var draft=CraftingCatalog.Copy(basis); draft.Recipes[0].Enabled=false; draft.Ranks[0].Enabled=false; draft.Recipes[0].Bonuses[0].Amount=77;
            Repository.Save(basis,draft);
            Check(Scalar("SELECT CONCAT(a_exp,':',a_mastered) FROM ep4_db.t_crafting_mastery") == "20:1", "Player XP changed.");
            Check(Scalar("SELECT COUNT(*) FROM ep4_db.t_crafting_master_rank") == "1" && Scalar("SELECT COUNT(*) FROM ep4_db.t_crafting_recipe_unlock") == "1", "Player unlocks changed.");
        });
        Run("fresh item definitions are checked again during save", () => {
            SeedSample(); var basis=Repository.Load(); var draft=CraftingCatalog.Copy(basis); draft.Recipes[0].CraftTimeMs++;
            Exec("UPDATE t_item SET a_enable=0 WHERE a_index=200003");
            try { Reject(()=>Repository.Save(basis,draft),"Teaching item"); }
            finally { Exec("UPDATE t_item SET a_enable=1 WHERE a_index=200003"); }
        });
        Run("item deletion guard blocks every crafting reference role", () => {
            SeedSample();
            foreach(int id in new[]{200001,200002,200003,200004}) Reject(()=>Batch(CraftingItemReferences.DeleteGuard("ep4_data",id)+$"DELETE FROM t_item WHERE a_index={id};"),"Temp_CraftingReferenceGuard");
            Batch(CraftingItemReferences.DeleteGuard("ep4_data",200005)+"DELETE FROM t_item WHERE a_index=200005;");
            Check(Scalar("SELECT COUNT(*) FROM t_item WHERE a_index BETWEEN 200001 AND 200004")=="4","Referenced item deleted.");
        });
        Run("content export lock blocks concurrent edits", () => {
            SeedSample(); Repository.WithLockedCatalog((snapshot,identity)=> {
                Check(identity.EndsWith(":ep4_data"),"Wrong identity.");
                using var other=new MySqlConnection(Connection); other.Open();
                using var timeout=new MySqlCommand("SET innodb_lock_wait_timeout=1",other); timeout.ExecuteNonQuery();
                Reject(()=> { using var update=new MySqlCommand("UPDATE t_crafting_recipe SET a_craft_time_ms=4",other); update.ExecuteNonQuery(); },"Lock wait timeout");
            });
        });
        Run("real interface emblem decodes without a rendering device", () => {
            string path=Path.Combine(game,".local/runtime/ClientEp4-VS2022-x64-Test/Data/Interface/AchievementCategory_2001.tex");
            using var bitmap=CraftingEmblem.Read(path); Check(bitmap.Width==64 && bitmap.Height==64,"Unexpected emblem dimensions."); bitmap.Save(Path.Combine(output,"emblem.png"));
        });
        Run("compressed emblem formats respect frame and mip lengths", () => {
            foreach(uint flags in new uint[]{4,13,5,37}) {
                string path=Path.Combine(output,"fixture-"+flags+".tex");
                using(var stream=File.Create(path)) using(var writer=new BinaryWriter(stream)) {
                    writer.Write("TVER"u8); writer.Write(4); writer.Write("TDAT"u8); writer.Write(flags); writer.Write(4); writer.Write(4); writer.Write(1); writer.Write(0); writer.Write(1); writer.Write("FRMC"u8);
                    bool alpha=flags is 5 or 37; writer.Write(alpha?20:12); writer.Write(alpha?16:8);
                    if(flags==5) writer.Write(ulong.MaxValue);
                    if(flags==37) { writer.Write((byte)255); writer.Write((byte)0); writer.Write(new byte[6]); }
                    writer.Write((ushort)0xf800); writer.Write((ushort)0); writer.Write(0);
                }
                using var bitmap=CraftingEmblem.Read(path); Check(bitmap.GetPixel(2,2)==Color.FromArgb(255,255,0,0),"Wrong DXT pixel for flags "+flags);
            }
        });
        Run("canonical seed exporter writes only the eight content files", () => {
            SeedSample(); string checkout=Path.Combine(output,"export-checkout");
            Directory.CreateDirectory(Path.Combine(checkout,"x64-server/db/seeds/ep4_data")); Directory.CreateDirectory(Path.Combine(checkout,"x64-server/compose"));
            File.Copy(Path.Combine(game,"x64-server/db/export-seed.sh"),Path.Combine(checkout,"x64-server/db/export-seed.sh"),true);
            File.Copy(Path.Combine(Directory.GetCurrentDirectory(),"tests/CraftingVerification/compose.yml"),Path.Combine(checkout,"x64-server/compose/docker-compose.yml"),true);
            foreach(string table in CraftingRepository.Tables) File.Copy(Path.Combine(game,"x64-server/db/seeds/ep4_data",table+".sql"),Path.Combine(checkout,"x64-server/db/seeds/ep4_data",table+".sql"),true);
            var basis=Repository.Load(); CraftingSeedExport.Export(checkout,Connection,"usa",basis.Fingerprint());
            Check(Directory.GetFiles(Path.Combine(checkout,"x64-server/db/seeds"),"*.sql",SearchOption.AllDirectories).Length==8,"Wrong exported scope.");
            Reset(); foreach(string table in CraftingRepository.Tables) Exec(File.ReadAllText(Path.Combine(checkout,"x64-server/db/seeds/ep4_data",table+".sql")));
            Check(Repository.Load().Fingerprint()==basis.Fingerprint(),"Export/import round-trip changed data.");
            var before=File.ReadAllText(Path.Combine(checkout,"x64-server/db/seeds/ep4_data/t_crafting_recipe.sql"));
            Exec("UPDATE t_crafting_recipe SET a_craft_time_ms=7"); Reject(()=>CraftingSeedExport.Export(checkout,Connection,"usa",basis.Fingerprint()),"changed since");
            Check(File.ReadAllText(Path.Combine(checkout,"x64-server/db/seeds/ep4_data/t_crafting_recipe.sql"))==before,"Stale export replaced seeds.");
        });
        Run("offline editor layout, teaching-item browser and navigation", () => {
            SeedSample();
            Exec("INSERT INTO t_item VALUES(200005,'Second recipe book',1,2,4,1,9999,0,0,0) ON DUPLICATE KEY UPDATE a_enable=1;");
            Preview();
        });
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine($"{Results.Count-failures}/{Results.Count} passed"); return failures==0?0:1;
    }
    static void Run(string name,Action action) { try { action(); Results.Add(new {name,passed=true}); Console.WriteLine("PASS: "+name); } catch(Exception e) { failures++; Results.Add(new {name,passed=false,error=e.ToString()}); Console.WriteLine("FAIL: "+name+": "+e); } }
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void Reject(Action action,string expected) { try { action(); } catch(Exception e) { Check(e.ToString().Contains(expected,StringComparison.OrdinalIgnoreCase),"Unexpected error: "+e); return; } throw new Exception("Expected rejection: "+expected); }
    static void Invalid(CraftingCatalog c,string message) => Check(CraftingRules.Validate(c).Any(i=>i.Error && i.Message.Contains(message,StringComparison.OrdinalIgnoreCase)),"Expected validation: "+message);
    static void Exec(string sql) { using var cmd=new MySqlCommand(sql,db); cmd.ExecuteNonQuery(); }
    static string Scalar(string sql) { using var cmd=new MySqlCommand(sql,db); return Convert.ToString(cmd.ExecuteScalar())!; }
    static void Reset() { Exec("SET FOREIGN_KEY_CHECKS=0;"+string.Join(";",CraftingRepository.Tables.Select(t=>"TRUNCATE TABLE "+t))+";SET FOREIGN_KEY_CHECKS=1;"); }
    static void SeedSample() { Reset(); Repository.Save(Repository.Load(),Sample()); }
    static CraftingCatalog Sample() => new() {
        Items=Repository.Load().Items,
        Recipes=[new() { Id=1, Profession=Profession.Cooking, OutputItem=200001, OutputQuantity=2, CraftTimeMs=2500, RequiredSkill=1, NoSkillUp=31, SortOrder=10, RequiresUnlock=true,
            Ingredients=[new() {ItemId=200002,Quantity=3}], Manuals=[new() {ItemId=200003}], MasteryXp=10, MasteryRequired=100,
            Bonuses=[new() {Effect=102,Amount=50}], BonusOutputs=[new() {ItemId=200004,Quantity=1,ChancePercent=12.34m}] }],
        Ranks=[new() {Id=1,Order=1,Name="Master Cook",RequiredMastered=1,Bonuses=[new() {Effect=4,Amount=20}]},new() {Id=2,Order=2,Name="Grandmaster",RequiredMastered=10,Bonuses=[new() {Effect=5,Amount=10}]}]
    };
    static void Batch(string sql)
    {
        var type=typeof(LastChaos_ToolBoxNG.Main).Assembly.GetType("LastChaos_ToolBoxNG.DatabaseWriteBatch")!;
        using var connection=new MySqlConnection(Connection); connection.Open();
        type.GetMethod("Execute",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[connection,sql]);
    }
    static void Preview()
    {
        Application.EnableVisualStyles();
        using var host=new LastChaos_ToolBoxNG.Main(); host.pSettings.WorkLocale="usa";
        using var editor=new CraftingMasteryEditor(host) { ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Location=new(-20000,-20000) };
        var type=typeof(CraftingMasteryEditor);
        type.GetField("repository",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(editor,Repository);
        editor.Show(); var deadline=DateTime.UtcNow.AddSeconds(15);
        while(!(bool)type.GetField("loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)! && DateTime.UtcNow<deadline) { Application.DoEvents(); Thread.Sleep(20); }
        Check((bool)type.GetField("loaded",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!,"UI did not load fixture.");
        var tabs=(TabControl)type.GetField("tabs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        void Capture(string name) { Application.DoEvents(); using var bitmap=new Bitmap(editor.Width,editor.Height); editor.DrawToBitmap(bitmap,new(0,0,bitmap.Width,bitmap.Height)); bitmap.Save(Path.Combine(output,name+".png")); }
        Capture("recipes");
        var detail=(Panel)type.GetField("recipeDetail",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        var details=(TabControl)detail.Controls[0];
        var fields=(FlowLayoutPanel)details.TabPages[0].Controls[0];
        var profession=fields.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<ComboBox>()).Single();
        Check((Profession)profession.SelectedItem! == Profession.Cooking,"Profession dropdown differs from saved recipe.");
        Check(!(bool)type.GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!,"Loading the UI modified the draft.");
        for(int n=1;n<details.TabPages.Count;n++) { details.SelectedIndex=n; Capture("recipe-tab-"+n); }
        tabs.SelectedIndex=1;
        var teachingList=tabs.TabPages[1].Controls.Find("TeachingItemList",true).OfType<DataGridView>().SingleOrDefault();
        Check(teachingList!=null,"Teaching Items must list existing recipe items without entering an ID.");
        Check(teachingList!.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToInt32(r.Cells["Id"].Value)==200003),"The assigned recipe book is missing from the browser.");
        Check(((NumericUpDown)type.GetField("bookItem",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!).Value==200003,"The existing teaching item was not selected automatically.");
        Capture("teaching");
        var bookSearch=tabs.TabPages[1].Controls.Find("TeachingItemSearch",true).OfType<TextBox>().Single();
        var bookFilter=tabs.TabPages[1].Controls.Find("TeachingItemFilter",true).OfType<ComboBox>().Single();
        var bookDetails=(TextBox)type.GetField("bookRecipes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        var bookId=(NumericUpDown)type.GetField("bookItem",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        Check(bookDetails.Text.Contains("Healing meal"),"Selecting a teaching item does not show its recipes.");
        bookSearch.Text="SECOND RECIPE BOOK"; bookFilter.SelectedIndex=2;
        Check(teachingList.Rows.Count==1 && bookId.Value==200005 && Convert.ToInt32(teachingList.Rows[0].Cells["RecipeCount"].Value)==0,"Unassigned manual search is incorrect.");
        Check(bookDetails.Text.StartsWith("No recipes assigned"),"An unassigned manual has a blank detail pane.");
        bookId.Value=200003;
        Check(bookSearch.Text.Length==0 && bookFilter.SelectedIndex==0 && Convert.ToInt32(teachingList.CurrentRow!.Cells["Id"].Value)==200003,"ID navigation did not reveal a filtered-out manual.");
        bookSearch.Text="no-such-recipe-book";
        Check(teachingList.Rows.Count==0 && bookId.Value==0 && bookDetails.Text.Contains("No recipe items match"),"Empty search retained a stale selection or a blank pane.");
        bookSearch.Clear(); bookFilter.SelectedIndex=1;
        Check(teachingList.Rows.Count==1 && bookId.Value==200003,"Assigned filter did not restore the existing manual.");
        Check(!(bool)type.GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!,"Browsing manuals modified the draft.");

        // Edit the real Learning grid, then verify the browser updates its
        // assignment counts and the normal Save action persists the change.
        tabs.SelectedIndex=0; details.SelectedIndex=2;
        DataGridView FindGrid(Control parent) => parent is DataGridView grid ? grid : parent.Controls.Cast<Control>().SelectMany(Descendants).OfType<DataGridView>().First();
        var manualGrid=FindGrid(details.TabPages[2]);
        manualGrid.CurrentCell=manualGrid.Rows[0].Cells["ItemId"]; manualGrid.BeginEdit(true);
        var cellEditor=manualGrid.EditingControl as DataGridViewTextBoxEditingControl ?? throw new Exception("Manual cell did not enter edit mode.");
        cellEditor.Text="200005";
        Check(manualGrid.EndEdit(),"Manual ID edit was rejected.");
        tabs.SelectedIndex=1; bookFilter.SelectedIndex=0; bookFilter.SelectedIndex=1;
        Check(teachingList.Rows.Count==1 && bookId.Value==200005 && Convert.ToInt32(teachingList.Rows[0].Cells["RecipeCount"].Value)==1,"The manual browser did not follow draft recipe assignments.");
        var save=(Task)type.GetMethod("SaveAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null)!;
        deadline=DateTime.UtcNow.AddSeconds(15);
        while(!save.IsCompleted && DateTime.UtcNow<deadline) { Application.DoEvents(); Thread.Sleep(20); }
        Check(save.IsCompletedSuccessfully && Repository.Load().Recipes[0].Manuals.Single().ItemId==200005,"The edited teaching association was not saved.");
        tabs.SelectedIndex=2; Capture("ranks"); editor.Close();
    }
    static IEnumerable<Control> Descendants(Control parent)
    {
        yield return parent;
        foreach(Control child in parent.Controls) foreach(var descendant in Descendants(child)) yield return descendant;
    }
}
