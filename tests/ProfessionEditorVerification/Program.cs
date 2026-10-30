using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Text.Json;
using LastChaos_ToolBoxNG;
using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using LastChaos_ToolBoxNG.ContentRecording;

static class Program
{
    static readonly Type EditorType = typeof(CraftingMasteryEditor);
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string description)
    { if (!value) throw new InvalidOperationException(description); checks++; Console.WriteLine("PASS " + description); }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, Private)!;
        if (args.Length == 0 && method.GetParameters().Length > 0) args = method.GetParameters().Select(p => p.DefaultValue).ToArray();
        try { return method.Invoke(owner, args); } catch (TargetInvocationException e) { throw e.InnerException!; }
    }
    static IEnumerable<Control> All(Control control)
    { foreach (Control child in control.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
    sealed class Snapshot
    {
        public Dictionary<string, List<Dictionary<string, string>>> Tables { get; set; } = [];
        public List<Dictionary<string, string>> Items { get; set; } = [];
        public List<Dictionary<string, string>> Npcs { get; set; } = [];
        public List<Dictionary<string, string>> Zones { get; set; } = [];
        public Dictionary<string,List<Dictionary<string,string>>> InfusionMetadata { get; set; } = [];
    }
    static int N(Dictionary<string, string> row, string field) => int.Parse(row[field], System.Globalization.CultureInfo.InvariantCulture);
    static CraftingCatalog Load(string path)
    {
        var raw = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path))!;
        var c = new CraftingCatalog();
        foreach (var table in ProfessionSchema.Tables)
            c.Professions.Tables[table.Name] = raw.Tables[table.Name].Select(r => new ProfessionRow { Values = r }).ToList();
        foreach (var r in raw.Items)
        { int id = N(r, "a_index"); c.Items[id] = new(id, r["a_name_usa"], N(r, "a_enable") == 1, N(r, "a_type_idx"), N(r, "a_subtype_idx"), long.Parse(r["a_flag"]), N(r, "a_weight"), N(r, "a_texture_id"), N(r, "a_texture_row"), N(r, "a_texture_col"), false, N(r, "a_wearing")); }
        foreach (var r in raw.Npcs) { int id = N(r, "a_index"); c.Professions.Npcs[id] = new(id, r["a_name"], long.Parse(r["a_flag"]), N(r, "a_enable") == 1); }
        foreach (var r in raw.Zones) c.Professions.Zones[N(r, "a_zone_index")] = r["a_name"];
        c.Professions.Infusions=InfusionMetadata.FromRows(raw.InfusionMetadata,raw.InfusionMetadata["t_skill"].ToDictionary(r=>N(r,"a_index"),r=>r["a_name_usa"]));
        c.SkillCap = N(raw.Tables["t_crafting_settings"].Single(), "a_skill_cap");
        var mastery = raw.Tables["t_crafting_mastery"].ToDictionary(r => N(r, "a_recipe_index"));
        foreach (var r in raw.Tables["t_crafting_recipe"])
        {
            int id = N(r, "a_index"); var m = mastery[id];
            c.Recipes.Add(new() { Id = id, Profession = (Profession)N(r, "a_category"), OutputItem = N(r, "a_output_item"), OutputQuantity = N(r, "a_output_count"),
                CraftTimeMs = N(r, "a_craft_time_ms"), RequiredSkill = N(r, "a_required_skill"), NoSkillUp = N(r, "a_no_skill_up"), SortOrder = N(r, "a_sort_order"), Enabled = N(r, "a_enable") == 1,
                RequiresUnlock = N(r, "a_requires_unlock") == 1, MasteryXp = N(m, "a_exp_per_craft"), BronzeRequired = N(m, "a_bronze_xp_required"), SilverRequired = N(m, "a_silver_xp_required"), MasteryRequired = N(m, "a_exp_required") });
        }
        foreach (var r in raw.Tables["t_crafting_ingredient"]) c.Recipes.Single(x => x.Id == N(r, "a_recipe_index")).Ingredients.Add(new() { ItemId = N(r, "a_item_index"), Quantity = N(r, "a_count") });
        foreach (var r in raw.Tables["t_crafting_manual"]) c.Recipes.Single(x => x.Id == N(r, "a_recipe_index")).Manuals.Add(new() { ItemId = N(r, "a_manual_item") });
        foreach (var r in raw.Tables["t_crafting_bonus_output"]) c.Recipes.Single(x => x.Id == N(r, "a_recipe_index")).BonusOutputs.Add(new() { ItemId = N(r, "a_item_index"), Quantity = N(r, "a_count"), ChancePercent = N(r, "a_chance") / 100m });
        foreach (var r in raw.Tables["t_crafting_mastery_bonus"]) c.Recipes.Single(x => x.Id == N(r, "a_recipe_index")).Bonuses.Add(new() { Rank = N(r, "a_rank"), Effect = N(r, "a_option_type"), Amount = N(r, "a_value") });
        foreach (var r in raw.Tables["t_gathering_material"]) c.Gathering.Add(new() { ItemId = N(r, "a_item_index"), Profession = N(r, "a_profession"), MasteryXp = N(r, "a_exp_per_item"), BronzeRequired = N(r, "a_bronze_xp_required"), SilverRequired = N(r, "a_silver_xp_required"), MasteryRequired = N(r, "a_exp_required") });
        foreach (var r in raw.Tables["t_gathering_material_bonus"]) c.Gathering.Single(x => x.ItemId == N(r, "a_item_index")).Bonuses.Add(new() { Rank = N(r, "a_rank"), Effect = N(r, "a_option_type"), Amount = N(r, "a_value") });
        foreach (var r in raw.Tables["t_crafting_master_rank"]) c.Ranks.Add(new() { Id = N(r, "a_index"), Order = N(r, "a_order"), Name = r["a_name"], RequiredMastered = N(r, "a_required_mastered"), Emblem = r["a_emblem"], Enabled = N(r, "a_enable") == 1 });
        foreach (var r in raw.Tables["t_crafting_master_rank_bonus"]) c.Ranks.Single(x => x.Id == N(r, "a_rank_index")).Bonuses.Add(new() { Effect = N(r, "a_option_type"), Amount = N(r, "a_value") });
        c.UpdateNames(); return c;
    }

    [STAThread] static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: ProfessionEditorVerification <game-checkout> <seed-snapshot.json> <output-directory>"); return 2; }
        string game = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.json"), "{\"passed\":false,\"stage\":\"running\",\"liveDatabaseChanged\":false}");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var c = Load(args[1]); var initial = c.Fingerprint();
        Check(!InfusionRules.Validate(c).Any(i=>i.Error),"authored infusion catalogue matches supported original potion effects");
        Check(c.Professions.Rows(InfusionSchema.Effects).Count==4&&c.Professions.Rows(InfusionSchema.Offers).All(r=>r.Int("a_required_count")==250),"four initial offers each consume 250 original potions");
        Check(InfusionSchema.Tables.All(t=>ContentRecordingPolicy.AllowlistedTables.Any(a=>a.Key=="ep4_data/"+t.Name))&&!ContentRecordingPolicy.AllowlistedTables.Any(t=>t.Table=="t_alchemy_infusion_unlock"),"content recording includes authored infusion tables and excludes player unlocks");
        ContentRecordingPolicy.ValidateCheckout(game);Check(ContentRecordingPolicy.AllowlistedTables.Count==121,"recording policy exactly matches all 121 authored seed tables");
        var copied=CraftingCatalog.Copy(c);Check(copied.Fingerprint()==initial&&!JsonSerializer.Serialize(copied).Contains("MagicIds"),"all authored infusion rows round-trip exactly without read-only metadata or progression");
        CraftingCatalog Draft() {var draft=CraftingCatalog.Copy(c);draft.Items=c.Items;draft.Professions.Infusions=c.Professions.Infusions;return draft;}
        void Deny(Action action,string label){try{action();}catch(InvalidOperationException){Check(true,label);return;}throw new Exception(label);}
        foreach(string column in new[]{"a_item_index","a_skill_index","a_skill_level"}) {
            var draft=Draft();draft.Professions.Rows(InfusionSchema.Effects)[0].Set(column,999999);
            Deny(()=>ProfessionStorage.ValidatePublished(c.Professions,draft.Professions),"published effect identity cannot change: "+column);
        }
        foreach(string table in new[]{InfusionSchema.Effects,InfusionSchema.Offers,InfusionSchema.Facility}) {
            var draft=Draft();draft.Professions.Rows(table).RemoveAt(0);Deny(()=>ProfessionStorage.ValidatePublished(c.Professions,draft.Professions),"published rows cannot disappear: "+table);
        }
        var offerDraft=Draft();offerDraft.Professions.Rows(InfusionSchema.Offers)[0].Set("a_required_count",300);offerDraft.Professions.Rows(InfusionSchema.Offers)[0].Set("a_enabled",0);
        ProfessionStorage.ValidatePublished(c.Professions,offerDraft.Professions);Check(!InfusionRules.Validate(offerDraft).Any(i=>i.Error),"offer cost and availability remain editable without changing acquired effect identity");
        foreach(string path in new[]{@"C:\outside.smc",@"Data\..\outside.smc",@"Data\World\bad.txt",@"Data\World\äm.smc"}) {
            var draft=Draft();draft.Professions.Rows(InfusionSchema.Facility)[0].Set("a_model",path);Check(InfusionRules.Validate(draft).Any(i=>i.Error),"unsafe station model path rejected: "+path);
        }
        var wrongSkill=Draft();wrongSkill.Professions.Rows(InfusionSchema.Effects)[0].Set("a_skill_index",504);Check(InfusionRules.Validate(wrongSkill).Any(i=>i.Error),"mismatched original potion skill is rejected");
        foreach(int flag in new[]{4,64,128,256,512,1024,2048,4096,131072}) {
            var raw=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(args[1]))!.InfusionMetadata;var skill=raw["t_skill"].Single(r=>N(r,"a_index")==505);skill["a_flag"]=(N(skill,"a_flag")|flag).ToString();
            Check(!InfusionMetadata.FromRows(raw).Potions[2846].Supported,"unsupported skill lifecycle flag rejected: "+flag);
        }
        var highLevelRaw=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(args[1]))!.InfusionMetadata;
        highLevelRaw["t_item"].Single(r=>N(r,"a_index")==2846)["a_num_1"]="128";
        highLevelRaw["t_skill"].Single(r=>N(r,"a_index")==505)["a_maxLevel"]="255";
        var highLevel=new Dictionary<string,string>(highLevelRaw["t_skilllevel"].Single(r=>N(r,"a_index")==505&&N(r,"a_level")==1));highLevel["a_level"]="128";highLevelRaw["t_skilllevel"].Add(highLevel);
        Check(!InfusionMetadata.FromRows(highLevelRaw).Potions[2846].Supported,"legacy signed skill-level boundary rejects 128 even when the prototype permits it");
        using var host = new LastChaos_ToolBoxNG.Main(); host.pSettings.WorkLocale = "usa";
        host.pSettings.ClientPath = Environment.GetEnvironmentVariable("LC_CLIENT_RUNTIME_ROOT") ?? Path.Combine(game, ".local/runtime/ClientEp4-VS2022-x64-Test");
        host.pSettings.DBHost = "invalid.offline.fixture"; host.pSettings.DBData = "not_a_database";
        // The real NPC picker uses this already-loaded metadata and never opens a connection.
        host.pTables.NPCTable = new DataTable();
        foreach (var name in new[] { "a_index", "a_level", "a_hp", "a_name_usa", "a_descr_usa" }) host.pTables.NPCTable.Columns.Add(name);
        foreach (var npc in c.Professions.Npcs.Values) host.pTables.NPCTable.Rows.Add(npc.Id, 1, 100, npc.Name, "Offline source fixture");
        using var editor = new CraftingMasteryEditor(host) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-20000, -20000) };
        Set(editor, "catalog", c); Set(editor, "original", CraftingCatalog.Copy(c)); Set(editor, "loaded", true);
        Set(editor, "busy", true); // Suppress the real database load for this offline fixture.
        editor.Show(); Set(editor, "busy", false);
        foreach (string method in new[] { "FilterRecipes", "RefreshRanks", "RefreshTeachingItems", "RefreshGathering", "RefreshProfessionPages", "RefreshHome" }) Call(editor, method);
        var tabs = Field<TabControl>(editor, "tabs"); var nav = Field<TreeView>(editor, "navigation");
        Check(tabs.TabPages.Cast<TabPage>().Count(p => p.Name.StartsWith("Profession")) == 5, "all four gathering pages and Infusions exist");
        void Navigate(int profession, string table)
        {
            nav.SelectedNode = nav.Nodes.Find(profession switch { 1 => "Mining", 2 => "Herbalism", 3 => "Fishing", 5 => "Infusions", _ => "Wave Absorption" }, true).Single();
            Call(editor, "NavigateProfessionPage", profession, table); Application.DoEvents();
        }
        Control Detail(int profession, string table) => editor.Controls.Find($"ProfessionDetail_{profession}_{table}", true).Single();
        void Capture(string name, Size size)
        { editor.Size = size; Application.DoEvents(); editor.PerformLayout(); Application.DoEvents(); using var image = new Bitmap(editor.Width, editor.Height); editor.DrawToBitmap(image, new(0, 0, image.Width, image.Height)); image.Save(Path.Combine(output, name + ".png")); }
        foreach (var size in new[] { new Size(1420, 900), new Size(1100, 720) })
        {
            string suffix = $"-{size.Width}x{size.Height}"; nav.SelectedNode = nav.Nodes.Find("Overview", true).Single(); Capture("overview" + suffix, size);
            foreach (var sample in new[] { (1, "t_gathering_node", "mining"), (2, "t_herb_population", "herbalism"), (3, "t_fishing_water", "fishing"), (4, "t_gathering_node", "wave"), (2, "t_gathering_settings", "herbalism-rules"), (5,InfusionSchema.Offers,"infusion-offers"),(5,InfusionSchema.Effects,"infusion-effects"),(5,InfusionSchema.Facility,"infusion-station") })
            {
                Navigate(sample.Item1, sample.Item2); Capture(sample.Item3 + suffix, size);
                var panel = (Panel)Detail(sample.Item1, sample.Item2);
                Check(!panel.HorizontalScroll.Visible && panel.RectangleToScreen(panel.ClientRectangle).Right <= editor.RectangleToScreen(editor.ClientRectangle).Right,
                    $"{sample.Item3} details fit the viewport at {size.Width}×{size.Height}");
            }
            nav.SelectedNode = nav.Nodes.Find("Resources & mastery", true).Single(); Capture("resource-mastery" + suffix, size);
        }
        Navigate(1, "t_gathering_node"); var mining = Detail(1, "t_gathering_node");
        Check(((NumericUpDown)mining.Controls.Find("a_timeout_ms", true).Single()).Value == c.Professions.Rows("t_gathering_node").First(r => r.Int("a_profession") == 1).Int("a_timeout_ms") / 1000m, "milliseconds display as readable seconds");
        Check(!mining.Controls.Find("a_index", true).Single().Enabled, "permanent source identities are read-only");
        var profile = (ComboBox)mining.Controls.Find("a_interaction_profile", true).Single();
        Check(profile.SelectedItem != null && Convert.ToInt32(profile.SelectedItem.GetType().GetProperty("Id")!.GetValue(profile.SelectedItem)) == c.Professions.Rows("t_gathering_node").First(r => r.Int("a_profession") == 1).Int("a_interaction_profile"),
            "profile dropdown displays the saved reference instead of a false None selection");
        Check(All(mining).OfType<Label>().Any(l => l.Text == "Mining"), "profession is named rather than an unexplained numeric ID");
        Check(!mining.Controls.Find("a_rhythm_beats", true).Single().Visible, "Mining does not present inactive Wave settings as active controls");
        Navigate(4, "t_gathering_node"); var wave = Detail(4, "t_gathering_node");
        Check(!wave.Controls.Find("a_interaction_profile", true).Single().Visible, "Wave does not expose inactive Mining geometry as an active setting");
        Navigate(2, "t_gathering_settings"); var herb = Detail(2, "t_gathering_settings");
        Check(!herb.Controls.Find("a_passive_speed_per_level_bp", true).Single().Visible, "Herbalism has no effective passive controls");
        Navigate(2, "t_herb_loot");
        var filter = (ComboBox)editor.Controls.Find("ProfessionParent_2_t_herb_loot", true).Single();
        int parent = c.Professions.Rows("t_herb_population").First().Int("a_index"); filter.SelectedValue = parent;
        var drops = (DataGridView)editor.Controls.Find("ProfessionRows_2_t_herb_loot", true).Single();
        Check(drops.Rows.Count == c.Professions.Rows("t_herb_loot").Count(r => r.Int("a_population_index") == parent), "named parent filter shows only that source's drops");
        Check(filter.Text.Contains(c.Professions.Npcs[c.Professions.Rows("t_herb_population").First().Int("a_npc_index")].Name), "source filter includes a readable NPC name");
        tabs.SelectedIndex = 0; Application.DoEvents();
        var recipeGrid = Field<DataGridView>(editor, "recipes"); recipeGrid.CurrentCell = recipeGrid.Rows[1].Cells[0];
        Check(Field<Recipe>(editor, "selectedRecipe").Id == ((Recipe)recipeGrid.Rows[1].DataBoundItem!).Id, "recipe details follow row-one selection without lag");
        tabs.SelectedIndex = 2; Application.DoEvents();
        var rankGrid = Field<DataGridView>(editor, "ranks"); rankGrid.CurrentCell = rankGrid.Rows[1].Cells[0];
        Check(Field<MastersRank>(editor, "selectedRank").Id == ((MastersRank)rankGrid.Rows[1].DataBoundItem!).Id, "Artisan Rank details follow row-one selection without lag");
        tabs.SelectedIndex = 3; Application.DoEvents();
        var resourceGrid = Field<DataGridView>(editor, "gatheringRows"); resourceGrid.CurrentCell = resourceGrid.Rows[1].Cells[0];
        Check(Field<GatheringResource>(editor, "selectedGathering").ItemId == ((GatheringResource)resourceGrid.Rows[1].DataBoundItem!).ItemId, "resource mastery details follow row-one selection without lag");
        tabs.SelectedIndex = 1; Application.DoEvents();
        var books = (DataGridView)editor.Controls.Find("TeachingItemList", true).Single(); books.CurrentCell = books.Rows[1].Cells[0];
        int book = (int)books.Rows[1].DataBoundItem!.GetType().GetProperty("Id")!.GetValue(books.Rows[1].DataBoundItem)!;
        Check(Field<NumericUpDown>(editor, "bookItem").Value == book, "teaching-item assignment follows row-one selection without lag");
        Check(c.Fingerprint() == initial && !Field<bool>(editor, "dirty"), "browsing every page and resizing never edits content");
        Navigate(1, "t_gathering_node");
        var browsers = Field<object>(editor, "professionBrowsers") as System.Collections.IDictionary;
        object browser = browsers![(1, "t_gathering_node")]!;
        bool cancelled = false;
        using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
        {
            timer.Tick += (_, _) => { foreach (var picker in Application.OpenForms.Cast<Form>().OfType<NPCPicker>().ToArray()) { cancelled = true; picker.DialogResult = DialogResult.Cancel; picker.Close(); timer.Stop(); } };
            timer.Start(); Call(editor, "NewProfessionEntry", browser, false);
        }
        Check(cancelled && c.Fingerprint() == initial && !Field<bool>(editor, "dirty"), "cancelling the real New-source NPC picker leaves the draft unchanged");
        Navigate(2, "t_herb_loot");
        object lootBrowser = browsers[(2, "t_herb_loot")]!;
        drops.CurrentCell = drops.Rows[1].Cells[0];
        var selectedDrop = (ProfessionRow)lootBrowser.GetType().GetField("Selected")!.GetValue(lootBrowser)!;
        Check(selectedDrop.Int("a_slot") == 1, "row details and actions follow the actual current cell rather than the prior row");
        int selectedItem = selectedDrop.Int("a_item_index");
        Call(editor, "MoveProfessionEntry", lootBrowser, -1);
        Check(selectedDrop.Int("a_slot") == 0 && c.Professions.Rows("t_herb_loot").Single(r => r.Int("a_population_index") == parent && r.Int("a_slot") == 0).Int("a_item_index") == selectedItem,
            "normal child ordering preserves the selected outcome and contiguous slot identity");
        Call(editor, "MoveProfessionEntry", lootBrowser, 1);
        if (c.Fingerprint() != initial)
        {
            File.WriteAllText(Path.Combine(output, "order-before.json"), initial);
            File.WriteAllText(Path.Combine(output, "order-after.json"), c.Fingerprint());
            Console.WriteLine("Selected outcome now: " + selectedDrop.Int("a_slot") + "; browser selected: " + ((ProfessionRow)lootBrowser.GetType().GetField("Selected")!.GetValue(lootBrowser)!).Int("a_slot"));
        }
        Check(c.Fingerprint() == initial, "reversing child ordering restores every drop and parent reference");
        Navigate(1, "t_gathering_node");
        Call(editor, "ToggleProfessionEntry", browser); Call(editor, "ToggleProfessionEntry", browser);
        Check(c.Fingerprint() == initial, "retire/enable changes availability without changing a source identity or its children");
        var duration = (NumericUpDown)Detail(1, "t_gathering_node").Controls.Find("a_timeout_ms", true).Single();
        decimal old = duration.Value; duration.Value = old + 1;
        Check(Field<bool>(editor, "dirty") && c.Professions.Rows("t_gathering_node").First(r => r.Int("a_profession") == 1).Decimal("a_timeout_ms") == (old + 1) * 1000, "normal field editing updates only the draft in stored units");
        duration.Value = old;
        Check(c.Fingerprint() == initial, "restoring the displayed value restores the exact catalogue snapshot");
        Navigate(5,InfusionSchema.Effects);var effectsPanel=Detail(5,InfusionSchema.Effects);
        Check(new[]{"a_index","a_item_index","a_skill_index","a_skill_level"}.All(name=>!effectsPanel.Controls.Find(name,true).Single().Enabled),"published potion, skill, level and effect ID are locked in the actual editor");
        Navigate(5,InfusionSchema.Offers);var offersPanel=Detail(5,InfusionSchema.Offers);
        var cost=(NumericUpDown)offersPanel.Controls.Find("a_required_count",true).Single();Check(cost.Value==250,"offer editor shows a readable 250-potion cost");cost.Value=251;
        Check(c.Professions.Rows(InfusionSchema.Offers)[0].Int("a_required_count")==251,"actual offer control edits only the draft potion quantity");cost.Value=250;
        var enabled=(CheckBox)offersPanel.Controls.Find("a_enabled",true).Single();enabled.Checked=false;Check(!c.Professions.Rows(InfusionSchema.Offers)[0].Enabled,"retiring an offer updates its actual enabled field");enabled.Checked=true;
        Navigate(5,InfusionSchema.Facility);var stationPanel=Detail(5,InfusionSchema.Facility);
        var radius=(NumericUpDown)stationPanel.Controls.Find("a_use_radius",true).Single();Check(radius.Value==4&&radius.DecimalPlaces==2,"station range displays metres rather than stored hundredths");radius.Value=4.25m;
        Check(c.Professions.Rows(InfusionSchema.Facility)[0].Int("a_use_radius")==425,"station metres convert exactly to native integer hundredths");radius.Value=4;
        Check(c.Fingerprint()==initial,"all actual infusion edits restore exact authored content");
        Call(editor,"NavigateInfusionEffect",2);var effectRows=(DataGridView)editor.Controls.Find("ProfessionRows_5_t_alchemy_effect",true).Single();
        Check(effectRows.CurrentRow!.Cells["Identity"].Value?.ToString()=="2","opening an offer effect selects its associated permanent definition");
        var newEffect=CraftingCatalog.Copy(c.Professions.Rows(InfusionSchema.Effects)[0]);newEffect.Set("a_index",100);c.Professions.Rows(InfusionSchema.Effects).Add(newEffect);Call(editor,"NavigateInfusionEffect",100);
        effectsPanel=Detail(5,InfusionSchema.Effects);var newPotion=(NumericUpDown)effectsPanel.Controls.Find("a_item_index",true).Single();
        Check(newPotion.Enabled&&!effectsPanel.Controls.Find("a_skill_index",true).Single().Enabled&&!effectsPanel.Controls.Find("a_skill_level",true).Single().Enabled,"new effects allow an item selection while keeping derived skill fields read-only");
        newPotion.Value=2845;Check(newEffect.Int("a_skill_index")==504&&newEffect.Int("a_skill_level")==1,"changing the actual original-potion control derives its exact skill and level");
        c.Professions.Rows(InfusionSchema.Effects).Remove(newEffect);Call(editor,"RefreshProfessionPages");Check(c.Fingerprint()==initial,"discarding a new effect restores all existing content exactly");
        Set(editor, "dirty", false); editor.Close();
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, checks, liveDatabaseChanged = false, gameClientLaunched = false }));
        Console.WriteLine(checks + " profession UI checks passed"); return 0;
    }
}
