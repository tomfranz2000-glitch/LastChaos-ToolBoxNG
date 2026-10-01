using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG;

public partial class ItemEditor
{
    private int initialCraftingItem, craftingReferenceGeneration;
    private bool copyCraftingItem;
    private readonly TextBox craftingReferences = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    private void InitializeCraftingLinks(int item, bool copy)
    {
        initialCraftingItem = item; copyCraftingItem = copy;
        var group = new GroupBox { Text = "Crafting & Mastery — saved recipe references (including retired)",
            Location = new(12, GeneralPanel.Controls.Cast<Control>().Select(c => c.Bottom).DefaultIfEmpty().Max() + 12), Size = new(700,180) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        var open = new Button { Text = "Recipes taught / Assign recipes", AutoSize = true };
        open.Click += (_, _) => { if (pTempItemRow != null) new CraftingMasteryEditor(pMain, Convert.ToInt32(pTempItemRow["a_index"])).Show(this); };
        var refresh = new Button { Text = "Refresh references", AutoSize = true };
        refresh.Click += (_, _) => RefreshCraftingLinks();
        buttons.Controls.AddRange([open, refresh]); group.Controls.Add(craftingReferences); group.Controls.Add(buttons); GeneralPanel.Controls.Add(group);
    }
    private void ApplyCraftingNavigation()
    {
        if (initialCraftingItem <= 0) return;
        int id = initialCraftingItem; initialCraftingItem = 0;
        for (int n = 0; n < MainList.Items.Count; n++)
            if (MainList.Items[n] is Main.ListBoxItem item && item.ID == id) {
                MainList.SelectedIndex = n;
                if (copyCraftingItem) { copyCraftingItem = false; btnCopy_Click(this, EventArgs.Empty); }
                return;
            }
        MessageBox.Show(this, $"Item {id} is not in the loaded item list. Reload the Item Editor after saving a new item.", "Crafting item");
    }
    private async void RefreshCraftingLinks()
    {
        if (pTempItemRow == null) return;
        int generation = ++craftingReferenceGeneration;
        int id = Convert.ToInt32(pTempItemRow["a_index"]);
        string connection = CraftingMasteryEditor.ConnectionString(pMain);
        try {
            string locale=pMain.pSettings.WorkLocale;
            var references = await Task.Run(() => CraftingItemReferences.Read(connection, id, locale));
            if (IsDisposed || generation != craftingReferenceGeneration) return;
            craftingReferences.Text = references.Count == 0 ? "No saved crafting references. Use Assign recipes to create a teaching link." : string.Join("\r\n", references);
        } catch (Exception e) { if (!IsDisposed && generation == craftingReferenceGeneration) craftingReferences.Text = "Unable to load references: " + e.Message; }
    }
    private bool TryCraftingDeleteGuard(int item, out string sql)
    {
        sql = "";
        try {
            string connection = CraftingMasteryEditor.ConnectionString(pMain);
            var uses = CraftingItemReferences.Read(connection, item, pMain.pSettings.WorkLocale);
            if (uses.Count > 0) {
                MessageBox.Show(this, "This item is referenced by crafting. Remove or replace these references in Crafting & Mastery before deleting it:\r\n\r\n" + string.Join("\r\n", uses), "Item is used by crafting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            using var db = new MySqlConnection(connection); db.Open();
            if (CraftingItemReferences.HasSchema(db)) sql = CraftingItemReferences.DeleteGuard(pMain.pSettings.DBData, item);
            return true;
        } catch (Exception e) { MessageBox.Show(this, "Cannot verify crafting references. Nothing was deleted.\r\n" + e.Message, "Item deletion", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }
}
