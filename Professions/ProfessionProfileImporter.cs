using System.Text;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LastChaos_ToolBoxNG.Professions;

/// <summary>Imports authored offline interaction geometry into the unsaved draft only.</summary>
public static class ProfessionProfileImporter
{
    public static bool Import(IWin32Window owner,ProfessionRow profile,string? gameRoot=null)
    {
        using var dialog=new ImportDialog(profile,gameRoot);
        return dialog.ShowDialog(owner)==DialogResult.OK;
    }
    public static string ExtractHex(string sql)
    {
        var match=Regex.Match(sql,@"VALUES\s*\(\s*\d+\s*,\s*'([0-9A-Fa-f]+)'\s*\)",RegexOptions.CultureInvariant);
        if(!match.Success)throw new InvalidDataException("The importer did not produce a profile geometry value.");
        var hex=match.Groups[1].Value;
        var error=ProfessionRules.ValidateProfile(hex);
        if(error!=null)throw new InvalidDataException(error);
        return hex.ToUpperInvariant();
    }
    public static IReadOnlyList<string> ReadBundleSurfaces(string path)
    {
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);if(Encoding.ASCII.GetString(reader.ReadBytes(8))!="LCVDIST2"||reader.ReadUInt32()!=2)throw new InvalidDataException("Select an engine LCVDIST2 distance bundle.");
        string Text(){uint n=reader.ReadUInt32();if(n>65536||n>stream.Length-stream.Position)throw new InvalidDataException("Invalid bundle text length.");return Encoding.ASCII.GetString(reader.ReadBytes((int)n));}
        _=Text();_=reader.ReadUInt64();if(reader.ReadUInt32()!=0)throw new InvalidDataException("Interaction profiles require source LOD0.");uint surfaces=reader.ReadUInt32();if(surfaces>4096)throw new InvalidDataException("Too many bundle surfaces.");var names=new List<string>();
        for(uint i=0;i<surfaces;i++){names.Add(Text());_=reader.ReadUInt64();_=reader.ReadUInt32();_=reader.ReadUInt32();uint vertices=reader.ReadUInt32(),indices=reader.ReadUInt32();long bytes=(long)vertices*18*4+(long)indices*2;if(bytes>stream.Length-stream.Position)throw new InvalidDataException("Truncated distance bundle.");stream.Seek(bytes,SeekOrigin.Current);}
        if(stream.Position!=stream.Length)throw new InvalidDataException("Unexpected trailing bundle data.");return names;
    }
    public static void ValidateMetadata(string hex,string metadata)
    {
        var bytes=Convert.FromHexString(hex);if(bytes.Length<40)throw new InvalidDataException("Truncated geometry header.");
        int length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4,4)));if(length>192||bytes.Length<40+length)throw new InvalidDataException("Truncated geometry source.");
        ulong hash=BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8+length,8));uint version=2166136261;
        unchecked{foreach(byte b in bytes)version=(version^b)*16777619;}if(version==0)version=1;
        using var json=JsonDocument.Parse(metadata);var root=json.RootElement;
        if(root.GetProperty("source_hash").GetString()!=hash.ToString("x16")||root.GetProperty("version").GetUInt32()!=version||root.GetProperty("bytes").GetInt32()!=bytes.Length||root.GetProperty("vertices").GetUInt32()!=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(32+length,4))||root.GetProperty("triangles").GetUInt32()!=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(36+length,4)))throw new InvalidDataException("Imported source hash, version or geometry counts disagree with metadata.");
    }
    sealed class ImportDialog:Form
    {
        readonly ProfessionRow row;
        readonly TextBox root=new(){Dock=DockStyle.Fill},bundle=new(){Dock=DockStyle.Fill},summary=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        readonly ComboBox surface=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDown};
        readonly Button run=new(){Text="Generate preview",AutoSize=true},apply=new(){Text="Use imported geometry",AutoSize=true,Enabled=false};
        string? generated;
        CancellationTokenSource? active;bool closeRequested;
        public ImportDialog(ProfessionRow row,string? gameRoot)
        {
            this.row=row;Text="Import Mining interaction profile";Size=new(760,520);MinimumSize=new(640,430);StartPosition=FormStartPosition.CenterParent;root.Text=gameRoot??"";
            var fields=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=3,AutoSize=true,Padding=new(10)};fields.ColumnStyles.Add(new(SizeType.Absolute,120));fields.ColumnStyles.Add(new(SizeType.Percent,100));fields.ColumnStyles.Add(new(SizeType.Absolute,90));
            Add("Game checkout",root,()=>{using var d=new FolderBrowserDialog{Description="Select the game checkout containing tools/gathering/ImportSurfaceProfile.py"};if(d.ShowDialog(this)==DialogResult.OK)root.Text=d.SelectedPath;});
            Add("LOD0 bundle (.vkd)",bundle,()=>{using var d=new OpenFileDialog{Filter="Engine distance bundle (*.vkd)|*.vkd|All files|*.*"};if(d.ShowDialog(this)==DialogResult.OK)bundle.Text=d.FileName;});
            Add("Solid surface",surface,null);
            bundle.TextChanged+=(_,_)=>{surface.Items.Clear();if(!File.Exists(bundle.Text))return;try{surface.Items.AddRange(ReadBundleSurfaces(bundle.Text).Cast<object>().ToArray());if(surface.Items.Count>0)surface.SelectedIndex=0;}catch(Exception e){summary.Text=e.Message;}};
            void Add(string name,Control control,Action? browse){int r=fields.RowCount++;fields.Controls.Add(new Label{Text=name,AutoSize=true,Padding=new(0,5,0,0)},0,r);fields.Controls.Add(control,1,r);if(browse!=null){var b=new Button{Text="Browse...",AutoSize=true};b.Click+=(_,_)=>browse();fields.Controls.Add(b,2,r);}}
            var help=new Label{Dock=DockStyle.Top,AutoSize=true,Padding=new(10),MaximumSize=new(710,0),Text="Use a LOD0 distance bundle exported by the engine asset tool and the exact solid surface name. The selected profile ID stays unchanged. Only a validated preview is copied into this editor draft; no SQL is executed."};
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,Padding=new(10)};buttons.Controls.Add(run);buttons.Controls.Add(apply);var cancel=new Button{Text="Cancel",AutoSize=true};cancel.Click+=(_,_)=>{if(active!=null){closeRequested=true;active.Cancel();}else Close();};buttons.Controls.Add(cancel);
            summary.Margin=new(10);Controls.Add(summary);Controls.Add(help);Controls.Add(fields);Controls.Add(buttons);
            run.Click+=async(_,_)=>await Generate();apply.Click+=(_,_)=>{if(generated==null)return;row.Set("a_geometry_hex",generated);DialogResult=DialogResult.OK;Close();};
        }
        protected override void OnFormClosing(FormClosingEventArgs e){if(active!=null){closeRequested=true;active.Cancel();e.Cancel=true;}base.OnFormClosing(e);}
        async Task Generate()
        {
            generated=null;apply.Enabled=false;run.Enabled=false;
            string temporary=Path.Combine(Path.GetTempPath(),"lc-profile-"+Guid.NewGuid().ToString("N"));
            try
            {
                string script=Path.Combine(root.Text,"tools","gathering","ImportSurfaceProfile.py");if(!File.Exists(script))throw new InvalidDataException("Select the game checkout containing ImportSurfaceProfile.py.");
                if(!File.Exists(bundle.Text)||string.IsNullOrWhiteSpace(surface.Text)||row.Int("a_index")<=0)throw new InvalidDataException("Select a valid LOD0 bundle, surface name and positive profile ID.");
                Directory.CreateDirectory(temporary);string sql=Path.Combine(temporary,"profile.sql"),metadata=Path.Combine(temporary,"metadata.json");
                var start=new ProcessStartInfo{FileName="python",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string arg in new[]{script,"--bundle",Path.GetFullPath(bundle.Text),"--surface",surface.Text,"--profile",row.Int("a_index").ToString(),"--output",sql,"--metadata",metadata})start.ArgumentList.Add(arg);
                using var process=Process.Start(start)??throw new IOException("Could not start Python.");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));active=timeout;
                try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);throw new IOException("Profile import timed out; no draft data changed.");}
                string output=await stdout,error=await stderr;if(process.ExitCode!=0)throw new InvalidDataException(error+output);
                generated=ExtractHex(await File.ReadAllTextAsync(sql));
                string report=await File.ReadAllTextAsync(metadata);ValidateMetadata(generated,report);
                using var json=JsonDocument.Parse(report);summary.Text="Validated draft geometry\r\n"+JsonSerializer.Serialize(json.RootElement,new JsonSerializerOptions{WriteIndented=true})+"\r\n\r\nThe source hash identifies the offline mesh bundle. Deploy matching model assets separately. New geometry is not saved until the editor's Save.";apply.Enabled=true;
            }
            catch(Exception e){if(!closeRequested){summary.Text=e.Message;MessageBox.Show(this,e.Message,"Profile import",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
            finally{active=null;run.Enabled=true;try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);}catch(IOException){}catch(UnauthorizedAccessException){}if(closeRequested)Close();}
        }
    }
}
