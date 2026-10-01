using LastChaos_ToolBoxNG.Crafting;

namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private string? exportCheckout;
    private async Task ExportAsync()
    {
        if (!loaded || !CommitGrids()) return;
        if (dirty) { status.Text = "Save or revert the draft before exporting database content."; return; }
        using var folder = new FolderBrowserDialog { Description = "Choose the game Git checkout for crafting seed export", UseDescriptionForTitle = true, InitialDirectory = exportCheckout ?? "" };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        exportCheckout = folder.SelectedPath;
        await RunAsync(async () => {
            status.Text = "Verifying the database and exporting eight crafting content tables…";
            string connection = ConnectionString(main), locale = main.pSettings.WorkLocale, expected = original.Fingerprint();
            await Task.Run(() => CraftingSeedExport.Export(exportCheckout, connection, locale, expected));
            status.Text = "Exported eight crafting seeds into the game checkout. Review and commit that Git diff; new item definitions need their separate item export.";
        });
    }
    private void ChooseEmblem(MastersRank rank)
    {
        string directory = Path.Combine(main.pSettings.ClientPath, "Data", "Interface");
        if (!Directory.Exists(directory)) { status.Text = "Set the toolbox Client Path to a client with Data/Interface textures."; return; }
        using var dialog = new Form { Text = "Choose a rank emblem — first-frame preview", Size = new(820,620), StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Filter .tex filenames" };
        var image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(42,42,42) };
        var message = new Label { AutoSize = true, MaximumSize = new(340,0) };
        string[] names = Directory.EnumerateFiles(directory, "*.tex").Select(p => Path.GetFileName(p)!).Where(n => n.Length <= 63 && Regex.IsMatch(n, "^[A-Za-z0-9_-]+[.]tex$")).Order().ToArray();
        void Filter() { list.Items.Clear(); list.Items.AddRange(names.Where(n => n.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray()); }
        search.TextChanged += (_, _) => Filter();
        list.SelectedIndexChanged += (_, _) => {
            image.Image?.Dispose(); image.Image = null;
            if (list.SelectedItem is not string name) return;
            try { image.Image = CraftingEmblem.Read(Path.Combine(directory, name)); message.Text = $"{name}\r\n{image.Image.Width} × {image.Image.Height}"; }
            catch (Exception e) { message.Text = name + "\r\nPreview unavailable: " + e.Message; }
        };
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(790,550), SplitterDistance = 420 };
        split.Panel1.Controls.Add(Vertical(search, list));
        split.Panel2.Controls.Add(Vertical(message, Button("Use this emblem", () => { if (list.SelectedItem != null) dialog.DialogResult = DialogResult.OK; }), image));
        dialog.Controls.Add(split); Filter(); list.SelectedItem = rank.Emblem;
        try { if (dialog.ShowDialog(this) == DialogResult.OK) { rank.Emblem = (string)list.SelectedItem!; Changed(); ShowRank(); } }
        finally { image.Image?.Dispose(); image.Image = null; }
    }
}
