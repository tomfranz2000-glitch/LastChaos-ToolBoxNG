using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private sealed record TeachingItemRow(int Id, string Name, int RecipeCount, string State);
    private readonly DataGridView teachingItems = Grid(true);
    private readonly TextBox teachingSearch = new() { Name = "TeachingItemSearch", Width = 240, PlaceholderText = "Item name or ID" };
    private readonly ComboBox teachingFilter = new() { Name = "TeachingItemFilter", Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label teachingCount = new() { AutoSize = true, Margin = new(5) };
    private bool bindingTeachingItems;

    private void BuildTeachingItems(TabPage page)
    {
        var split = new SplitContainer {
            Size = new(1350,750), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 380, Panel2MinSize = 520, SplitterDistance = 580
        };
        Shown += (_,_) => split.SplitterDistance = Math.Max(380,Math.Min(600,split.Width-525));
        page.Controls.Add(split);
        teachingItems.Name = "TeachingItemList";
        teachingItems.MultiSelect = false;
        teachingItems.Columns.Add(new DataGridViewImageColumn {
            Name = "Icon", HeaderText = "", FillWeight = 25, ImageLayout = DataGridViewImageCellLayout.Zoom,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = null }
        });
        Column(teachingItems,"Id","Item ID",55);
        Column(teachingItems,"Name","Recipe item",175);
        Column(teachingItems,"RecipeCount","Recipes",50);
        Column(teachingItems,"State","Status",75);
        teachingItems.CellFormatting += (_,e) => {
            if(e.RowIndex >= 0 && teachingItems.Columns[e.ColumnIndex].Name == "Icon"
                && teachingItems.Rows[e.RowIndex].DataBoundItem is TeachingItemRow item) {
                e.Value = ItemIcon(item.Id); e.FormattingApplied = true;
            }
        };
        teachingItems.SelectionChanged += (_,_) => {
            if(bindingTeachingItems || teachingItems.CurrentRow?.DataBoundItem is not TeachingItemRow item) return;
            bindingTeachingItems = true;
            try { bookItem.Value = item.Id; }
            finally { bindingTeachingItems = false; }
            RefreshBook();
        };
        teachingFilter.Items.AddRange(["All recipe items","Assigned","Unassigned"]);
        teachingFilter.SelectedIndex = 0;
        var filters = Flow(); filters.Controls.AddRange([Label("Search"),teachingSearch,teachingFilter]);
        split.Panel1.Controls.Add(Vertical(filters,teachingCount,teachingItems));
        teachingSearch.TextChanged += (_,_) => RefreshTeachingItems(true);
        teachingFilter.SelectedIndexChanged += (_,_) => RefreshTeachingItems(true);

        var bookTools = Flow();
        bookTools.Controls.AddRange([Label("Item ID"),bookItem,
            Button("Choose item",()=> { int? id=PickItem((int)bookItem.Value); if(id.HasValue) bookItem.Value=id.Value; }),
            Button("Open item",()=>OpenItem((int)bookItem.Value)),
            Button("New recipe-book item",()=>CreateManual()),Button("Assign recipes…",()=>AssignBook())]);
        bookItem.ValueChanged += (_,_) => RevealTeachingItem();
        split.Panel2.Controls.Add(Vertical(bookTools,bookName,Label("Recipes taught by this item"),bookRecipes));
        split.Panel2.SizeChanged += (_,_) => bookName.MaximumSize = new(Math.Max(100,split.Panel2.ClientSize.Width-24),0);
    }

    private List<TeachingItemRow> TeachingItemRows()
    {
        var assigned = catalog.Recipes.SelectMany(r => r.Manuals.Select(m => (m.ItemId,r.Id))).Distinct()
            .GroupBy(link => link.ItemId).ToDictionary(g => g.Key,g => g.Count());
        // Include unassigned manual items and broken links, so authors can find
        // existing books and repair definitions without already knowing an ID.
        return catalog.Items.Values.Where(i => i.Manual).Select(i => i.Id).Concat(assigned.Keys)
            .Where(id => id > 0).Distinct().Select(id => {
                int count = assigned.GetValueOrDefault(id);
                string state = !catalog.Items.TryGetValue(id,out var item) ? "Missing item" :
                    !item.Enabled ? "Disabled" : !item.Manual ? "Wrong type" : item.Timed ? "Timed item" :
                    count == 0 ? "Unassigned" : "Assigned";
                return new TeachingItemRow(id,catalog.ItemName(id),count,state);
            }).OrderByDescending(i => i.RecipeCount > 0).ThenBy(i => i.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Id).ToList();
    }

    private void RefreshTeachingItems(bool chooseVisible = false)
    {
        if(!loaded || bindingTeachingItems) return;
        int id = (int)bookItem.Value;
        var all = TeachingItemRows();
        string query = teachingSearch.Text.Trim();
        var visible = all.Where(i => (query.Length == 0 || i.Name.Contains(query,StringComparison.OrdinalIgnoreCase) || i.Id.ToString().Contains(query))
            && (teachingFilter.SelectedIndex != 1 || i.RecipeCount > 0)
            && (teachingFilter.SelectedIndex != 2 || i.RecipeCount == 0)).ToList();
        if(id == 0 || (chooseVisible && visible.All(i => i.Id != id))) id = visible.FirstOrDefault()?.Id ?? 0;
        bindingTeachingItems = true;
        try {
            if(teachingItems.DataSource is not BindingList<TeachingItemRow> current || !current.SequenceEqual(visible))
                teachingItems.DataSource = new BindingList<TeachingItemRow>(visible);
            teachingItems.ClearSelection(); teachingItems.CurrentCell = null;
            foreach(DataGridViewRow row in teachingItems.Rows)
                if(row.DataBoundItem is TeachingItemRow item && item.Id == id) { teachingItems.CurrentCell = row.Cells["Id"]; break; }
            bookItem.Value = id;
            teachingCount.Text = all.Count == 0 ? "No recipe items found. Create a recipe-book item to begin." :
                $"{visible.Count:N0} of {all.Count:N0} recipe items • {all.Count(i => i.RecipeCount > 0):N0} assigned";
        } finally { bindingTeachingItems = false; }
        RefreshBook();
    }

    private void RevealTeachingItem()
    {
        if(bindingTeachingItems || !loaded) return;
        int id = (int)bookItem.Value;
        bool visible = teachingItems.Rows.Cast<DataGridViewRow>().Any(row => row.DataBoundItem is TeachingItemRow item && item.Id == id);
        if(!visible && TeachingItemRows().Any(item => item.Id == id)) {
            bindingTeachingItems = true;
            try { teachingSearch.Clear(); teachingFilter.SelectedIndex = 0; }
            finally { bindingTeachingItems = false; }
        }
        RefreshTeachingItems();
    }

    private void RefreshBook()
    {
        int id = (int)bookItem.Value;
        if(id == 0) {
            bookName.Text = "Select a recipe item to inspect or assign its recipes.";
            bookRecipes.Text = loaded && teachingItems.Rows.Count == 0 ? "No recipe items match the current search or filter." : "Select an item from the list.";
            return;
        }
        bookName.Text = $"{catalog.ItemName(id)} — " + (catalog.Items.TryGetValue(id,out var item) && item.Enabled && item.Manual && !item.Timed
            ? "supported teaching item" : "not a usable teaching item; check its definition in Item Editor");
        var taught = catalog.Recipes.Where(r => r.Manuals.Any(m => m.ItemId == id)).OrderBy(r => r.Profession).ThenBy(r => r.RequiredSkill).ThenBy(r => r.Id).ToList();
        bookRecipes.Text = taught.Count == 0 ? "No recipes assigned to this item. Use Assign recipes to choose what it teaches." :
            string.Join("\r\n",taught.Select(r => $"{r.Profession} • #{r.Id} {catalog.ItemName(r.OutputItem)} • requires skill {r.RequiredSkill}{(r.Enabled ? "" : " • retired")}"));
    }
}
