using LastChaos_ToolBoxNG.Crafting;
using LastChaos_ToolBoxNG.Professions;
using System.Globalization;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private sealed class ProfessionBrowser
    {
        public required int Profession;
        public required ProfessionTable Table;
        public required TabPage Page;
        public required DataGridView List;
        public required Panel Detail;
        public required TextBox Search;
        public required ComboBox Parent;
        public required Label Validation;
        public ProfessionRow? Selected;
        public bool Binding;
        public bool Sizing;
    }
    private sealed record ProfessionChoice(int Id, string Name)
    { public override string ToString() => Name; }
    private sealed class ProfessionListEntry(ProfessionRow row, string identity, string name, string summary, string state)
    {
        public ProfessionRow Row { get; } = row;
        public string Identity { get; set; } = identity;
        public string Name { get; set; } = name;
        public string Summary { get; set; } = summary;
        public string State { get; set; } = state;
    }
    private readonly Dictionary<int, TabPage> professionPages = [];
    private readonly Dictionary<(int, string), ProfessionBrowser> professionBrowsers = [];
    private bool refreshingProfessionPages;

    private static string GatheringProfessionName(int id) => id switch
    { 1 => "Mining", 2 => "Herbalism", 3 => "Fishing", 4 => "Wave Absorption", 5 => "Infusions", 6 => "Transmutation", _ => "Gathering" };

    private void BuildProfessionPages()
    {
        var definitions = new (int Id, (string Table, string Title)[] Sections)[] {
            (1, [("t_gathering_settings","Skill rules"),("t_gathering_node","Ore sources"),("t_gathering_node_loot","Passive drops"),("t_gathering_tool","Tools"),("t_gathering_profile","Model profiles")]),
            (2, [("t_gathering_settings","Skill rules"),("t_herb_population","Populations"),("t_herb_spawn_point","Candidate sites"),("t_herb_loot","Harvest drops")]),
            (3, [("t_fishing_settings","Skill and rod"),("t_fishing_water","Fishable waters"),("t_fishing_water_vertex","Water boundaries"),("t_fishing_loot","Catches")]),
            (4, [("t_gathering_settings","Skill rules"),("t_gathering_node","Wave sources"),("t_gathering_node_loot","Resource drops"),("t_gathering_tool","Tools")]),
            (5, [(InfusionSchema.Offers,"Brewing recipes"),(InfusionSchema.Ingredients,"Extra ingredients"),(InfusionSchema.Consumables,"Permanent potions"),(InfusionSchema.Effects,"Permanent effects"),(InfusionSchema.Facility,"Station"),(InfusionSchema.ConstructionCosts,"Construction costs")]),
            (6, [(TransmutationSchema.Offers,"Upgrading items"),(TransmutationSchema.Settings,"Reservoir and value"),(TransmutationSchema.ItemRules,"Equipment exceptions"),(TransmutationSchema.Facility,"Crucible"),(TransmutationSchema.ConstructionCosts,"Construction costs")])
        };
        foreach (var definition in definitions)
        {
            var page = new TabPage(GatheringProfessionName(definition.Id)) { Name = "Profession" + definition.Id };
            professionPages.Add(definition.Id, page); tabs.TabPages.Add(page);
            var sections = new TabControl { Dock = DockStyle.Fill };
            page.Controls.Add(sections);
            foreach (var section in definition.Sections)
            {
                var child = new TabPage(section.Title) { Name = section.Table };
                sections.TabPages.Add(child);
                BuildProfessionBrowser(definition.Id, ProfessionSchema.Find(section.Table), child);
            }
        }
    }

    private void BuildProfessionBrowser(int professionId, ProfessionTable table, TabPage page)
    {
        var split = new SplitContainer { Size = new(1280, 700), Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1, Panel1MinSize = 300, Panel2MinSize = 420, SplitterDistance = 360 };
        page.Controls.Add(split);
        var list = Grid(true); list.MultiSelect = false; list.Name = $"ProfessionRows_{professionId}_{table.Name}";
        Column(list, "Identity", "ID", 50); Column(list, "Name", "Entry", 170);
        Column(list, "Summary", "Details", 120); Column(list, "State", "State", 50);
        var searchBox = new TextBox { Name = $"ProfessionSearch_{professionId}_{table.Name}", Width = 220, PlaceholderText = "Search name or ID" };
        var parentBox = new ComboBox { Name = $"ProfessionParent_{professionId}_{table.Name}", Width = 285, DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Name", ValueMember = "Id" };
        var filters = Flow(); filters.Controls.AddRange([searchBox, parentBox]);
        var detail = new Panel { Name = $"ProfessionDetail_{professionId}_{table.Name}", Dock = DockStyle.Fill, AutoScroll = true };
        var validation = new Label { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new(850, 0), Padding = new(8) };
        var browser = new ProfessionBrowser { Profession = professionId, Table = table, Page = page,
            List = list, Detail = detail, Search = searchBox, Parent = parentBox, Validation = validation };
        professionBrowsers.Add((professionId, table.Name), browser);
        var tools = Flow();
        bool singleton = table.Name is "t_gathering_settings" or "t_fishing_settings" or InfusionSchema.Facility or TransmutationSchema.Facility or TransmutationSchema.Settings;
        if (!singleton)
        {
            tools.Controls.AddRange([Button("New…", () => NewProfessionEntry(browser, false)),
                Button("Duplicate…", () => NewProfessionEntry(browser, true))]);
            if (table.Fields.Any(f => f.Column is "a_enable" or "a_enabled"))
                tools.Controls.Add(Button(table.Name==TransmutationSchema.ItemRules?"Exclude / Allow":"Retire / Enable", () => ToggleProfessionEntry(browser)));
            tools.Controls.Add(Button(IsDurableProfessionTable(table.Name) ? "Discard new" : "Remove", () => RemoveProfessionEntry(browser)));
        }
        if (table.Fields.Any(f => f.Column is "a_slot" or "a_vertex_index"))
            tools.Controls.AddRange([Button("Move up", () => MoveProfessionEntry(browser, -1)), Button("Move down", () => MoveProfessionEntry(browser, 1))]);
        if (table.Name is "t_herb_population" or "t_herb_spawn_point" or "t_fishing_water" or "t_fishing_water_vertex")
            tools.Controls.Add(Button("Edit on map…", () => EditProfessionMap(browser)));
        if (table.Name == "t_gathering_profile")
            tools.Controls.Add(Button("Import model profile…", () => ImportProfessionProfile(browser)));
        if (professionId < 5) tools.Controls.Add(Button("Balance / timing preview…", () => { if (loaded) RunProfessionAction(() => ProfessionPreviews.Show(this, catalog, ProfessionPreviewSource(browser))); }));
        split.Panel1.Controls.Add(Vertical(filters, tools, list));
        split.Panel2.Controls.Add(Vertical(detail, validation));
        // Detail is the expanding element, while the validation block remains below it.
        if (split.Panel2.Controls[0] is TableLayoutPanel layout)
        { layout.RowStyles[0] = new(SizeType.Percent, 100); layout.RowStyles[1] = new(SizeType.AutoSize);
            if (layout.ColumnStyles.Count == 0) layout.ColumnStyles.Add(new(SizeType.Percent, 100)); }
        if (split.Panel1.Controls[0] is TableLayoutPanel left && left.ColumnStyles.Count == 0) left.ColumnStyles.Add(new(SizeType.Percent, 100));
        searchBox.TextChanged += (_, _) => RefreshProfessionBrowser(browser);
        parentBox.SelectedIndexChanged += (_, _) => { if (!browser.Binding) RefreshProfessionBrowser(browser); };
        // SelectionChanged can run while CurrentRow still points at the previous
        // cell. Bind details only after the current cell has actually changed.
        list.CurrentCellChanged += (_, _) => {
            if (browser.Binding || !loaded) return;
            browser.Selected = list.CurrentRow?.DataBoundItem is ProfessionListEntry entry ? entry.Row : null;
            ShowProfessionEntry(browser);
        };
        detail.SizeChanged += (_, _) => SizeProfessionFields(browser);
    }

    private void NavigateProfession(int professionId) => NavigateProfessionPage(professionId);
    private void NavigateProfessionPage(int professionId, string? table = null)
    {
        if (!professionPages.TryGetValue(professionId, out var page)) return;
        tabs.SelectedTab = page;
        if (table != null && professionBrowsers.TryGetValue((professionId, table), out var browser)
            && browser.Page.Parent is TabControl sections) sections.SelectedTab = browser.Page;
    }

    private void RefreshProfessionPages()
    {
        if (!loaded || refreshingProfessionPages) return;
        refreshingProfessionPages = true;
        try
        {
            foreach (var browser in professionBrowsers.Values)
            { RefreshProfessionParents(browser); RefreshProfessionBrowser(browser); }
        }
        finally { refreshingProfessionPages = false; }
    }

    private IEnumerable<ProfessionRow> ProfessionRows(ProfessionBrowser browser)
    {
        var rows = catalog.Professions.Rows(browser.Table.Name).AsEnumerable();
        return browser.Table.Name switch {
            "t_gathering_settings" => rows.Where(r => r.Int("a_index") == browser.Profession),
            "t_gathering_node" or "t_gathering_tool" => rows.Where(r => r.Int("a_profession") == browser.Profession),
            "t_gathering_node_loot" => rows.Where(r => catalog.Professions.Rows("t_gathering_node")
                .Any(n => n.Int("a_index") == r.Int("a_node_index") && n.Int("a_profession") == browser.Profession)),
            _ => rows
        };
    }

    private void RefreshProfessionParents(ProfessionBrowser browser)
    {
        var parent = browser.Table.Fields.FirstOrDefault(f => f.Kind == FieldKind.Parent);
        browser.Parent.Visible = parent != null;
        if (parent == null) return;
        int selected = browser.Parent.SelectedItem is ProfessionChoice choice ? choice.Id : 0;
        browser.Binding = true;
        try
        {
            var choices = new List<ProfessionChoice> { new(0, browser.Table.Name==InfusionSchema.Ingredients ? "All brewing recipes" : browser.Table.Name is InfusionSchema.ConstructionCosts or TransmutationSchema.ConstructionCosts ? "All stations" : browser.Profession==5 ? "All permanent effects" : "All sources") };
            choices.AddRange(ParentRows(parent.ReferenceTable, browser.Profession)
                .Select(r => new ProfessionChoice(r.Int("a_index"), DescribeProfessionRow(parent.ReferenceTable, r))));
            browser.Parent.DataSource = choices;
            browser.Parent.SelectedValue = choices.Any(c => c.Id == selected) ? selected : 0;
        }
        finally { browser.Binding = false; }
    }

    private IEnumerable<ProfessionRow> ParentRows(string table, int professionId) => catalog.Professions.Rows(table)
        .Where(r => table != "t_gathering_node" || r.Int("a_profession") == professionId)
        .OrderBy(r => r.Int("a_index"));

    private void RefreshProfessionBrowser(ProfessionBrowser browser)
    {
        if (!loaded || browser.Binding) return;
        string selectedKey = browser.Selected?.Key(browser.Table) ?? "";
        string search = browser.Search.Text.Trim();
        var parent = browser.Table.Fields.FirstOrDefault(f => f.Kind == FieldKind.Parent);
        int parentId = browser.Parent.SelectedItem is ProfessionChoice choice ? choice.Id : 0;
        var entries = ProfessionRows(browser).Where(r => parent == null || parentId == 0 || r.Int(parent.Column) == parentId)
            .Select(r => new ProfessionListEntry(r, r.Key(browser.Table), DescribeProfessionRow(browser.Table.Name, r),
                ProfessionRowSummary(browser.Table.Name, r), ProfessionEntryState(browser.Table.Name,r)))
            .Where(e => search.Length == 0 || (e.Identity + " " + e.Name + " " + e.Summary).Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => browser.Table.Name is InfusionSchema.Offers or TransmutationSchema.Offers ? e.Row.Int("a_sort") : e.Row.Int("a_index")).ThenBy(e=>e.Row.Int("a_index")).ThenBy(e => e.Row.Int("a_slot")).ThenBy(e => e.Row.Int("a_vertex_index")).ToList();
        browser.Binding = true;
        try
        {
            browser.List.DataSource = new BindingList<ProfessionListEntry>(entries);
            foreach (DataGridViewRow row in browser.List.Rows)
                if (row.DataBoundItem is ProfessionListEntry e && e.Identity == selectedKey)
                { browser.List.CurrentCell = row.Cells[0]; break; }
            browser.Selected = browser.List.CurrentRow?.DataBoundItem is ProfessionListEntry current ? current.Row : null;
        }
        finally { browser.Binding = false; }
        ShowProfessionEntry(browser);
    }

    private string NpcName(int id) => catalog.Professions.Npcs.TryGetValue(id, out var npc) ? npc.Name : "Missing NPC";
    private static string ProfessionEntryState(string table,ProfessionRow row) => table==TransmutationSchema.Facility
        ? row.Int("a_construction_enabled")==1 ? row.Enabled ? "Build & use" : "Build only" : row.Enabled ? "Use only" : "Paused"
        : table==TransmutationSchema.ItemRules ? row.Enabled ? "Formula / override" : "Excluded"
        : table==InfusionSchema.Facility
        ? row.Int("a_construction_enabled")==1 ? row.Enabled ? "Build & brew" : "Build only" : row.Enabled ? "Brew only" : "Paused"
        : table is InfusionSchema.Effects or InfusionSchema.Consumables ? "Permanent" : row.Enabled ? "Enabled" : "Retired";
    private string ZoneName(int id) => catalog.Professions.Zones.GetValueOrDefault(id, "Missing zone");
    private string DescribeProfessionRow(string table, ProfessionRow row) => table switch {
        InfusionSchema.Effects => catalog.ItemName(row.Int("a_item_index")),
        InfusionSchema.Offers => catalog.ItemName(row.Int("a_output_item_index")),
        InfusionSchema.Consumables => catalog.ItemName(row.Int("a_item_index")),
        InfusionSchema.Facility or TransmutationSchema.Facility => row.Get("a_name"),
        TransmutationSchema.Settings => "Stored points and equipment value",
        TransmutationSchema.Offers => catalog.ItemName(row.Int("a_output_item_index")),
        TransmutationSchema.ItemRules => catalog.ItemName(row.Int("a_item_index")),
        "t_gathering_settings" => GatheringProfessionName(row.Int("a_index")) + " progression",
        "t_fishing_settings" => "Fishing progression and carried rod",
        "t_gathering_node" => NpcName(row.Int("a_npc_index")),
        "t_gathering_tool" => catalog.ItemName(row.Int("a_item_index")),
        "t_gathering_profile" => "Mining model profile " + row.Int("a_index"),
        "t_herb_population" => NpcName(row.Int("a_npc_index")) + " — " + ZoneName(row.Int("a_zone_index")),
        "t_herb_spawn_point" => $"Site {row.Int("a_index")} — X {row.Get("a_pos_x")}, Z {row.Get("a_pos_z")}",
        "t_fishing_water" => row.Get("a_name"),
        "t_fishing_water_vertex" => $"Piece {row.Int("a_polygon_index") + 1}, vertex {row.Int("a_vertex_index") + 1}",
        _ => catalog.ItemName(row.Int("a_item_index"))
    };
    private string ProfessionRowSummary(string table, ProfessionRow row)
    {
        if (table.EndsWith("_loot", StringComparison.Ordinal))
        {
            var definition = ProfessionSchema.Find(table);
            string parent = definition.Fields.First(f => f.Kind == FieldKind.Parent).Column;
            long total = catalog.Professions.Rows(table).Where(r => r.Int(parent) == row.Int(parent) && r.Enabled)
                .Sum(r => (long)r.Int("a_weight"));
            decimal chance = row.Enabled && total > 0 ? row.Decimal("a_weight") * 100 / total : 0;
            return $"×{row.Int("a_item_count")} • {chance:0.##}%";
        }
        return table switch {
            TransmutationSchema.Settings => $"{row.Int("a_capacity"):N0} points maximum • level × {row.Int("a_points_numerator")} / {row.Int("a_points_denominator")}",
            TransmutationSchema.Offers => $"{row.Int("a_point_cost"):N0} points • {row.Int("a_output_count"):N0} items • {TimeSpan.FromSeconds(Math.Max(0,row.Int("a_duration_sec"))).TotalMinutes:0.##} min",
            TransmutationSchema.ItemRules => !row.Enabled ? "Excluded from conversion" : row.Int("a_points_override")==0 ? "Uses level formula" : $"{row.Int("a_points_override"):N0} points per item",
            TransmutationSchema.Facility => $"Construction {(row.Int("a_construction_enabled")==1?"on":"off")} • new activity {(row.Enabled?"on":"off")} • private hall",
            TransmutationSchema.ConstructionCosts => $"×{row.Int("a_count"):N0} • Crucible #{row.Int("a_facility_index")}",
            InfusionSchema.Effects => $"{catalog.Professions.Infusions.Potions.GetValueOrDefault(row.Int("a_item_index"))?.SkillName??"Skill #"+row.Int("a_skill_index")} • level {row.Int("a_skill_level")}",
            InfusionSchema.Offers => $"{row.Int("a_output_count"):N0} finished • {TimeSpan.FromSeconds(Math.Max(0,row.Int("a_duration_sec"))).TotalHours:0.##} h • {row.Int("a_required_count"):N0} original potions",
            InfusionSchema.Consumables => InfusionPotionName(row.Int("a_effect_index")),
            InfusionSchema.Ingredients => $"×{row.Int("a_count"):N0} • recipe #{row.Int("a_offer_index")}",
            InfusionSchema.ConstructionCosts => $"×{row.Int("a_count"):N0} • station #{row.Int("a_facility_index")}",
            InfusionSchema.Facility => $"Construction {(row.Int("a_construction_enabled")==1?"on":"off")} • brewing {(row.Enabled?"on":"off")} • private hall",
            "t_gathering_node" => catalog.ItemName(row.Int("a_item_index")) + $" • requires {row.Int("a_required_skill")}",
            "t_gathering_tool" => $"Requires {row.Int("a_required_skill")} • +{row.Decimal("a_yield_bonus_bp") / 100:0.##}% yield",
            "t_herb_population" => $"Minimum {row.Int("a_min_alive")} • {catalog.Professions.Rows("t_herb_spawn_point").Count(r => r.Int("a_population_index") == row.Int("a_index") && r.Enabled)} enabled sites",
            "t_fishing_water" => ZoneName(row.Int("a_zone_index")),
            "t_fishing_water_vertex" => $"X {row.Get("a_pos_x")}, Z {row.Get("a_pos_z")}",
            "t_gathering_profile" => string.IsNullOrEmpty(row.Get("a_geometry_hex")) ? "Import a model to configure" : "Imported mesh — preview / replace below",
            "t_gathering_settings" or "t_fishing_settings" => $"Maximum skill {row.Int("a_skill_cap")}",
            _ => "Edit on the map or by coordinates"
        };
    }

    private void ShowProfessionEntry(ProfessionBrowser browser)
    {
        ClearPanel(browser.Detail);
        if (browser.Selected is not ProfessionRow row)
        {
            var empty=Label("No entries match this view. Select New to add content, or change the source/search filter.");
            empty.AutoSize=false;empty.Dock=DockStyle.Top;empty.Height=72;empty.Padding=new(8);
            browser.Detail.AutoScrollPosition=Point.Empty;browser.Detail.Controls.Add(empty);
            SizeProfessionFields(browser);UpdateProfessionValidation(browser); return;
        }
        var fields = Fields(); fields.AutoScroll = false; fields.AutoSize = true; fields.Dock = DockStyle.Top;
        browser.Detail.Controls.Add(fields);
        var heading = Label(DescribeProfessionRow(browser.Table.Name, row)); heading.Font = new(Font, FontStyle.Bold);
        heading.MaximumSize = new(650, 0); fields.Controls.Add(heading);
        string description = browser.Table.Name == "t_gathering_settings"
            ? $"Skill-up chance and yield rules for {GatheringProfessionName(browser.Profession)}. Earned skill and mastery belong to each character; these settings do not reset progress."
            : browser.Table.Help;
        var help = Label(description); help.MaximumSize = new(650, 0); fields.Controls.Add(help);
        var quick = Flow(); quick.Dock = DockStyle.None;
        if (browser.Table.Name==InfusionSchema.Offers && catalog.Professions.Rows(InfusionSchema.Effects).FirstOrDefault(r=>r.Int("a_index")==row.Int("a_effect_index")) is { } effect)
        {
            quick.Controls.Add(Button("Open original potion",()=>OpenItem(effect.Int("a_item_index"))));
            quick.Controls.Add(Button("Effect definition…",()=>NavigateInfusionEffect(effect.Int("a_index"))));
            quick.Controls.Add(Button("Open finished potion",()=>OpenItem(row.Int("a_output_item_index"))));
            quick.Controls.Add(Button("Extra ingredients…",()=>SelectProfessionChild(5,InfusionSchema.Ingredients,row.Int("a_index"))));
            fields.Controls.Add(Label($"Consumes {row.Int("a_required_count"):N0} × {catalog.ItemName(effect.Int("a_item_index"))}, plus extra ingredients. Brews {row.Int("a_output_count"):N0} × {catalog.ItemName(row.Int("a_output_item_index"))} while online or offline. The recipient consumes it to gain the exact permanent effect. Already knowing the effect does not prevent brewing."));
        }
        if(browser.Table.Name==InfusionSchema.Consumables) {
            quick.Controls.Add(Button("Open finished item",()=>OpenItem(row.Int("a_item_index"))));
            quick.Controls.Add(Button("Effect definition…",()=>NavigateInfusionEffect(row.Int("a_effect_index"))));
            fields.Controls.Add(Label("The immutable effect mapping supplies the original potion's skill and level. Keep the finished item's ordinary skill fields at zero so it cannot fall back to a timed effect."));
        }
        if(browser.Table.Name is InfusionSchema.Facility or TransmutationSchema.Facility) {
            int facilityProfession=browser.Table.Name==TransmutationSchema.Facility?6:5;
            string costTable=facilityProfession==6?TransmutationSchema.ConstructionCosts:InfusionSchema.ConstructionCosts;
            quick.Controls.Add(Button("Construction costs…",()=>SelectProfessionChild(facilityProfession,costTable,row.Int("a_index"))));
            var costs=catalog.Professions.Rows(costTable).Where(r=>r.Int("a_facility_index")==row.Int("a_index")).OrderBy(r=>r.Int("a_slot")).ToList();
            string summary=costs.Count==0?"Free construction":string.Join(", ",costs.Select(r=>$"{r.Int("a_count"):N0} × {catalog.ItemName(r.Int("a_item_index"))}"));
            fields.Controls.Add(Label("One-time build cost: "+summary+". Edit these materials with the button below."));
        }
        AddTransmutationSummary(fields,quick,browser,row);
        if (row.Int("a_npc_index") > 0)
            quick.Controls.Add(Button("Open NPC / static placement…", () => new NPCEditor(main, row.Int("a_npc_index")).Show(this)));
        if (browser.Table.Name == "t_gathering_node")
            quick.Controls.Add(Button(browser.Profession == 1 ? "Passive drops…" : "Resource drops…",
                () => SelectProfessionChild(browser.Profession, "t_gathering_node_loot", row.Int("a_index"))));
        if (browser.Table.Name == "t_herb_population")
        {
            quick.Controls.Add(Button("Harvest drops…", () => SelectProfessionChild(2, "t_herb_loot", row.Int("a_index"))));
            quick.Controls.Add(Button("Candidate sites…", () => SelectProfessionChild(2, "t_herb_spawn_point", row.Int("a_index"))));
        }
        if (browser.Table.Name == "t_fishing_water")
            quick.Controls.Add(Button("Catches…", () => SelectProfessionChild(3, "t_fishing_loot", row.Int("a_index"))));
        if (ResourceItemFor(browser.Table.Name, row) is int resource && resource > 0)
            quick.Controls.Add(Button("Resource mastery…", () => RunProfessionAction(() =>
                SelectMasteryResource(ResourceItemFor(browser.Table.Name, row) ?? 0, browser.Profession))));
        if (quick.Controls.Count > 0) fields.Controls.Add(quick); else quick.Dispose();

        var effective = browser.Table.Fields.Where(f => !InactiveProfessionField(browser, f)).ToList();
        foreach (var grouping in effective.GroupBy(f => f.Group).OrderBy(group => browser.Table.Name is InfusionSchema.Facility or TransmutationSchema.Facility
            ? group.Key switch { "Availability"=>0,"Unbuilt plot"=>1,"General"=>2,_=>3 } : 0))
            AddProfessionFieldGroup(fields, browser, row, grouping.Key, grouping, false);
        var reserved = browser.Table.Fields.Where(f => InactiveProfessionField(browser, f)).ToList();
        if (reserved.Count > 0)
        {
            var advanced = new Panel { AutoSize = true, Visible = false };
            var reservedFields = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            advanced.Controls.Add(reservedFields);
            var reveal = new CheckBox { Text = "Show reserved / inactive engine fields (read-only)", AutoSize = true, Margin = new(8) };
            reveal.CheckedChanged += (_, _) => advanced.Visible = reveal.Checked;
            fields.Controls.Add(reveal); fields.Controls.Add(advanced);
            AddProfessionFieldGroup(reservedFields, browser, row, "Reserved data", reserved, true);
        }
        var apply = Label(ProfessionApplyGuidance(browser)); apply.MaximumSize = new(650, 0);
        apply.ForeColor = Color.FromArgb(70, 80, 95); fields.Controls.Add(apply);
        SizeProfessionFields(browser); UpdateProfessionValidation(browser);
    }

    private static void SizeProfessionFields(ProfessionBrowser browser)
    {
        if (browser.Sizing) return;
        browser.Sizing = true;
        try
        {
        int width = Math.Max(320, browser.Detail.ClientSize.Width - 38);
        browser.Validation.MaximumSize = new(Math.Max(320, browser.Detail.ClientSize.Width), 0);
        foreach(var label in browser.Detail.Controls.OfType<Label>())label.MaximumSize=new(width,0);
        foreach (var outer in browser.Detail.Controls.OfType<FlowLayoutPanel>())
        {
            outer.MaximumSize = new(width + 16, 0);
            foreach (var label in outer.Controls.OfType<Label>()) label.MaximumSize = new(width, 0);
            foreach (var row in outer.Controls.OfType<FlowLayoutPanel>()) row.MaximumSize = new(width, 0);
        }
        foreach (var group in Descendants(browser.Detail).OfType<GroupBox>().Where(c => c.Name == "ProfessionFieldGroup"))
        {
            group.MaximumSize = new(width, 0); group.Width = width;
            foreach (var line in Descendants(group).OfType<FlowLayoutPanel>().Where(c => c.Name == "ProfessionFieldRow"))
                line.MaximumSize = new(Math.Max(290, width - 24), 0);
            foreach (var label in Descendants(group).OfType<Label>().Where(c => c.MaximumSize.Width > 185))
                label.MaximumSize = new(Math.Max(290, width - 30), 0);
        }
        }
        finally { browser.Sizing = false; }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private ProfessionRow? ProfessionPreviewSource(ProfessionBrowser browser)
    {
        string source = browser.Profession switch { 2 => "t_herb_population", 3 => "t_fishing_water", _ => "t_gathering_node" };
        if (browser.Table.Name == source) return browser.Selected;
        var parent = browser.Table.Fields.FirstOrDefault(f => f.ReferenceTable == source && f.Kind == FieldKind.Parent);
        if (parent != null && browser.Selected != null)
            return catalog.Professions.Rows(source).FirstOrDefault(r => r.Int("a_index") == browser.Selected.Int(parent.Column));
        return catalog.Professions.Rows(source).FirstOrDefault(r => source != "t_gathering_node" || r.Int("a_profession") == browser.Profession);
    }
    private static bool InactiveProfessionField(ProfessionBrowser browser, ProfessionField field) => field.Kind != FieldKind.Geometry && (field.Reserved
        || (browser.Table.Name == "t_gathering_settings" && browser.Profession == 2 && field.Column.StartsWith("a_passive_", StringComparison.Ordinal))
        || (browser.Table.Name == "t_gathering_node" && browser.Profession == 1 && field.Column.StartsWith("a_rhythm_", StringComparison.Ordinal))
        || (browser.Table.Name == "t_gathering_node" && browser.Profession == 4 && field.Column is "a_interaction_profile" or "a_target_radius_bp"));

    private void AddProfessionFieldGroup(FlowLayoutPanel outer, ProfessionBrowser browser, ProfessionRow row,
        string title, IEnumerable<ProfessionField> definitions, bool reserved)
    {
        var content = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new(5, 12, 5, 5) };
        var group = new GroupBox { Name = "ProfessionFieldGroup", Text = title, AutoSize = true, Padding = new(4), MinimumSize = new(320, 0) };
        group.Controls.Add(content); outer.Controls.Add(group);
        foreach (var field in definitions)
        {
            var line = new FlowLayoutPanel { Name = "ProfessionFieldRow", AutoSize = true, WrapContents = true, Margin = new(2, 3, 2, 3), MaximumSize = new(650, 0) };
            var caption = Label(field.Column == "a_profession" ? "Profession" : field.Label); caption.MaximumSize = new(185, 0); caption.MinimumSize = new(185, 0);
            line.Controls.Add(caption);
            bool keyLocked = browser.Table.Name is "t_gathering_settings" or "t_fishing_settings" or TransmutationSchema.Settings or TransmutationSchema.Facility
                ? field.Column == "a_index"
                : IsDurableProfessionTable(browser.Table.Name) && browser.Table.Keys.Contains(field.Column);
            bool effectIdentity = browser.Table.Name==InfusionSchema.Effects && (field.Column is "a_item_index" or "a_skill_index" or "a_skill_level")
                && original.Professions.Rows(InfusionSchema.Effects).Any(r=>r.Int("a_index")==row.Int("a_index"));
            effectIdentity |= browser.Table.Name==InfusionSchema.Consumables && field.Column=="a_effect_index"
                && original.Professions.Rows(InfusionSchema.Consumables).Any(r=>r.Int("a_item_index")==row.Int("a_item_index"));
            bool derivedSkill = browser.Table.Name==InfusionSchema.Effects && field.Column is "a_skill_index" or "a_skill_level";
            bool readOnly = reserved || keyLocked || effectIdentity || derivedSkill || field.Column == "a_profession" || field.Column is "a_slot" or "a_vertex_index";
            if (field.Kind == FieldKind.Geometry)
            {
                var label = Label(string.IsNullOrEmpty(row.Get(field.Column)) ? "No model profile imported" : "Imported interaction mesh (generated data)");
                label.MaximumSize = new(400, 0); line.Controls.Add(label);
                if (!reserved && browser.Table.Name == "t_gathering_profile")
                    line.Controls.Add(Button("Import…", () => ImportProfessionProfile(browser)));
            }
            else if (field.Kind == FieldKind.Boolean)
            {
                var check = new CheckBox { Name = field.Column, Text = row.Int(field.Column) == 1 ? "Yes" : "No", AutoSize = true,
                    Checked = row.Int(field.Column) == 1, Enabled = !readOnly, Margin = new(5, 8, 5, 4) };
                check.CheckedChanged += (_, _) => { check.Text = check.Checked ? "Yes" : "No"; row.Set(field.Column, check.Checked ? 1 : 0); ProfessionEntryEdited(browser); };
                line.Controls.Add(check);
            }
            else if (field.Kind == FieldKind.Text)
            {
                var text = new TextBox { Name = field.Column, Text = row.Get(field.Column), Width = 340, ReadOnly = readOnly, MaxLength = (int)Math.Clamp(field.Max, 1, 10000) };
                text.TextChanged += (_, _) => { row.Set(field.Column, text.Text); ProfessionEntryEdited(browser); };
                line.Controls.Add(text);
            }
            else if (field.Kind == FieldKind.Duration && !readOnly)
                AddProfessionDuration(line,browser,row,field);
            else if (field.Kind == FieldKind.ArtisanRank && !readOnly)
                AddProfessionArtisanRank(line, browser, row, field);
            else if (field.Kind is FieldKind.Parent or FieldKind.Profile)
                AddProfessionReference(line, browser, row, field, readOnly);
            else if (field.Kind is FieldKind.Item or FieldKind.Npc or FieldKind.Zone && !readOnly)
                AddProfessionPicker(line, browser, row, field);
            else if (field.Column == "a_profession" || browser.Table.Name == "t_gathering_settings" && field.Column == "a_index")
                line.Controls.Add(Label(GatheringProfessionName(row.Int(field.Column))));
            else
            {
                decimal value = row.Decimal(field.Column) / field.Scale;
                var number = new NumericUpDown { Name = field.Column, Width = 145, DecimalPlaces = field.DecimalPlaces,
                    Minimum = Math.Min(field.Min / field.Scale, value), Maximum = Math.Max(field.Max / field.Scale, value),
                    Value = value, ThousandsSeparator = true, Enabled = !readOnly,
                    Increment = field.DecimalPlaces == 0 ? 1 : (decimal)Math.Pow(10, -field.DecimalPlaces) };
                number.ValueChanged += (_, _) => { row.Set(field.Column, number.Value * field.Scale); ProfessionEntryEdited(browser); };
                line.Controls.Add(number);
            }
            content.Controls.Add(line);
            bool resourceHelp = browser.Table.Name == "t_gathering_node" && field.Column == "a_item_index";
            if (field.Help.Length > 0 || readOnly && field.Identity || derivedSkill || effectIdentity || resourceHelp)
            {
                string explanation = derivedSkill ? "Derived from the selected original potion. Its exact skill and level define the permanent buff." : effectIdentity ? "Published permanent identity: retained for every character who already acquired this effect." : field.Help;
                if (resourceHelp) explanation += browser.Profession == 1
                    ? "Active Mining awards this resource. Passive gathering uses the weighted drop list when configured. Each drop's resource mastery is edited separately."
                    : "This item labels the source. Active and passive Wave rewards use its weighted drop list; changing this display item does not replace those outcomes.";
                if (keyLocked) explanation = "Stable identity. New entries receive an unused ID; published identities are retained. " + explanation;
                else if (reserved) explanation = "Stored for engine compatibility; this field does not control the selected gathering mode. " + explanation;
                var hint = Label(explanation); hint.MaximumSize = new(610, 0); hint.ForeColor = SystemColors.GrayText;
                hint.Margin = new(10, 0, 5, 7); content.Controls.Add(hint);
            }
        }
    }

    private void AddProfessionArtisanRank(FlowLayoutPanel line, ProfessionBrowser browser, ProfessionRow row, ProfessionField field)
    {
        var combo = new ComboBox { Name = field.Column, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Name", ValueMember = "Id" };
        bool refreshing = false;
        void RefreshChoices()
        {
            refreshing = true; combo.BeginUpdate();
            try
            {
                var choices = new List<ProfessionChoice> { new(0, "None — unrestricted") };
                choices.AddRange(catalog.Ranks.OrderBy(rank => rank.Order).ThenBy(rank => rank.Id)
                    .Select(rank => new ProfessionChoice(rank.Id, $"{rank.Name} (ID {rank.Id})" + (rank.Enabled ? "" : " — retired"))));
                int saved = row.Int(field.Column);
                if (!choices.Any(choice => choice.Id == saved)) choices.Add(new(saved, "Missing Artisan Rank (ID " + saved + ")"));
                combo.Items.Clear(); combo.Items.AddRange(choices.Cast<object>().ToArray());
                combo.SelectedIndex = choices.FindIndex(choice => choice.Id == saved);
            }
            finally { combo.EndUpdate(); refreshing = false; }
        }
        RefreshChoices();
        combo.DropDown += (_, _) => RefreshChoices();
        combo.SelectedIndexChanged += (_, _) => {
            if (refreshing || combo.SelectedItem is not ProfessionChoice selected) return;
            row.Set(field.Column, selected.Id); ProfessionEntryEdited(browser);
        };
        line.Controls.Add(combo);
    }

    private void AddProfessionReference(FlowLayoutPanel line, ProfessionBrowser browser, ProfessionRow row, ProfessionField field, bool readOnly=false)
    {
        var combo = new ComboBox { Name = field.Column, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", ValueMember = "Id", Enabled=!readOnly };
        var choices = ParentRows(field.ReferenceTable, browser.Profession)
            .Select(r => new ProfessionChoice(r.Int("a_index"), DescribeProfessionRow(field.ReferenceTable, r))).ToList();
        if (field.Min == 0 || choices.Count == 0) choices.Insert(0, new(0, "None — create or import a source first"));
        if (!choices.Any(c => c.Id == row.Int(field.Column))) choices.Add(new(row.Int(field.Column), "Missing reference #" + row.Get(field.Column)));
        combo.Items.AddRange(choices.Cast<object>().ToArray()); combo.SelectedIndex = choices.FindIndex(c => c.Id == row.Int(field.Column));
        combo.SelectedIndexChanged += (_, _) => {
            if (combo.SelectedItem is not ProfessionChoice selected) return;
            int previous = row.Int(field.Column); row.Set(field.Column, selected.Id);
            if (field.Kind == FieldKind.Parent) { ReindexProfessionChildren(browser.Table.Name, previous); ReindexProfessionChildren(browser.Table.Name, selected.Id); }
            ProfessionEntryEdited(browser);
        };
        line.Controls.Add(combo);
    }

    private void AddProfessionPicker(FlowLayoutPanel line, ProfessionBrowser browser, ProfessionRow row, ProfessionField field)
    {
        var number = new NumericUpDown { Name = field.Column, Minimum = Math.Min(field.Min, row.Int(field.Column)), Maximum = field.Max,
            Value = row.Int(field.Column), Width = 125, ThousandsSeparator = true };
        var name = Label(ProfessionReferenceName(field.Kind, row.Int(field.Column))); name.MaximumSize = new(330, 0);
        number.ValueChanged += (_, _) => {
            row.Set(field.Column, number.Value); name.Text = ProfessionReferenceName(field.Kind, (int)number.Value);
            if(browser.Table.Name==InfusionSchema.Effects && field.Column=="a_item_index") {
                DeriveInfusionSkill(row);
                foreach(var skillControl in Descendants(browser.Detail).OfType<NumericUpDown>().Where(c=>c.Name is "a_skill_index" or "a_skill_level"))
                    skillControl.Value=Math.Clamp(row.Int(skillControl.Name),skillControl.Minimum,skillControl.Maximum);
            }
            ProfessionEntryEdited(browser);
        };
        line.Controls.Add(number);
        line.Controls.Add(Button("Choose…", () => {
            int? id = PickProfessionReference(field.Kind, (int)number.Value);
            if (id.HasValue) number.Value = Math.Clamp(id.Value, number.Minimum, number.Maximum);
        }));
        if (field.Kind == FieldKind.Item) line.Controls.Add(Button("Open item", () => OpenItem((int)number.Value)));
        if (field.Kind == FieldKind.Npc) line.Controls.Add(Button("Open NPC", () => new NPCEditor(main, (int)number.Value).Show(this)));
        line.Controls.Add(name);
    }
    private string ProfessionReferenceName(FieldKind kind, int id) => kind switch
    { FieldKind.Item => catalog.ItemName(id), FieldKind.Npc => NpcName(id), FieldKind.Zone => ZoneName(id), _ => id.ToString(CultureInfo.InvariantCulture) };
    private int? PickProfessionReference(FieldKind kind, int current)
    {
        if (kind == FieldKind.Item) return PickItem(current);
        if (kind == FieldKind.Npc)
        { using var picker = new NPCPicker(main, this, current, false); return picker.ShowDialog(this) == DialogResult.OK ? Convert.ToInt32(picker.ReturnValues[0]) : null; }
        using (var picker = new ZonePicker(main, this, current))
            return picker.ShowDialog(this) == DialogResult.OK ? Convert.ToInt32(picker.ReturnValues[0]) : null;
    }

    private void ProfessionEntryEdited(ProfessionBrowser browser)
    {
        Changed();
        if(browser.Selected is { } transmutationRow)
            foreach(var label in Descendants(browser.Detail).OfType<Label>().Where(c=>c.Name=="TransmutationExplanation"))
                label.Text=TransmutationSummary(browser.Table.Name,transmutationRow);
        if (browser.List.DataSource is BindingList<ProfessionListEntry> entries)
        {
            browser.Binding = true;
            try { for (int i = 0; i < entries.Count; i++) {
                var entry = entries[i]; entry.Identity = entry.Row.Key(browser.Table);
                entry.Name = DescribeProfessionRow(browser.Table.Name, entry.Row);
                entry.Summary = ProfessionRowSummary(browser.Table.Name, entry.Row);
                entry.State = ProfessionEntryState(browser.Table.Name,entry.Row); entries.ResetItem(i);
            } } finally { browser.Binding = false; }
        }
        UpdateProfessionValidation(browser);
    }
    private void UpdateProfessionValidation(ProfessionBrowser browser)
    {
        if (!loaded) { browser.Validation.Text = "Load profession content to begin."; return; }
        var issues = ProfessionRules.Validate(catalog); var errors = issues.Where(i => i.Error).ToList();
        browser.Validation.ForeColor = errors.Count > 0 ? Color.Firebrick : Color.DarkSlateGray;
        browser.Validation.Text = errors.Count == 0 ? "No catalogue errors. World accessibility and deployed assets are checked separately. Player progress is never edited."
            : $"{errors.Count} issue(s) prevent saving:\r\n" + string.Join("\r\n", errors.Take(3).Select(i => i.ToString()))
                + (errors.Count > 3 ? "\r\nOpen Validate / Overview for the full list." : "");
    }
    private void DeriveInfusionSkill(ProfessionRow row) {
        bool found=catalog.Professions.Infusions.Potions.TryGetValue(row.Int("a_item_index"),out var potion);
        row.Set("a_skill_index",found?potion!.SkillId:0);row.Set("a_skill_level",found?potion!.SkillLevel:0);
    }
    private void NavigateInfusionEffect(int id) {
        NavigateProfessionPage(5,InfusionSchema.Effects);
        var browser=professionBrowsers[(5,InfusionSchema.Effects)];browser.Search.Clear();
        browser.Selected=catalog.Professions.Rows(InfusionSchema.Effects).FirstOrDefault(r=>r.Int("a_index")==id);
        RefreshProfessionBrowser(browser);
    }
    private string InfusionPotionName(int effectId) => catalog.Professions.Rows(InfusionSchema.Effects).FirstOrDefault(r=>r.Int("a_index")==effectId) is { } effect ? catalog.ItemName(effect.Int("a_item_index")) : "Missing permanent effect #"+effectId;
    private static string ProfessionApplyGuidance(ProfessionBrowser browser) => browser.Profession == 6
        ? "Apply: Save, then reopen the Transmutation Crucible or plot. Model/placement changes may require re-entering the hall; new artwork needs client deployment. Construction rank and costs apply once. Paid orders retain their original prices, products and times. Stored points, buildings, queued work and claim receipts are never exported."
        : browser.Profession == 5
        ? "Apply: Save, then reopen the Alchemy station/plot. Construction costs and rank apply once when building; existing built ownership is retained. Brewing enabled controls new brews independently. Placement/model changes may require re-entering the private hall; new artwork needs client asset deployment. Accepted brewing jobs keep their original terms. Character station/effect unlocks, jobs and receipts are never exported."
        : browser.Profession == 2
        ? "Apply: Save, then reopen /gather for skill/loot/mastery changes. Use the admin command /herbpop reload for population timing and candidate-site edits. Enabling a new population or changing its zone/NPC ownership needs a GameServer restart."
        : browser.Table.Name == "t_gathering_profile"
            ? "Apply: import to the draft, Save, then reopen /gather. Interaction geometry must match the deployed client model. Model/texture asset changes still need separate deployment."
            : "Apply: Save, then reopen /gather to refresh gathering rules and rewards. Existing item/NPC prototypes and static world placements follow their normal editor/export/restart workflow.";

    private void SelectProfessionChild(int professionId, string table, int parentId)
    {
        NavigateProfessionPage(professionId, table);
        var browser = professionBrowsers[(professionId, table)]; RefreshProfessionParents(browser);
        browser.Parent.SelectedValue = parentId; RefreshProfessionBrowser(browser);
    }
    private static int? ResourceItemFor(string table, ProfessionRow row) => table is "t_gathering_node" or "t_gathering_node_loot" or "t_herb_loot" or "t_fishing_loot"
        ? row.Int("a_item_index") : null;

    private static bool IsDurableProfessionTable(string table) => table is "t_gathering_node" or "t_gathering_tool"
        or "t_gathering_profile" or "t_herb_population" or "t_herb_spawn_point" or "t_fishing_water" or InfusionSchema.Effects or InfusionSchema.Offers or InfusionSchema.Facility or InfusionSchema.Consumables;
    private void RunProfessionAction(Action action)
    { try { action(); } catch (Exception error) { Error(error); } }

    private void NewProfessionEntry(ProfessionBrowser browser, bool duplicate)
    {
        if (!loaded || duplicate && browser.Selected == null) return;
        RunProfessionAction(() => {
            var definition = browser.Table;
            // New sources inherit the current branch's known-good timing rules.
            // Duplicate additionally copies the source's related drops and geometry.
            ProfessionRow? template = duplicate ? browser.Selected : ProfessionRows(browser).FirstOrDefault();
            var row = template == null ? ProfessionSchema.NewRow(definition.Name) : CraftingCatalog.Copy(template);
            var rows = catalog.Professions.Rows(definition.Name);
            if (definition.Name is InfusionSchema.Effects or InfusionSchema.Offers && rows.Count>=16)
                throw new InvalidOperationException("The station supports at most 16 permanent effects and 16 offers, including retired offers.");
            if(definition.Name==TransmutationSchema.Offers && rows.Count>=16)throw new InvalidOperationException("At most 16 Transmutation outputs are supported. Remove or edit an existing output; paid orders keep their saved terms.");
            if(definition.Name==TransmutationSchema.ItemRules && rows.Count>=4096)throw new InvalidOperationException("At most 4096 equipment exceptions are supported.");
            if(definition.Name==InfusionSchema.Consumables && rows.Count>=64)throw new InvalidOperationException("At most 64 permanent potion mappings are supported; retain published mappings.");
            var parent = definition.Fields.FirstOrDefault(f => f.Kind == FieldKind.Parent);
            if(IsAlchemyCostTable(definition.Name) && parent!=null && browser.Parent.SelectedItem is ProfessionChoice selected && selected.Id>0 && rows.Count(r=>r.Int(parent.Column)==selected.Id)>=8)
                throw new InvalidOperationException("A recipe or station supports at most eight different material costs.");
            if (definition.Fields.Any(f => f.Column == "a_index")) row.Set("a_index", checked(rows.Select(r => r.Int("a_index")).DefaultIfEmpty().Max() + 1));
            if (row.Values.ContainsKey("a_profession")) row.Set("a_profession", browser.Profession);
            if (parent != null)
            {
                int selectedParent = browser.Parent.SelectedItem is ProfessionChoice choice && choice.Id > 0 ? choice.Id
                    : browser.Selected?.Int(parent.Column) ?? ParentRows(parent.ReferenceTable, browser.Profession).FirstOrDefault()?.Int("a_index") ?? 0;
                if (selectedParent <= 0) throw new InvalidOperationException("Create a source first, then add its drops, sites or boundaries.");
                row.Set(parent.Column, selectedParent);
            }
            if(IsAlchemyCostTable(definition.Name) && parent!=null && rows.Count(r=>r.Int(parent.Column)==row.Int(parent.Column))>=8)
                throw new InvalidOperationException("A recipe or station supports at most eight different material costs.");
            if (definition.Name == "t_gathering_profile" && !duplicate)
            {
                row.Set("a_geometry_hex", "");
                if (!ProfessionProfileImporter.Import(this, row, exportCheckout)) return;
            }
            if (definition.Name is "t_gathering_node" or "t_herb_population")
            {
                int? npc = PickProfessionReference(FieldKind.Npc, duplicate ? row.Int("a_npc_index") : 0);
                if (!npc.HasValue) return; row.Set("a_npc_index", npc.Value);
                if (definition.Name == "t_herb_population")
                {
                    int? zone = PickProfessionReference(FieldKind.Zone, row.Int("a_zone_index"));
                    if (!zone.HasValue) return; row.Set("a_zone_index", zone.Value);
                    if (rows.Any(r => r.Int("a_zone_index") == zone && r.Int("a_npc_index") == npc))
                        throw new InvalidOperationException("That zone already has a population for this NPC. Choose another zone or NPC.");
                }
                else if (rows.Any(r => r.Int("a_npc_index") == npc))
                    throw new InvalidOperationException("That NPC already has a gathering source. Choose a different NPC prototype.");
            }
            if (definition.Name == "t_fishing_water")
            {
                int? zone = PickProfessionReference(FieldKind.Zone, row.Int("a_zone_index"));
                if (!zone.HasValue) return; row.Set("a_zone_index", zone.Value);
                row.Set("a_name", duplicate ? row.Get("a_name") + " copy" : "New fishing water");
            }
            if (definition.Fields.Any(f => f.Column == "a_item_index"))
            {
                int? item = PickItem(row.Int("a_item_index")); if (!item.HasValue) return;
                if (definition.Name is "t_gathering_tool" or InfusionSchema.Consumables or TransmutationSchema.ItemRules && rows.Any(r => r.Int("a_item_index") == item))
                    throw new InvalidOperationException("This item already has an entry in this section. Select it in the list to edit it.");
                row.Set("a_item_index", item.Value);
                if(definition.Name==InfusionSchema.Effects)DeriveInfusionSkill(row);
                if (ResourceItemFor(definition.Name, row).HasValue) EnsureGatheringResource(item.Value, browser.Profession);
            }
            if (definition.Name.EndsWith("_loot", StringComparison.Ordinal) || IsAlchemyCostTable(definition.Name))
                row.Set("a_slot", rows.Count(r => r.Int(parent!.Column) == row.Int(parent.Column)));
            if (definition.Name == "t_fishing_water_vertex")
                row.Set("a_vertex_index", rows.Count(r => r.Int("a_water_index") == row.Int("a_water_index") && r.Int("a_polygon_index") == row.Int("a_polygon_index")));
            // A brand new source is configured as a draft before it can spawn.
            if (definition.Name is "t_gathering_node" or "t_herb_population" or "t_fishing_water") row.Set("a_enable", 0);
            if(definition.Name==TransmutationSchema.Offers) {int? output=PickItem(row.Int("a_output_item_index"));if(!output.HasValue)return;row.Set("a_output_item_index",output.Value);}
            rows.Add(row);
            if (duplicate && template != null) CopyProfessionChildren(definition.Name, template, row);
            browser.Selected = row; Changed(); RefreshProfessionPages();
            status.Text = "Added to the draft. Configure the source, drops and geometry, then enable it. Save validates related content together.";
        });
    }

    private void CopyProfessionChildren(string table, ProfessionRow source, ProfessionRow target)
    {
        var children = table switch {
            "t_gathering_node" => new[] { ("t_gathering_node_loot", "a_node_index") },
            "t_herb_population" => new[] { ("t_herb_loot", "a_population_index"), ("t_herb_spawn_point", "a_population_index") },
            "t_fishing_water" => new[] { ("t_fishing_loot", "a_water_index"), ("t_fishing_water_vertex", "a_water_index") },
            InfusionSchema.Offers => new[] { (InfusionSchema.Ingredients,"a_offer_index") },
            _ => Array.Empty<(string, string)>()
        };
        foreach (var (childTable, parentColumn) in children)
        {
            var rows = catalog.Professions.Rows(childTable);
            foreach (var old in rows.Where(r => r.Int(parentColumn) == source.Int("a_index")).ToArray())
            {
                var copy = CraftingCatalog.Copy(old); copy.Set(parentColumn, target.Int("a_index"));
                if (copy.Values.ContainsKey("a_index")) copy.Set("a_index", checked(rows.Select(r => r.Int("a_index")).DefaultIfEmpty().Max() + 1));
                rows.Add(copy);
            }
        }
    }
    private void ToggleProfessionEntry(ProfessionBrowser browser)
    {
        if (browser.Selected is not ProfessionRow row) return;
        row.Set(row.Values.ContainsKey("a_enabled")?"a_enabled":"a_enable", row.Enabled ? 0 : 1); ProfessionEntryEdited(browser); ShowProfessionEntry(browser);
    }
    private void RemoveProfessionEntry(ProfessionBrowser browser)
    {
        if (browser.Selected is not ProfessionRow row) return;
        var definition = browser.Table;
        if (IsDurableProfessionTable(definition.Name) && original.Professions.Rows(definition.Name).Any(r => r.Key(definition) == row.Key(definition)))
        {
            status.Text = definition.Fields.Any(f => f.Column is "a_enable" or "a_enabled") ? "Published identities are preserved. Use Retire / Enable instead."
                : "Published identities and permanent potion mappings must remain defined for existing items, effects and accepted brewing jobs.";
            return;
        }
        RunProfessionAction(() => {
            if (definition.Name is "t_gathering_node" or "t_herb_population" or "t_fishing_water" or InfusionSchema.Offers)
            {
                foreach (var child in ProfessionSchema.Tables)
                {
                    var parent = child.Fields.FirstOrDefault(f => f.ReferenceTable == definition.Name && f.Kind == FieldKind.Parent);
                    if (parent != null) catalog.Professions.Rows(child.Name).RemoveAll(r => r.Int(parent.Column) == row.Int("a_index"));
                }
            }
            catalog.Professions.Rows(definition.Name).Remove(row);
            var parentField = definition.Fields.FirstOrDefault(f => f.Kind == FieldKind.Parent);
            if (parentField != null) ReindexProfessionChildren(definition.Name, row.Int(parentField.Column));
            browser.Selected = null; Changed(); RefreshProfessionPages();
        });
    }
    private void MoveProfessionEntry(ProfessionBrowser browser, int direction)
    {
        if (browser.Selected is not ProfessionRow row) return;
        var parent = browser.Table.Fields.FirstOrDefault(f => f.Kind == FieldKind.Parent);
        if (parent == null) return;
        string order = browser.Table.Name == "t_fishing_water_vertex" ? "a_vertex_index" : "a_slot";
        var siblings = catalog.Professions.Rows(browser.Table.Name).Where(r => r.Int(parent.Column) == row.Int(parent.Column)
            && (order != "a_vertex_index" || r.Int("a_polygon_index") == row.Int("a_polygon_index"))).OrderBy(r => r.Int(order)).ToList();
        int index = siblings.IndexOf(row), other = index + direction;
        if (index < 0 || other < 0 || other >= siblings.Count) return;
        int value = row.Int(order); row.Set(order, siblings[other].Int(order)); siblings[other].Set(order, value);
        Changed(); RefreshProfessionBrowser(browser);
    }
    private void ReindexProfessionChildren(string table, int parentId)
    {
        var definition = ProfessionSchema.Find(table); var parent = definition.Fields.FirstOrDefault(f => f.Kind == FieldKind.Parent);
        string order = table == "t_fishing_water_vertex" ? "a_vertex_index" : "a_slot";
        if (parent == null || !definition.Fields.Any(f => f.Column == order)) return;
        foreach (var group in catalog.Professions.Rows(table).Where(r => r.Int(parent.Column) == parentId)
                     .GroupBy(r => table == "t_fishing_water_vertex" ? r.Int("a_polygon_index") : 0))
        { int index = 0; foreach (var row in group.OrderBy(r => r.Int(order))) row.Set(order, index++); }
    }

    private void EditProfessionMap(ProfessionBrowser browser)
    {
        if (!loaded) return;
        RunProfessionAction(() => {
            if (browser.Table.Name is "t_herb_population" or "t_herb_spawn_point")
            {
                int id = browser.Table.Name == "t_herb_population" ? browser.Selected?.Int("a_index") ?? 0
                    : browser.Selected?.Int("a_population_index") ?? (browser.Parent.SelectedItem as ProfessionChoice)?.Id ?? 0;
                var source = catalog.Professions.Rows("t_herb_population").FirstOrDefault(r => r.Int("a_index") == id)
                    ?? throw new InvalidOperationException("Select a herb population first.");
                if (ProfessionMapEditor.EditHerbs(this, main, source.Int("a_zone_index"), id, catalog.Professions.Rows("t_herb_spawn_point")))
                { Changed(); RefreshProfessionPages(); }
            }
            else
            {
                int id = browser.Table.Name == "t_fishing_water" ? browser.Selected?.Int("a_index") ?? 0
                    : browser.Selected?.Int("a_water_index") ?? (browser.Parent.SelectedItem as ProfessionChoice)?.Id ?? 0;
                var source = catalog.Professions.Rows("t_fishing_water").FirstOrDefault(r => r.Int("a_index") == id)
                    ?? throw new InvalidOperationException("Select a body of water first.");
                if (ProfessionMapEditor.EditFishing(this, main, source.Int("a_zone_index"), id, catalog.Professions.Rows("t_fishing_water_vertex")))
                { Changed(); RefreshProfessionPages(); }
            }
        });
    }
    private void ImportProfessionProfile(ProfessionBrowser browser)
    {
        if (browser.Selected is not ProfessionRow row) { status.Text = "Select a model profile, or choose New to import one."; return; }
        RunProfessionAction(() => { if (ProfessionProfileImporter.Import(this, row, exportCheckout)) { ProfessionEntryEdited(browser); ShowProfessionEntry(browser); } });
    }
}
