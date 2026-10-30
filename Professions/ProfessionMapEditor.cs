using System.Data;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Draft-only spatial authoring. Uses the same world-map transform as NPCEditor.</summary>
public sealed class ProfessionMapEditor : Form
{
    readonly bool herbs;
    readonly int parent;
    readonly IList<ProfessionRow> destination;
    readonly DataTable points = new();
    readonly DataGridView grid = new() { Dock=DockStyle.Fill, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows=false, MultiSelect=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect };
    readonly MapCanvas canvas = new();
    readonly NumericUpDown polygon = new() { Minimum=0,Maximum=31,Width=60 };
    readonly Label info = new() { AutoSize=true, MaximumSize=new(1060,0) };
    Bitmap? background;
    float unitsPerPixel=3.0003f,zoom=1,panX=15,panY=15;
    Point last; bool panning, moving; int nextId;
    const string X="a_pos_x",Z="a_pos_z";

    public static bool EditHerbs(IWin32Window owner,Main main,int zone,int populationId,IList<ProfessionRow> rows)
    { using var dialog=new ProfessionMapEditor(main,zone,populationId,rows,true); return dialog.ShowDialog(owner)==DialogResult.OK; }
    public static bool EditFishing(IWin32Window owner,Main main,int zone,int waterId,IList<ProfessionRow> rows)
    { using var dialog=new ProfessionMapEditor(main,zone,waterId,rows,false); return dialog.ShowDialog(owner)==DialogResult.OK; }
    public static float WorldUnitsPerMapPixel(float ratio) => ratio>0 ? Defs.m_fZoomDetail/ratio*(ratio>Defs.WorldRatio?4:1) : 3.0003f;
    public static PointF WorldToMap(decimal x,decimal z,float ratio) => new((float)x/WorldUnitsPerMapPixel(ratio),(float)z/WorldUnitsPerMapPixel(ratio));
    public static PointF MapToWorld(PointF point,float ratio) => new(point.X*WorldUnitsPerMapPixel(ratio),point.Y*WorldUnitsPerMapPixel(ratio));

    ProfessionMapEditor(Main main,int zone,int parent,IList<ProfessionRow> rows,bool herbs)
    {
        this.parent=parent;destination=rows;this.herbs=herbs;
        Text=herbs?"Herbalism candidate spawn points":"Fishing water boundary pieces";
        Size=new(1120,770);MinimumSize=new(900,600);StartPosition=FormStartPosition.CenterParent;
        foreach(var field in herbs?new[]{"a_index",X,Z,"a_y_layer","a_pos_r","a_enable"}:new[]{"a_polygon_index","a_vertex_index",X,Z}) points.Columns.Add(field,typeof(string));
        string key=herbs?"a_population_index":"a_water_index";
        foreach(var row in rows.Where(r=>r.Int(key)==parent)) points.Rows.Add(points.Columns.Cast<DataColumn>().Select(c=>(object)row.Get(c.ColumnName)).ToArray());
        nextId=rows.Count==0?1:rows.Max(r=>r.Int("a_index"))+1;
        grid.DataSource=points;
        foreach(DataGridViewColumn column in grid.Columns) column.HeaderText=column.Name switch {"a_index"=>"ID","a_pos_x"=>"X","a_pos_z"=>"Z","a_y_layer"=>"Layer","a_pos_r"=>"Heading","a_enable"=>"Enabled (0/1)","a_polygon_index"=>"Piece","a_vertex_index"=>"Order",_=>column.Name};
        if(herbs)
        {
            grid.Columns["a_index"].ReadOnly=true;
            int enabledIndex=grid.Columns["a_enable"].Index;
            grid.Columns.Remove("a_enable");
            grid.Columns.Insert(enabledIndex,new DataGridViewCheckBoxColumn { Name="a_enable",DataPropertyName="a_enable",HeaderText="Enabled",TrueValue="1",FalseValue="0",ValueType=typeof(string),ThreeState=false });
            grid.CurrentCellDirtyStateChanged+=(_,_)=>{if(grid.IsCurrentCellDirty&&grid.CurrentCell is DataGridViewCheckBoxCell)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        }
        else grid.Columns["a_vertex_index"].ReadOnly=true;
        var split=new SplitContainer {Size=new(1100,650),Dock=DockStyle.Fill,SplitterDistance=690,Panel1MinSize=430,Panel2MinSize=300};
        split.Panel1.Controls.Add(canvas);split.Panel2.Controls.Add(grid);
        var top=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,Padding=new(8)};
        var add=Button("Add point",()=>Add(ScreenToWorld(new(canvas.Width/2,canvas.Height/2))));
        top.Controls.Add(add);top.Controls.Add(Button("Delete selected",Delete));
        if(!herbs){top.Controls.Add(new Label{Text="Active piece",AutoSize=true,Padding=new(0,6,0,0)});top.Controls.Add(polygon);top.Controls.Add(Button("New piece",()=>polygon.Value=Math.Min(31,points.Rows.Cast<DataRow>().Select(r=>ReadInt(r,"a_polygon_index")).DefaultIfEmpty(-1).Max()+1)));}
        if(!herbs){top.Controls.Add(Button("Earlier",()=>MoveOrder(-1)));top.Controls.Add(Button("Later",()=>MoveOrder(1)));}
        top.Controls.Add(Button("Fit",Fit));top.Controls.Add(Button("Validate",()=>ValidateDraft(true)));
        top.Controls.Add(new Label{Text="Right-click adds • drag a point moves it • middle-drag pans • wheel zooms",AutoSize=true,Padding=new(4,6,0,0)});
        var bottom=new FlowLayoutPanel {Dock=DockStyle.Bottom,AutoSize=true,Padding=new(8)};
        bottom.Controls.Add(Button("Use draft changes",()=>{if(ValidateDraft(true)){Commit();DialogResult=DialogResult.OK;Close();}}));bottom.Controls.Add(Button("Cancel",()=>Close()));
        var notice=new Panel{Dock=DockStyle.Bottom,Height=56,Padding=new(8)};notice.Controls.Add(info);
        Controls.Add(split);Controls.Add(notice);Controls.Add(bottom);Controls.Add(top);
        try {var image=main.GetWorldMap(zone+"0");background=image.Item1;if(image.Item2>0){unitsPerPixel=WorldUnitsPerMapPixel(image.Item2);info.Text="World-map placement preview. Terrain collision, peace flags and actual water require server/map validation.";}else{background.Dispose();background=null;info.Text="No world map found: coordinate canvas only. Grid coordinates remain exact; no walkability or water validation is implied.";}}
        catch(Exception e){info.Text="Map unavailable; coordinate canvas only. "+e.Message;}
        canvas.Paint+=PaintMap;canvas.MouseWheel+=Zoom;canvas.MouseDown+=Down;canvas.MouseMove+=MovePoint;canvas.MouseUp+=(_,_)=>{panning=moving=false;canvas.Capture=false;};canvas.MouseEnter+=(_,_)=>canvas.Focus();
        grid.CellValueChanged+=(_,_)=>canvas.Invalidate();grid.SelectionChanged+=(_,_)=>canvas.Invalidate();grid.DataError+=(_,e)=>{e.ThrowException=false;};
        Shown+=(_,_)=>Fit();
    }
    static Button Button(string text,Action click){var b=new Button{Text=text,AutoSize=true};b.Click+=(_,_)=>click();return b;}
    static decimal Read(DataRow row,string field)=>decimal.TryParse(row[field].ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:decimal.MinValue;
    static int ReadInt(DataRow row,string field)=>int.TryParse(row[field].ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:-1;
    PointF ScreenToWorld(Point p)=>new((p.X-panX)/zoom*unitsPerPixel,(p.Y-panY)/zoom*unitsPerPixel);
    PointF Screen(DataRow r)=>new((float)Read(r,X)/unitsPerPixel*zoom+panX,(float)Read(r,Z)/unitsPerPixel*zoom+panY);
    void Add(PointF world)
    {
        if(world.X<0||world.Y<0)return;
        if(herbs){if(nextId<=0||nextId==int.MaxValue){MessageBox.Show(this,"No available point ID.");return;}points.Rows.Add((nextId++).ToString(),world.X.ToString("0.00",CultureInfo.InvariantCulture),world.Y.ToString("0.00",CultureInfo.InvariantCulture),"0","0","1");}
        else{var piece=(int)polygon.Value;int count=points.Rows.Cast<DataRow>().Count(r=>ReadInt(r,"a_polygon_index")==piece);if(count>=64){MessageBox.Show(this,"A piece supports at most 64 vertices.");return;}points.Rows.Add(piece.ToString(),count.ToString(),world.X.ToString("0.00",CultureInfo.InvariantCulture),world.Y.ToString("0.00",CultureInfo.InvariantCulture));}
        if(grid.Rows.Count>0)grid.CurrentCell=grid.Rows[grid.Rows.Count-1].Cells[0];canvas.Invalidate();
    }
    void Delete(){if(grid.CurrentRow?.DataBoundItem is DataRowView view){points.Rows.Remove(view.Row);Compact();canvas.Invalidate();}}
    void MoveOrder(int direction){if(grid.CurrentRow?.DataBoundItem is not DataRowView v)return;int index=points.Rows.IndexOf(v.Row);int target=index+direction;if(target<0||target>=points.Rows.Count||ReadInt(v.Row,"a_polygon_index")!=ReadInt(points.Rows[target],"a_polygon_index"))return;var values=v.Row.ItemArray;points.Rows.RemoveAt(index);var row=points.NewRow();row.ItemArray=values;points.Rows.InsertAt(row,target);Compact();grid.CurrentCell=grid.Rows[target].Cells[0];canvas.Invalidate();}
    void Compact(){if(!herbs)foreach(var group in points.Rows.Cast<DataRow>().GroupBy(r=>ReadInt(r,"a_polygon_index"))) {int i=0;foreach(var row in group)row["a_vertex_index"]=(i++).ToString(CultureInfo.InvariantCulture);}}
    void Down(object? sender,MouseEventArgs e)
    {
        last=e.Location;if(e.Button==MouseButtons.Middle){panning=true;canvas.Capture=true;return;}
        if(e.Button==MouseButtons.Right){Add(ScreenToWorld(e.Location));return;}
        if(e.Button!=MouseButtons.Left)return;
        var nearest=points.Rows.Cast<DataRow>().Select(r=>(r,p:Screen(r))).Where(v=>Math.Abs(v.p.X-e.X)<10&&Math.Abs(v.p.Y-e.Y)<10).OrderBy(v=>Math.Abs(v.p.X-e.X)+Math.Abs(v.p.Y-e.Y)).FirstOrDefault();
        if(nearest.r!=null){int index=points.Rows.IndexOf(nearest.r);grid.CurrentCell=grid.Rows[index].Cells[0];moving=true;canvas.Capture=true;}
    }
    void MovePoint(object? sender,MouseEventArgs e)
    {
        if(panning){panX+=e.X-last.X;panY+=e.Y-last.Y;last=e.Location;canvas.Invalidate();}
        if(moving&&grid.CurrentRow?.DataBoundItem is DataRowView view){var p=ScreenToWorld(e.Location);view.Row[X]=Math.Max(0,p.X).ToString("0.00",CultureInfo.InvariantCulture);view.Row[Z]=Math.Max(0,p.Y).ToString("0.00",CultureInfo.InvariantCulture);canvas.Invalidate();}
    }
    void Zoom(object? sender,MouseEventArgs e){var world=ScreenToWorld(e.Location);zoom=Math.Clamp(zoom*(e.Delta>0?1.2f:1/1.2f),.05f,30);panX=e.X-world.X/unitsPerPixel*zoom;panY=e.Y-world.Y/unitsPerPixel*zoom;canvas.Invalidate();}
    void Fit()
    {
        var valid=points.Rows.Cast<DataRow>().Where(r=>Read(r,X)>=0&&Read(r,Z)>=0).ToArray();
        float left=0,top=0,right=background?.Width??512,bottom=background?.Height??512;
        if(background==null&&valid.Length>0){left=(float)valid.Min(r=>Read(r,X))/unitsPerPixel;top=(float)valid.Min(r=>Read(r,Z))/unitsPerPixel;right=(float)valid.Max(r=>Read(r,X))/unitsPerPixel;bottom=(float)valid.Max(r=>Read(r,Z))/unitsPerPixel;}
        zoom=Math.Clamp(Math.Min((canvas.Width-50)/Math.Max(30,right-left),(canvas.Height-50)/Math.Max(30,bottom-top)),.05f,30);panX=25-left*zoom;panY=25-top*zoom;canvas.Invalidate();
    }
    void PaintMap(object? sender,PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.FromArgb(28,32,35));
        if(background!=null)g.DrawImage(background,panX,panY,background.Width*zoom,background.Height*zoom);
        else{using var pen=new Pen(Color.FromArgb(60,70,74));for(float x=panX%80;x<canvas.Width;x+=80)g.DrawLine(pen,x,0,x,canvas.Height);for(float y=panY%80;y<canvas.Height;y+=80)g.DrawLine(pen,0,y,canvas.Width,y);}
        if(!herbs)foreach(var group in points.Rows.Cast<DataRow>().Where(r=>Read(r,X)>=0&&Read(r,Z)>=0).GroupBy(r=>ReadInt(r,"a_polygon_index"))) {var p=group.Select(Screen).ToArray();if(p.Length>=3){using var fill=new SolidBrush(Color.FromArgb(50,65,170,220));g.FillPolygon(fill,p);}if(p.Length>=2){using var pen=new Pen(Color.SkyBlue,2);g.DrawLines(pen,p);g.DrawLine(pen,p[^1],p[0]);}}
        foreach(DataRow r in points.Rows){if(Read(r,X)<0||Read(r,Z)<0)continue;var p=Screen(r);bool selected=grid.CurrentRow?.DataBoundItem is DataRowView v&&v.Row==r;using var brush=new SolidBrush(selected?Color.Gold:herbs&&ReadInt(r,"a_enable")==0?Color.Gray:herbs?Color.LimeGreen:Color.Cyan);g.FillEllipse(brush,p.X-5,p.Y-5,10,10);g.DrawString(herbs?r["a_index"].ToString():r["a_polygon_index"]+":"+r["a_vertex_index"],Font,Brushes.White,p.X+7,p.Y-7);}
    }
    bool ValidateDraft(bool show)
    {
        grid.EndEdit();Compact();string? error=null;
        foreach(DataRow r in points.Rows)if(Read(r,X)<0||Read(r,Z)<0||Read(r,X)>100000||Read(r,Z)>100000){error="Coordinates must be finite numbers from 0 to 100,000 world metres.";break;}
        if(error==null&&herbs){var ids=new HashSet<int>();var locations=new HashSet<(int,decimal,decimal)>();foreach(DataRow r in points.Rows){int layer=ReadInt(r,"a_y_layer");if(ReadInt(r,"a_index")<=0||!ids.Add(ReadInt(r,"a_index"))||layer<0||layer>126||layer%2!=0||Read(r,"a_pos_r")==decimal.MinValue||ReadInt(r,"a_enable") is <0 or >1||!locations.Add((layer,Read(r,X),Read(r,Z)))){error="Use unique IDs/positions, even layer 0–126, finite heading and Enabled 0/1.";break;}}}
        if(error==null&&!herbs){if(points.Rows.Count==0)error="A water body needs at least one polygon piece.";else if(points.Rows.Count>8192)error="At most 8,192 vertices are supported globally.";else foreach(var group in points.Rows.Cast<DataRow>().GroupBy(r=>ReadInt(r,"a_polygon_index"))){if(group.Key<0||group.Key>31){error="Piece must be 0–31.";break;}error=ProfessionRules.ValidatePolygon(group.Select(r=>(Read(r,X),Read(r,Z))));if(error!=null)break;}}
        if(show)MessageBox.Show(this,error??"Geometry is valid. Terrain, accessibility, water and global catalogue constraints are checked separately.","Spatial validation",MessageBoxButtons.OK,error==null?MessageBoxIcon.Information:MessageBoxIcon.Warning);
        return error==null;
    }
    public static string? ValidatePolygon(IReadOnlyList<PointF> points)
    {
        if(points.Count<3||points.Count>64)return "Each piece needs 3–64 vertices.";
        var p=points.Select(v=>(x:(long)Math.Round(v.X*100d,MidpointRounding.AwayFromZero),z:(long)Math.Round(v.Y*100d,MidpointRounding.AwayFromZero))).ToArray();
        if(points.Any(v=>!float.IsFinite(v.X)||!float.IsFinite(v.Y)||v.X<0||v.Y<0||v.X>100000||v.Y>100000))return "Polygon coordinate is invalid.";
        long Cross(int a,int b,int c)=>(p[b].x-p[a].x)*(p[c].z-p[a].z)-(p[b].z-p[a].z)*(p[c].x-p[a].x);
        bool On(int a,int b,int c)=>Cross(a,b,c)==0&&p[c].x>=Math.Min(p[a].x,p[b].x)&&p[c].x<=Math.Max(p[a].x,p[b].x)&&p[c].z>=Math.Min(p[a].z,p[b].z)&&p[c].z<=Math.Max(p[a].z,p[b].z);
        long area=0;for(int i=0;i<p.Length;i++){int n=(i+1)%p.Length;area+=p[i].x*p[n].z-p[n].x*p[i].z;for(int j=i+1;j<p.Length;j++){if(p[i]==p[j])return "Repeated polygon vertices are not allowed.";if(j==n||(i==0&&j==p.Length-1))continue;int m=(j+1)%p.Length;long a=Cross(i,n,j),b=Cross(i,n,m),c=Cross(j,m,i),d=Cross(j,m,n);if((Math.Sign(a)!=Math.Sign(b)&&Math.Sign(c)!=Math.Sign(d)&&a!=0&&b!=0&&c!=0&&d!=0)||On(i,n,j)||On(i,n,m)||On(j,m,i)||On(j,m,n))return "Polygon edges cannot intersect.";}}
        return area==0?"Polygon must enclose a nonzero area.":null;
    }
    void Commit()
    {
        string key=herbs?"a_population_index":"a_water_index";
        foreach(var row in destination.Where(r=>r.Int(key)==parent).ToArray())destination.Remove(row);
        foreach(DataRow r in points.Rows){var row=new ProfessionRow();row.Set(key,parent);foreach(DataColumn c in points.Columns)row.Set(c.ColumnName,r[c].ToString()??"");destination.Add(row);}
    }
    protected override void Dispose(bool disposing){if(disposing)background?.Dispose();base.Dispose(disposing);}
    sealed class MapCanvas:Control{public MapCanvas(){Dock=DockStyle.Fill;DoubleBuffered=true;ResizeRedraw=true;TabStop=true;}}
}
