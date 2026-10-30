using System.Reflection;
using LastChaos_ToolBoxNG;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks++; Console.WriteLine("PASS " + description);
    }
    static object? Call(object owner, string method, params object?[] args) => owner.GetType().GetMethod(method, Private)!.Invoke(owner, args);
    [STAThread] static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: ContentRecordingUiVerification <toolbox-checkout> <evidence-directory>");
        string checkout = Path.GetFullPath(args[0]), evidence = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(evidence);
        string temporary = Path.Combine(Path.GetTempPath(), "lc-content-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using var host = new LastChaos_ToolBoxNG.Main();
            // Show the actual controls offscreen, suppressing only application startup
            // (which normally connects to the configured database and checks updates).
            host.Load -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), host, host.GetType().GetMethod("Main_Load", Private)!);
            host.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), host, host.GetType().GetMethod("Main_Shown", Private)!);
            host.ShowInTaskbar = false; host.StartPosition = FormStartPosition.Manual; host.Location = new(-20000, -20000);
            host.Show(); Application.DoEvents();
            string settings = Path.Combine(temporary, "Settings.ini");
            File.Copy(Path.Combine(checkout, "Settings.ini.dummy"), settings);
            // Simulate a settings file from before this feature, without changing any real preferences.
            string text = File.ReadAllText(settings);
            int begin = text.IndexOf("[ContentVersioning]", StringComparison.Ordinal);
            int end = text.IndexOf("[3DViewerDialog]", begin, StringComparison.Ordinal);
            File.WriteAllText(settings, text.Remove(begin, end - begin));
            host.pSettings.SettingsFile = settings;
            Call(host, "LoadSettings"); // Does not call Main_Load or connect to a database.
            Check(host.pSettings.ContentCheckout == "", "older settings load without requiring a new key");
            var record = (Button)host.Controls.Find("RecordContentChanges", true).Single();
            var choose = (Button)host.Controls.Find("ChooseContentCheckout", true).Single();
            Check(record.Visible && choose.Visible && record.Enabled && choose.Enabled, "recording and configuration actions are visible and available");
            Check(!((bool)host.GetType().GetField("recordingContent", Private)!.GetValue(host)!), "opening the toolbox never starts recording");
            string selected = Path.Combine(temporary, "Game checkout with spaces");
            host.WriteToINI(settings, "ContentVersioning", "GameCheckout", selected);
            Call(host, "LoadSettings");
            Check(host.pSettings.ContentCheckout == selected, "game checkout survives a settings reload");
            Check(File.ReadAllText(settings).Contains("MySQLHost = 127.0.0.1"), "saving checkout preserves database settings");
            foreach (var size in new[] { new Size(1024, 576), new Size(1440, 900) }) {
                host.Size = size; host.PerformLayout();
                var panel = host.Controls.Find("ContentRecordingPanel", true).Single();
                Check(host.ClientRectangle.Contains(panel.Bounds), $"recording panel fits {size.Width}x{size.Height}");
                Check(!record.Bounds.IntersectsWith(choose.Bounds), "primary and setup buttons do not overlap");
                using var image = new Bitmap(host.Width, host.Height);
                host.DrawToBitmap(image, new(0, 0, image.Width, image.Height));
                image.Save(Path.Combine(evidence, $"content-recording-{size.Width}x{size.Height}.png"));
            }
            host.GetType().GetField("recordingContent", Private)!.SetValue(host, true);
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            Call(host, "Main_FormClosing", host, closing);
            Check(closing.Cancel, "close waits for the in-flight recording operation");
            host.GetType().GetField("recordingContent", Private)!.SetValue(host, false);
            File.WriteAllText(Path.Combine(evidence, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks, liveDatabaseChanged = false, clientLaunched = false }));
            Console.WriteLine($"PASS {checks} UI checks; no live database connection or Git recording");
            return 0;
        } finally { Directory.Delete(temporary, true); }
    }
}
