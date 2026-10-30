using LastChaos_ToolBoxNG.Professions;

namespace LastChaos_ToolBoxNG;

public partial class CraftingMasteryEditor
{
    private void NavigatePermanentPotion(int item)
    {
        NavigateProfessionPage(5,InfusionSchema.Consumables);
        var browser=professionBrowsers[(5,InfusionSchema.Consumables)];browser.Search.Clear();browser.Parent.SelectedValue=0;
        browser.Selected=catalog.Professions.Rows(InfusionSchema.Consumables).FirstOrDefault(r=>r.Int("a_item_index")==item);
        RefreshProfessionBrowser(browser);
    }

    private void AddProfessionDuration(FlowLayoutPanel line, ProfessionBrowser browser, ProfessionRow row, ProfessionField field)
    {
        int stored=row.Int(field.Column), valid=Math.Clamp(stored,0,604800);
        var hours=new NumericUpDown {Name=field.Column+"_hours",Width=70,Minimum=0,Maximum=168,Value=valid/3600};
        var minutes=new NumericUpDown {Name=field.Column+"_minutes",Width=60,Minimum=0,Maximum=59,Value=valid%3600/60};
        var seconds=new NumericUpDown {Name=field.Column+"_seconds",Width=60,Minimum=0,Maximum=59,Value=valid%60};
        line.Controls.AddRange([hours,Label("h"),minutes,Label("min"),seconds,Label("sec")]);
        if(stored!=valid)line.Controls.Add(Label($"Stored value {stored} seconds is invalid; change the time to correct it."));
        void ChangedTime(object? sender,EventArgs args)
        {
            row.Set(field.Column,(int)(hours.Value*3600+minutes.Value*60+seconds.Value));
            ProfessionEntryEdited(browser);
        }
        hours.ValueChanged+=ChangedTime;minutes.ValueChanged+=ChangedTime;seconds.ValueChanged+=ChangedTime;
    }
}
