namespace LastChaos_ToolBoxNG;

public sealed partial class CraftingMasteryEditor
{
    private static string CompactSkillRanges(IEnumerable<int> values)
    {
        var ranges=new List<string>();int first=-1,last=-1;
        foreach(int value in values) {
            if(first<0) { first=last=value;continue; }
            if(value==last+1) {last=value;continue;}
            ranges.Add(first==last?first.ToString():$"{first}–{last}");first=last=value;
        }
        if(first>=0)ranges.Add(first==last?first.ToString():$"{first}–{last}");
        return ranges.Count==0?"None":string.Join(", ",ranges);
    }
}
