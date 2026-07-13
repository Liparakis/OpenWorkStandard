using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

#pragma warning disable SA1600, SA1602, SA1307, SA1401, SA1100, SA1407

namespace Ows.Setup;

internal sealed class SetupForm : Form {
    private enum SetupMode {
        Install,
        Uninstall
    }

    private enum SetupPage {
        Welcome,
        Options,
        Progress,
        Complete,
        Failure,
        Existing
    }

    private const int ContentLeft = 24;
    private static readonly Color Blue = Color.FromArgb(9, 99, 218);
    private static readonly Color Green = Color.FromArgb(35, 139, 61);
    private static readonly Color Ink = Color.FromArgb(24, 32, 44);
    private static readonly Color Muted = Color.FromArgb(86, 98, 116);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private SetupMode mode;
    private readonly bool purgeDataRequested;
    private SetupPage page;
    private readonly Panel rail = new();
    private readonly Panel header = new();
    private readonly Panel body = new();
    private readonly Panel actions = new();
    private readonly Label title = new();
    private readonly Label subtitle = new();
    private readonly Button backButton = new();
    private readonly Button cancelButton = new();
    private readonly Button primaryButton = new();
    private readonly TextBox installPathBox = new();
    private readonly CheckBox addCliBox = new();
    private readonly CheckBox startAgentBox = new();
    private readonly CheckBox purgeDataBox = new();
    private readonly ProgressBar progressBar = new();
    private readonly FlowLayoutPanel progressRows = new();
    private readonly List<Label> progressStatuses = [];
    private readonly List<Label> progressMarkers = [];
    private readonly Label progressPercent = new();
    private int progressIndex;
    private bool operationStarted;
    private bool reinstallRequested = true;
    private Exception? lastError;

    public SetupForm(bool uninstall, bool purgeData = false) {
        mode = uninstall ? SetupMode.Uninstall : SetupMode.Install;
        purgeDataRequested = purgeData;
        page = mode == SetupMode.Install && Program.IsInstalled() ? SetupPage.Existing : SetupPage.Welcome;

        Text = "OWS Setup";
        ClientSize = new Size(670, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        ConfigureControls();
        Render();
    }

    private void ConfigureControls() {
        rail.Dock = DockStyle.Left;
        rail.Width = 160;
        rail.BackColor = Color.FromArgb(248, 250, 253);

        var content = new Panel {
            Location = new Point(rail.Width, 0),
            Size = new Size(ClientSize.Width - rail.Width, ClientSize.Height),
            BackColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(content);
        Controls.Add(rail);
        rail.BringToFront();

        header.Dock = DockStyle.None;
        header.Height = 70;
        header.Padding = new Padding(24, 18, 24, 0);
        content.Controls.Add(header);

        body.Dock = DockStyle.None;
        body.Padding = new Padding(38, 8, 36, 8);
        content.Controls.Add(body);

        actions.Dock = DockStyle.None;
        actions.Height = 56;
        actions.Padding = new Padding(24, 8, 24, 10);
        content.Controls.Add(actions);
        LayoutContent();

        title.AutoSize = true;
        title.Font = new Font("Segoe UI Semibold", 15F);
        title.ForeColor = Ink;
        title.Location = new Point(ContentLeft, 18);
        header.Controls.Add(title);

        subtitle.AutoSize = true;
        subtitle.ForeColor = Muted;
        subtitle.Location = new Point(ContentLeft, 44);
        header.Controls.Add(subtitle);

        ConfigureButton(backButton, "Back", false);
        ConfigureButton(cancelButton, "Cancel", false);
        ConfigureButton(primaryButton, "Continue", true);

        addCliBox.Text = "Install OWS CLI to PATH";
        addCliBox.Checked = true;
        addCliBox.AutoSize = true;
        addCliBox.ForeColor = Ink;

        startAgentBox.Text = "Start OWS Agent automatically";
        startAgentBox.Checked = true;
        startAgentBox.AutoSize = true;
        startAgentBox.ForeColor = Ink;
    }

    private void Render() {
        UpdateRail();
        header.Visible = page != SetupPage.Existing;
        LayoutContent();
        body.Controls.Clear();
        actions.Controls.Clear();
        progressStatuses.Clear();
        progressMarkers.Clear();

        switch (page) {
            case SetupPage.Welcome:
                RenderWelcome();
                break;
            case SetupPage.Options:
                RenderOptions();
                break;
            case SetupPage.Progress:
                RenderProgress();
                break;
            case SetupPage.Complete:
                RenderComplete();
                break;
            case SetupPage.Failure:
                RenderFailure();
                break;
            case SetupPage.Existing:
                RenderExisting();
                break;
        }
    }

    private void LayoutContent() {
        if (body.Parent is not Panel content) {
            return;
        }

        var headerHeight = page != SetupPage.Existing ? header.Height : 0;
        header.Bounds = new Rectangle(0, 0, content.ClientSize.Width, header.Height);
        body.Bounds = new Rectangle(
            0,
            headerHeight,
            content.ClientSize.Width,
            Math.Max(0, content.ClientSize.Height - headerHeight - actions.Height)
        );
        actions.Bounds = new Rectangle(
            0,
            content.ClientSize.Height - actions.Height,
            content.ClientSize.Width,
            actions.Height
        );
    }

    private void RenderWelcome() {
        if (mode == SetupMode.Uninstall) {
            SetHeader("Uninstall Open Work Standard", "Remove OWS from this computer.");
            AddText("This will remove the following from your computer:", 0, 10, 420, 22, true);
            AddChecklist(["OWS Agent (Windows service)", "OWS CLI (ows command)", "Registry entries", "Installed application files"]);
            AddInfo("Your project folders (.ows) are not removed automatically and will be preserved.", 0, 158);
            AddActions("Back", "Cancel", "Next", () => { }, GoCancel, GoUninstallOptions);
            backButton.Visible = false;
            return;
        }

        SetHeader("Install Open Work Standard", "A local-first, privacy-preserving proof-of-work toolchain.");
        AddText("This will install:", 0, 10, 420, 22, true);
        AddChecklist(["OWS Agent (runs silently as a Windows service)", "OWS CLI (ows command)", "Required application files"]);
        AddInfo("The OWS Agent runs silently in the background as a Windows service.", 0, 132);
        AddActions("Back", "Cancel", "Continue", () => { }, GoCancel, GoInstallOptions);
        backButton.Visible = false;
    }

    private void RenderOptions() {
        if (mode == SetupMode.Uninstall) {
            SetHeader("Uninstall options", "Choose what should be removed.");
            var removeFiles = new CheckBox {
                Text = "Remove installed application files",
                Checked = true,
                Enabled = false,
                AutoSize = true,
                Location = ContentPoint(0, 14),
                ForeColor = Ink
            };
            body.Controls.Add(removeFiles);

            purgeDataBox.Text = "Preserve shared Agent data";
            purgeDataBox.Checked = !purgeDataRequested;
            purgeDataBox.AutoSize = true;
            purgeDataBox.Location = ContentPoint(0, 52);
            purgeDataBox.ForeColor = Ink;
            body.Controls.Add(purgeDataBox);
            AddText("Uncheck to remove logs, configuration, and service data.", 24, 73, 400, 20, false, Muted);

            var preserve = new CheckBox {
                Text = "Preserve user project data",
                Checked = true,
                Enabled = false,
                AutoSize = true,
                Location = ContentPoint(0, 112),
                ForeColor = Ink
            };
            body.Controls.Add(preserve);
            AddInfo("Project .ows folders will not be removed.", 0, 155);
            AddActions("Back", "Cancel", "Uninstall", GoUninstallWelcome, GoCancel, StartOperation);
            return;
        }

        SetHeader("Installation options", "Choose where OWS is installed and how it starts.");
        AddText("Installation location", 0, 12, 420, 20, true);
        installPathBox.Text = string.IsNullOrWhiteSpace(installPathBox.Text)
            ? Program.GetInstallDirectory()
            : installPathBox.Text;
        installPathBox.Location = ContentPoint(0, 38);
        installPathBox.Width = 290;
        installPathBox.Height = 25;
        body.Controls.Add(installPathBox);

        var browse = new Button { Text = "Browse...", Location = ContentPoint(300, 36), Size = new Size(82, 28) };
        browse.Click += BrowseForInstallDirectory;
        body.Controls.Add(browse);

        addCliBox.Location = ContentPoint(0, 88);
        startAgentBox.Location = ContentPoint(0, 121);
        body.Controls.Add(addCliBox);
        body.Controls.Add(startAgentBox);
        AddInfo("The Agent runs silently as a Windows service and watches explicitly initialized projects.", 0, 155);
        AddActions("Back", "Cancel", "Install", GoInstallWelcome, GoCancel, StartOperation);
    }

    private void RenderProgress() {
        SetHeader(
            mode == SetupMode.Install ? "Installing Open Work Standard" : "Uninstalling Open Work Standard",
            mode == SetupMode.Install
                ? "Please wait while OWS is installed on your computer."
                : "Please wait while OWS is removed from your computer."
        );

        progressBar.Location = ContentPoint(0, 12);
        progressBar.Width = 330;
        progressBar.Height = 18;
        progressBar.Style = ProgressBarStyle.Continuous;
        body.Controls.Add(progressBar);

        progressPercent.Text = "0%";
        progressPercent.AutoSize = true;
        progressPercent.Location = ContentPoint(342, 11);
        progressPercent.ForeColor = Muted;
        body.Controls.Add(progressPercent);
        progressRows.Location = ContentPoint(0, 40);
        progressRows.Size = new Size(420, 180);
        progressRows.FlowDirection = FlowDirection.TopDown;
        progressRows.WrapContents = false;
        progressRows.AutoScroll = false;
        body.Controls.Add(progressRows);

        var rows = mode == SetupMode.Install
            ? new[] { "Preparing files", "Installing OWS Agent", "Registering Windows service", "Installing CLI", "Verifying installation" }
            : new[] { "Stopping OWS Agent", "Removing Windows service", "Removing CLI", "Removing installed files", "Verifying cleanup" };
        foreach (var row in rows) {
            var rowPanel = new Panel { Width = 395, Height = 28, Margin = new Padding(0, 0, 0, 2) };
            var marker = new Label { Text = "○", AutoSize = true, Font = new Font("Segoe UI", 14F), ForeColor = Muted, Location = new Point(0, 2) };
            var label = new Label { Text = row, AutoSize = true, ForeColor = Ink, Location = new Point(28, 7) };
            var status = new Label { Text = "Pending", AutoSize = true, ForeColor = Muted, Location = new Point(300, 5) };
            rowPanel.Controls.Add(marker);
            rowPanel.Controls.Add(label);
            rowPanel.Controls.Add(status);
            progressRows.Controls.Add(rowPanel);
            progressStatuses.Add(status);
            progressMarkers.Add(marker);
        }

        if (progressStatuses.Count > 0) {
            progressStatuses[0].Text = "In progress";
            progressStatuses[0].ForeColor = Blue;
            progressMarkers[0].Text = "●";
            progressMarkers[0].ForeColor = Blue;
        }

        backButton.Visible = false;
        cancelButton.Visible = true;
        cancelButton.Enabled = false;
        primaryButton.Visible = false;
        cancelButton.Left = actions.ClientSize.Width - cancelButton.Width - actions.Padding.Right - 8;
        actions.Controls.Add(cancelButton);
        progressIndex = 0;
        operationStarted = false;
        QueueOperationStart();
    }

    private void RenderComplete() {
        SetHeader(
            mode == SetupMode.Install ? "Installation completed successfully" : "Uninstallation completed",
            mode == SetupMode.Install
                ? "Open Work Standard is now installed on your system."
                : "Open Work Standard has been removed from your computer."
        );
        AddCompletionRows();
        if (mode == SetupMode.Install) {
            AddInfo(
                startAgentBox.Checked
                    ? "The Agent is running silently as a Windows service. You can manage it in Windows Services."
                    : "The Agent service is installed but was not started. You can start it in Windows Services.",
                0, 155
            );
            var services = new Button { Text = "Open Windows Services", Location = ContentPoint(0, 210), Size = new Size(160, 30) };
            services.Click += (_, _) => Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
            body.Controls.Add(services);
        } else {
            AddInfo(purgeDataBox.Checked ? "Shared Agent data was preserved. Project .ows folders were preserved." : "Shared Agent data was removed. Project .ows folders were preserved.", 0, 155);
        }

        AddActions("Back", "", "Finish", () => { }, () => Close(), () => Close());
        backButton.Visible = false;
        cancelButton.Visible = false;
    }

    private void RenderFailure() {
        SetHeader(
            mode == SetupMode.Install ? "Installation failed" : "Uninstallation failed",
            mode == SetupMode.Install ? "OWS Setup could not complete the installation." : "OWS Setup could not complete cleanup."
        );
        var error = lastError?.Message ?? "An unknown error occurred.";
        AddError(error, 0, 12);
        AddText("What happened?", 0, 78, 420, 22, true);
        AddText(
            mode == SetupMode.Install
                ? "The installer stopped before all components were configured. Check administrator permissions and retry."
                : "Some components may still be present. Close running programs and retry cleanup.",
            0, 103, 420, 44, false, Muted
        );

        var details = new Button { Text = "Copy details", Location = ContentPoint(0, 158), Size = new Size(105, 30) };
        details.Click += (_, _) => Clipboard.SetText($"{Text}\n\n{lastError}");
        body.Controls.Add(details);
        AddActions("Back", "Cancel", "Retry", () => { }, () => Close(), RetryOperation);
        backButton.Visible = false;
    }

    private void RenderExisting() {
        header.Visible = false;
        body.Padding = new Padding(0);
        AddText("Open Work Standard is already installed", 0, 0, 560, 30, true, Ink, 15F);
        AddText("We detected an existing installation on this computer.", 0, 34, 560, 24, false, Muted);
        AddCard("Repair", "Fix missing or corrupted files, services, or configuration.", 0, 62, "Repair", () => StartExistingOperation(false));
        AddCard("Reinstall", "Reinstall all components over the existing installation.", 0, 120, "Reinstall", () => StartExistingOperation(true));
        AddCard("Uninstall", "Remove OWS and choose whether shared Agent data is preserved.", 0, 178, "Uninstall", StartUninstallFromExisting);
        AddActions("", "Cancel", "", () => { }, () => Close(), () => { });
        backButton.Visible = false;
        primaryButton.Visible = false;
    }

    private void AddCompletionRows() {
        var rows = mode == SetupMode.Install
            ? new[] { "OWS Agent service", "OWS CLI", "Registry entries", "Installed application files" }
            : new[] { "OWS Agent service", "OWS CLI", "Registry entries", "Installed application files" };
        var panel = new Panel { Location = ContentPoint(0, 8), Size = new Size(420, 140) };
        var y = 10;
        foreach (var row in rows) {
            var marker = new Label { Text = "✓", AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Green, Location = new Point(12, y) };
            var label = new Label { Text = row, AutoSize = true, ForeColor = Ink, Location = new Point(40, y + 2) };
            panel.Controls.Add(marker);
            panel.Controls.Add(label);
            y += 28;
        }

        body.Controls.Add(panel);
    }

    private void AddCard(string heading, string description, int x, int y, string actionText, Action action) {
        var card = new Panel {
            Location = ContentPoint(x, y),
            Size = new Size(420, 50),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        AddText(heading, 14, 6, 280, 20, true, Ink, 9F, card);
        AddText(description, 14, 25, 280, 18, false, Muted, 8.5F, card);
        var button = new Button {
            Text = actionText,
            Size = new Size(82, 28),
            Location = new Point(320, 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => action();
        card.Controls.Add(button);
        body.Controls.Add(card);
    }

    private void AddChecklist(IEnumerable<string> entries) {
        var y = 38;
        foreach (var entry in entries) {
            AddText("✓", 0, y, 20, 20, true, Muted, 10F);
            AddText(entry, 24, y, 430, 22, false, Ink);
            y += 28;
        }
    }

    private void AddInfo(string message, int x, int y) {
        var panel = new Panel {
            Location = ContentPoint(x, y),
            Size = new Size(420, 44),
            BackColor = Color.FromArgb(240, 247, 255),
            BorderStyle = BorderStyle.FixedSingle
        };
        var icon = new Label {
            Text = "ⓘ",
            Location = new Point(8, 5),
            Size = new Size(20, 34),
            ForeColor = Blue,
            Font = new Font("Segoe UI", 12F),
            TextAlign = ContentAlignment.MiddleCenter
        };
        panel.Controls.Add(icon);
        var messageLabel = new Label {
            Text = message,
            Location = new Point(34, 5),
            Size = new Size(360, 34),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(messageLabel);
        body.Controls.Add(panel);
    }

    private void AddError(string message, int x, int y) {
        var panel = new Panel {
            Location = ContentPoint(x, y),
            Size = new Size(420, 54),
            BackColor = Color.FromArgb(255, 242, 242),
            BorderStyle = BorderStyle.FixedSingle
        };
        AddText("!", 12, 12, 20, 24, true, Color.FromArgb(190, 35, 35), 12F, panel);
        AddText(message, 38, 9, 365, 34, false, Color.FromArgb(125, 30, 30), 8.5F, panel);
        body.Controls.Add(panel);
    }

    private void AddText(
        string value,
        int x,
        int y,
        int width,
        int height,
        bool bold = false,
        Color? color = null,
        float size = 9F,
        Control? parent = null
    ) {
        var label = new Label {
            Text = value,
            Location = new Point(parent is null ? ContentLeft + x : x, y),
            Size = new Size(width, height),
            AutoEllipsis = false,
            ForeColor = color ?? Ink,
            Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size),
            AutoSize = false
        };
        (parent ?? body).Controls.Add(label);
    }

    private static Point ContentPoint(int x, int y) => new(ContentLeft + x, y);

    private void SetHeader(string heading, string supportingText) {
        title.Text = heading;
        subtitle.Text = supportingText;
    }

    private void AddActions(string back, string cancel, string primary, Action backAction, Action cancelAction, Action primaryAction) {
        ConfigureActionButton(backButton, back, backAction, false);
        ConfigureActionButton(cancelButton, cancel, cancelAction, false);
        ConfigureActionButton(primaryButton, primary, primaryAction, true);
        if (!string.IsNullOrEmpty(back)) {
            backButton.Left = 0;
            actions.Controls.Add(backButton);
        }

        if (!string.IsNullOrEmpty(cancel)) {
            cancelButton.Left = string.IsNullOrEmpty(primary)
                ? actions.ClientSize.Width - cancelButton.Width - actions.Padding.Right - 8
                : actions.ClientSize.Width - 205;
            cancelButton.BackColor = Blue;
            cancelButton.ForeColor = Color.White;
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.FlatAppearance.BorderSize = 0;
            actions.Controls.Add(cancelButton);
        }

        if (!string.IsNullOrEmpty(primary)) {
            primaryButton.Left = actions.Width - 98;
            actions.Controls.Add(primaryButton);
        }
    }

    private void ConfigureActionButton(Button button, string text, Action action, bool prominent) {
        button.Text = text;
        button.Visible = !string.IsNullOrEmpty(text);
        button.Enabled = true;
        button.Click -= ActionButtonClick;
        button.Tag = action;
        button.Click += ActionButtonClick;
        button.Anchor = AnchorStyles.Bottom;
        button.Size = new Size(prominent ? 92 : 82, 32);
        button.Top = 2;
        if (prominent) {
            button.BackColor = Blue;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
        } else {
            button.BackColor = Color.White;
            button.ForeColor = Ink;
            button.FlatStyle = FlatStyle.Standard;
        }
    }

    private static void ConfigureButton(Button button, string text, bool prominent) {
        button.Text = text;
        button.Size = new Size(prominent ? 92 : 82, 32);
        button.Font = new Font("Segoe UI Semibold", 9F);
    }

    private void ActionButtonClick(object? sender, EventArgs e) {
        if ((sender as Button)?.Tag is Action action) {
            action();
        }
    }

    private void UpdateRail() {
        rail.Controls.Clear();
        var labels = mode == SetupMode.Install
            ? new[] { "Welcome", "Options", "Install", "Complete" }
            : new[] { "Welcome", "Options", "Uninstall", "Complete" };
        var active = page switch {
            SetupPage.Welcome or SetupPage.Existing => 0,
            SetupPage.Options => 1,
            SetupPage.Progress or SetupPage.Failure => 2,
            SetupPage.Complete => 3,
            _ => 0
        };

        for (var i = 0; i < labels.Length; i++) {
            var row = new Panel { Location = new Point(25, 52 + i * 38), Size = new Size(140, 28) };
            var marker = new StepMarker { state = i < active ? StepState.Done : i == active ? StepState.Active : StepState.Pending, Location = new Point(0, 6), Size = new Size(12, 12) };
            var label = new Label { Text = labels[i], AutoSize = true, Location = new Point(24, 3), ForeColor = i <= active ? Ink : Muted, Font = new Font("Segoe UI", 8.5F) };
            row.Controls.Add(marker);
            row.Controls.Add(label);
            rail.Controls.Add(row);
        }
    }

    private async void BrowseForInstallDirectory(object? sender, EventArgs e) {
        if (sender is not Button browse) {
            return;
        }

        browse.Enabled = false;
        try {
            var defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var currentPath = installPathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(currentPath)) {
                var fullCurrentPath = Path.GetFullPath(currentPath);
                defaultPath = Directory.Exists(fullCurrentPath)
                    ? fullCurrentPath
                    : Directory.GetParent(fullCurrentPath)?.FullName ?? defaultPath;
            }

            var selectedPath = await PickFolderAsync(defaultPath);
            if (selectedPath is not null && !IsDisposed) {
                selectedPath = Path.GetFullPath(selectedPath);
                installPathBox.Text = string.Equals(
                    Path.GetFileName(selectedPath),
                    "Open Work Standard",
                    StringComparison.OrdinalIgnoreCase
                ) ? selectedPath : Path.Combine(selectedPath, "Open Work Standard");
            }
        } catch (Exception exception) {
            MessageBox.Show(
                this,
                $"Could not open the installation location picker.\n\n{exception.Message}",
                "Open Work Standard Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        } finally {
            if (!IsDisposed) {
                browse.Enabled = true;
            }
        }
    }

    private static Task<string?> PickFolderAsync(string initialPath) {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try {
                using var dialog = new FolderBrowserDialog {
                    SelectedPath = initialPath,
                    RootFolder = Environment.SpecialFolder.MyComputer,
                    AutoUpgradeEnabled = false,
                    Description = "Choose the parent folder for Open Work Standard",
                    ShowNewFolderButton = true,
                    UseDescriptionForTitle = true
                };
                completion.SetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null);
            } catch (Exception exception) {
                completion.SetException(exception);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void GoInstallOptions() {
        page = SetupPage.Options;
        Render();
    }

    private void GoInstallWelcome() {
        page = SetupPage.Welcome;
        Render();
    }

    private void GoUninstallOptions() {
        page = SetupPage.Options;
        Render();
    }

    private void GoUninstallWelcome() {
        page = SetupPage.Welcome;
        Render();
    }

    private void GoCancel() => Close();

    private void StartExistingOperation(bool reinstall) {
        installPathBox.Text = Program.GetInstallDirectory();
        reinstallRequested = reinstall;
        page = SetupPage.Progress;
        Render();
    }

    private void StartUninstallFromExisting() {
        mode = SetupMode.Uninstall;
        page = SetupPage.Options;
        Render();
    }

    private void StartOperation() {
        if (operationStarted || IsDisposed) {
            return;
        }

        operationStarted = true;
        try {
            if (mode == SetupMode.Install) {
                var target = Path.GetFullPath(installPathBox.Text.Trim());
                if (!Path.IsPathRooted(target) || !string.Equals(Path.GetFileName(target), "Open Work Standard", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("Choose an installation folder named 'Open Work Standard'.");
                }

                _ = RunOperationAsync(() => Program.Install(target, addCliBox.Checked, startAgentBox.Checked, reinstallRequested, ReportStep));
            } else {
                _ = RunOperationAsync(() => Program.Uninstall(!purgeDataBox.Checked, ReportStep));
            }
        } catch (Exception exception) {
            ShowFailure(exception);
        }
    }

    private void QueueOperationStart() {
        if (IsHandleCreated) {
            BeginInvoke(StartOperation);
            return;
        }

        Shown += StartOperationOnShown;
    }

    private void StartOperationOnShown(object? sender, EventArgs e) {
        Shown -= StartOperationOnShown;
        StartOperation();
    }

    private async Task RunOperationAsync(Action operation) {
        try {
            await Task.Run(operation);
            CompleteProgress();
            page = SetupPage.Complete;
            Render();
        } catch (Exception exception) {
            ShowFailure(exception);
        }
    }

    private void ReportStep(string step) {
        if (InvokeRequired) {
            Invoke(new Action<string>(ReportStep), step);
            return;
        }

        if (progressStatuses.Count == 0) {
            return;
        }

        var index = Math.Min(progressIndex, progressStatuses.Count - 1);
        progressIndex = Math.Min(progressIndex + 1, progressStatuses.Count);
        progressStatuses[index].Text = "Completed";
        progressStatuses[index].ForeColor = Green;
        progressMarkers[index].Text = "✓";
        progressMarkers[index].ForeColor = Green;
        if (index + 1 < progressStatuses.Count) {
            progressStatuses[index + 1].Text = "In progress";
            progressStatuses[index + 1].ForeColor = Blue;
            progressMarkers[index + 1].Text = "●";
            progressMarkers[index + 1].ForeColor = Blue;
        }

        progressBar.Value = Math.Min(100, (progressIndex * 100) / progressStatuses.Count);
        progressPercent.Text = $"{progressBar.Value}%";
    }

    private void CompleteProgress() {
        if (InvokeRequired) {
            Invoke(CompleteProgress);
            return;
        }

        progressIndex = progressStatuses.Count;
        progressBar.Value = 100;
        progressPercent.Text = "100%";
        for (var index = 0; index < progressStatuses.Count; index++) {
            progressStatuses[index].Text = "Completed";
            progressStatuses[index].ForeColor = Green;
            progressMarkers[index].Text = "✓";
            progressMarkers[index].ForeColor = Green;
        }
    }

    private void ShowFailure(Exception exception) {
        if (InvokeRequired) {
            Invoke(new Action<Exception>(ShowFailure), exception);
            return;
        }

        lastError = exception;
        page = SetupPage.Failure;
        Render();
    }

    private void RetryOperation() {
        progressIndex = 0;
        operationStarted = false;
        lastError = null;
        page = SetupPage.Progress;
        Render();
    }

    private void SetActionsEnabled(bool enabled) {
        backButton.Enabled = enabled;
        cancelButton.Enabled = enabled;
        primaryButton.Enabled = enabled;
    }

    private enum StepState {
        Pending,
        Active,
        Done
    }

    private sealed class StepMarker : Panel {
        internal StepState state;

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var color = state == StepState.Done ? Green : state == StepState.Active ? Blue : Color.FromArgb(150, 163, 180);
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, 1, 1, 10, 10);
            if (state == StepState.Pending) {
                using var inner = new SolidBrush(Color.FromArgb(248, 250, 253));
                e.Graphics.FillEllipse(inner, 3, 3, 6, 6);
            }
        }
    }
}
