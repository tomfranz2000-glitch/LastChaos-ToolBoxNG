namespace LastChaos_ToolBoxNG;

public partial class NPCEditor
{
    private int professionNpcToSelect;
    public NPCEditor(Main host,int npcId):this(host) { professionNpcToSelect=npcId; }
    private void ApplyProfessionNpcNavigation()
    {
        if(professionNpcToSelect<=0)return;
        int target=professionNpcToSelect;professionNpcToSelect=0;
        for(int i=0;i<MainList.Items.Count;i++)
            if(MainList.Items[i] is Main.ListBoxItem item&&item.ID==target) {MainList.SelectedIndex=i;return;}
        MessageBox.Show(this,$"NPC {target} is not in the loaded catalogue. Check the selected database and NPC definition.","Profession source");
    }
}
