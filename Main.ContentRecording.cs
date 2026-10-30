using LastChaos_ToolBoxNG.ContentRecording;

namespace LastChaos_ToolBoxNG;

public partial class Main
{
    private Button recordContentButton = null!, contentCheckoutButton = null!;
    private Label contentRecordingStatus = null!;
    private bool recordingContent;

    private void InitializeContentRecording()
    {
        var panel = new Panel {
            Name = "ContentRecordingPanel", Location = new(270, 72), Size = new(520, 178),
            Anchor = AnchorStyles.Top | AnchorStyles.Left, BackColor = Color.FromArgb(33, 35, 36)
        };
        var heading = new Label { Text = "Content version control", Location = new(14, 12), Size = new(490, 23),
            ForeColor = Color.FromArgb(208, 203, 148), Font = new Font(Font, FontStyle.Bold) };
        var help = new Label { Text = "Save your editors first. Record saved database changes and required\nclient data in one local Git commit. Nothing is pushed or applied to the game.",
            Location = new(14, 40), Size = new(490, 39), ForeColor = Color.Gainsboro, UseMnemonic = false };
        recordContentButton = ContentButton("Record content changes", "RecordContentChanges", new(14, 87), new(245, 33));
        contentCheckoutButton = ContentButton("Choose game checkout…", "ChooseContentCheckout", new(271, 87), new(233, 33));
        contentRecordingStatus = new Label { Name = "ContentRecordingStatus", Text = "Choose the game checkout once to get started.",
            Location = new(14, 130), Size = new(490, 38), AutoEllipsis = true, ForeColor = Color.Silver, UseMnemonic = false };
        recordContentButton.Click += RecordContentChanges;
        contentCheckoutButton.Click += (_, _) => ChooseContentCheckout();
        panel.Controls.AddRange([heading, help, recordContentButton, contentCheckoutButton, contentRecordingStatus]);
        Controls.Add(panel);
    }

    private static Button ContentButton(string text, string name, Point location, Size size)
    {
        var button = new Button { Text = text, Name = name, Location = location, Size = size,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.FromArgb(208, 203, 148) };
        button.FlatAppearance.BorderColor = Color.FromArgb(91, 85, 76);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 56, 54);
        return button;
    }

    private void RefreshContentCheckoutStatus()
    {
        if (recordingContent) return;
        contentRecordingStatus.Text = string.IsNullOrWhiteSpace(pSettings.ContentCheckout)
            ? "Choose the game checkout once to get started."
            : "Game checkout: " + pSettings.ContentCheckout;
    }

    private bool ChooseContentCheckout()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose the game Git checkout (contains x64-server and x64-client).",
            UseDescriptionForTitle = true, ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(pSettings.ContentCheckout) ? pSettings.ContentCheckout : "" };
        if (picker.ShowDialog(this) != DialogResult.OK) return false;
        try {
            ContentGitRecorder.ValidateCheckout(picker.SelectedPath);
            if (!File.Exists(Path.Combine(picker.SelectedPath, "x64-server", "db", "export-seed.sh")))
                throw new InvalidOperationException("Choose the game checkout containing x64-server/db/export-seed.sh.");
            // Write succeeds before changing the in-memory setting. Older Settings.ini files stay intact.
            WriteToINI(pSettings.SettingsFile, "ContentVersioning", "GameCheckout", picker.SelectedPath);
            pSettings.ContentCheckout = picker.SelectedPath;
            RefreshContentCheckoutStatus();
            return true;
        } catch (Exception error) {
            MessageBox.Show(this, error.Message, "Game checkout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private async void RecordContentChanges(object? sender, EventArgs args)
    {
        if (recordingContent) return;
        if (string.IsNullOrWhiteSpace(pSettings.ContentCheckout) && !ChooseContentCheckout()) return;
        recordingContent = true;
        recordContentButton.Enabled = contentCheckoutButton.Enabled = false;
        // Reconnecting closes editors and replaces settings. Keep those actions unavailable while recording.
        btnReconnect.Enabled = btnReloadSettings.Enabled = false;
        var progress = new Progress<string>(message => {
            contentRecordingStatus.Text = message;
            Logger(LogTypes.Message, "Content > " + message, false);
        });
        try {
            var result = await ContentRecordingService.RecordAsync(this, progress);
            string message = result.Changed
                ? $"Recorded {result.Files} files in local commit {result.Commit[..Math.Min(12, result.Commit.Length)]}."
                : "Nothing to record. Saved content already matches Git.";
            if (!string.IsNullOrEmpty(result.Warning)) message += "\n\n" + result.Warning;
            contentRecordingStatus.Text = message;
            Logger(LogTypes.Success, "Content > " + message, false);
            MessageBox.Show(this, message, "Content version control", MessageBoxButtons.OK,
                string.IsNullOrEmpty(result.Warning) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        } catch (Exception error) {
            contentRecordingStatus.Text = "Recording failed. See the console for details.";
            string message = error.Message;
            if (error.Data["ContentSnapshotCleanupWarning"] is string warning) message += "\n\n" + warning;
            Logger(LogTypes.Error, "Content > " + message, false);
            MessageBox.Show(this, message, "Content recording failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } finally {
            recordingContent = false;
            recordContentButton.Enabled = contentCheckoutButton.Enabled = true;
            btnReconnect.Enabled = btnReloadSettings.Enabled = true;
        }
    }
}
