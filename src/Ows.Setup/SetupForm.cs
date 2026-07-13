using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

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
    private SetupMode _mode;
    private readonly bool _purgeDataRequested;
    private SetupPage _page;
    private readonly Panel _rail = new();
    private readonly Panel _header = new();
    private readonly Panel _body = new();
    private readonly Panel _actions = new();
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Button _backButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _primaryButton = new();
    private readonly TextBox _installPathBox = new();
    private readonly CheckBox _addCliBox = new();
    private readonly CheckBox _startAgentBox = new();
    private readonly CheckBox _purgeDataBox = new();
    private readonly ProgressBar _progressBar = new();
    private readonly FlowLayoutPanel _progressRows = new();
    private readonly List<Label> _progressStatuses = [];
    private readonly List<Label> _progressMarkers = [];
    private readonly Label _progressPercent = new();
    private int _progressIndex;
    private bool _operationStarted;
    private bool _reinstallRequested = true;
    private Exception? _lastError;

    public SetupForm(bool uninstall, bool purgeData = false) {
        _mode = uninstall ? SetupMode.Uninstall : SetupMode.Install;
        _purgeDataRequested = purgeData;
        _page = _mode == SetupMode.Install && Program.IsInstalled() ? SetupPage.Existing : SetupPage.Welcome;

        Text = "OWS Setup";
        using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ows-logo.ico")) {
            if (iconStream is not null) {
                Icon = new Icon(iconStream);
            }
        }
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
        _rail.Dock = DockStyle.Left;
        _rail.Width = 160;
        _rail.BackColor = Color.FromArgb(248, 250, 253);

        var content = new Panel {
            Location = new Point(_rail.Width, 0),
            Size = new Size(ClientSize.Width - _rail.Width, ClientSize.Height),
            BackColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(content);
        Controls.Add(_rail);
        _rail.BringToFront();

        _header.Dock = DockStyle.None;
        _header.Height = 70;
        _header.Padding = new Padding(24, 18, 24, 0);
        content.Controls.Add(_header);

        _body.Dock = DockStyle.None;
        _body.Padding = new Padding(38, 8, 36, 8);
        content.Controls.Add(_body);

        _actions.Dock = DockStyle.None;
        _actions.Height = 56;
        _actions.Padding = new Padding(24, 8, 24, 10);
        content.Controls.Add(_actions);
        LayoutContent();

        _title.AutoSize = true;
        _title.Font = new Font("Segoe UI Semibold", 15F);
        _title.ForeColor = Ink;
        _title.Location = new Point(ContentLeft, 18);
        _header.Controls.Add(_title);

        _subtitle.AutoSize = true;
        _subtitle.ForeColor = Muted;
        _subtitle.Location = new Point(ContentLeft, 44);
        _header.Controls.Add(_subtitle);

        ConfigureButton(_backButton, "Back", false);
        ConfigureButton(_cancelButton, "Cancel", false);
        ConfigureButton(_primaryButton, "Continue", true);

        _addCliBox.Text = "Install OWS CLI to PATH";
        _addCliBox.Checked = true;
        _addCliBox.AutoSize = true;
        _addCliBox.ForeColor = Ink;

        _startAgentBox.Text = "Start OWS Agent automatically";
        _startAgentBox.Checked = true;
        _startAgentBox.AutoSize = true;
        _startAgentBox.ForeColor = Ink;
    }

    private void Render() {
        UpdateRail();
        _header.Visible = _page != SetupPage.Existing;
        LayoutContent();
        _body.Controls.Clear();
        _actions.Controls.Clear();
        _progressStatuses.Clear();
        _progressMarkers.Clear();

        switch (_page) {
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
        if (_body.Parent is not Panel content) {
            return;
        }

        var headerHeight = _page != SetupPage.Existing ? _header.Height : 0;
        _header.Bounds = new Rectangle(0, 0, content.ClientSize.Width, _header.Height);
        _body.Bounds = new Rectangle(
            0,
            headerHeight,
            content.ClientSize.Width,
            Math.Max(0, content.ClientSize.Height - headerHeight - _actions.Height)
        );
        _actions.Bounds = new Rectangle(
            0,
            content.ClientSize.Height - _actions.Height,
            content.ClientSize.Width,
            _actions.Height
        );
    }

    private void RenderWelcome() {
        if (_mode == SetupMode.Uninstall) {
            SetHeader("Uninstall Open Work Standard", "Remove OWS from this computer.");
            AddText("This will remove the following from your computer:", 0, 10, 420, 22, true);
            AddChecklist(
                [
                    "OWS Agent (Windows service)", "OWS CLI (ows command)", "Registry entries",
                    "Installed application files"
                ]
            );
            AddInfo("Your project folders (.ows) are not removed automatically and will be preserved.", 0, 158);
            AddActions("Back", "Cancel", "Next", () => { }, GoCancel, GoUninstallOptions);
            _backButton.Visible = false;
            return;
        }

        SetHeader("Install Open Work Standard", "A local-first, privacy-preserving proof-of-work toolchain.");
        AddText("This will install:", 0, 10, 420, 22, true);
        AddChecklist(
            ["OWS Agent (runs silently as a Windows service)", "OWS CLI (ows command)", "Required application files"]
        );
        AddInfo("The OWS Agent runs silently in the background as a Windows service.", 0, 132);
        AddActions("Back", "Cancel", "Continue", () => { }, GoCancel, GoInstallOptions);
        _backButton.Visible = false;
    }

    private void RenderOptions() {
        if (_mode == SetupMode.Uninstall) {
            SetHeader("Uninstall options", "Choose what should be removed.");
            var removeFiles = new CheckBox {
                Text = "Remove installed application files",
                Checked = true,
                Enabled = false,
                AutoSize = true,
                Location = ContentPoint(0, 14),
                ForeColor = Ink
            };
            _body.Controls.Add(removeFiles);

            _purgeDataBox.Text = "Preserve shared Agent data";
            _purgeDataBox.Checked = !_purgeDataRequested;
            _purgeDataBox.AutoSize = true;
            _purgeDataBox.Location = ContentPoint(0, 52);
            _purgeDataBox.ForeColor = Ink;
            _body.Controls.Add(_purgeDataBox);
            AddText("Uncheck to remove logs, configuration, and service data.", 24, 73, 400, 20, false, Muted);

            var preserve = new CheckBox {
                Text = "Preserve user project data",
                Checked = true,
                Enabled = false,
                AutoSize = true,
                Location = ContentPoint(0, 112),
                ForeColor = Ink
            };
            _body.Controls.Add(preserve);
            AddInfo("Project .ows folders will not be removed.", 0, 155);
            AddActions("Back", "Cancel", "Uninstall", GoUninstallWelcome, GoCancel, StartOperation);
            return;
        }

        SetHeader("Installation options", "Choose where OWS is installed and how it starts.");
        AddText("Installation location", 0, 12, 420, 20, true);
        _installPathBox.Text = string.IsNullOrWhiteSpace(_installPathBox.Text)
            ? Program.GetInstallDirectory()
            : _installPathBox.Text;
        _installPathBox.Location = ContentPoint(0, 38);
        _installPathBox.Width = 290;
        _installPathBox.Height = 25;
        _body.Controls.Add(_installPathBox);

        var browse = new Button { Text = "Browse...", Location = ContentPoint(300, 36), Size = new Size(82, 28) };
        browse.Click += BrowseForInstallDirectory;
        _body.Controls.Add(browse);

        _addCliBox.Location = ContentPoint(0, 88);
        _startAgentBox.Location = ContentPoint(0, 121);
        _body.Controls.Add(_addCliBox);
        _body.Controls.Add(_startAgentBox);
        AddInfo("The Agent runs silently as a Windows service and watches explicitly initialized projects.", 0, 155);
        AddActions("Back", "Cancel", "Install", GoInstallWelcome, GoCancel, StartOperation);
    }

    private void RenderProgress() {
        SetHeader(
            _mode == SetupMode.Install ? "Installing Open Work Standard" : "Uninstalling Open Work Standard",
            _mode == SetupMode.Install
                ? "Please wait while OWS is installed on your computer."
                : "Please wait while OWS is removed from your computer."
        );

        _progressBar.Location = ContentPoint(0, 12);
        _progressBar.Width = 330;
        _progressBar.Height = 18;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _body.Controls.Add(_progressBar);

        _progressPercent.Text = "0%";
        _progressPercent.AutoSize = true;
        _progressPercent.Location = ContentPoint(342, 11);
        _progressPercent.ForeColor = Muted;
        _body.Controls.Add(_progressPercent);
        _progressRows.Location = ContentPoint(0, 40);
        _progressRows.Size = new Size(420, 180);
        _progressRows.FlowDirection = FlowDirection.TopDown;
        _progressRows.WrapContents = false;
        _progressRows.AutoScroll = false;
        _body.Controls.Add(_progressRows);

        var rows = _mode == SetupMode.Install
            ? new[] {
                "Preparing files", "Installing OWS Agent", "Registering Windows service", "Installing CLI",
                "Verifying installation"
            }
            : new[] {
                "Stopping OWS Agent", "Removing Windows service", "Removing CLI", "Removing installed files",
                "Verifying cleanup"
            };
        foreach (var row in rows) {
            var rowPanel = new Panel { Width = 395, Height = 28, Margin = new Padding(0, 0, 0, 2) };
            var marker = new Label {
                Text = "○", AutoSize = true, Font = new Font("Segoe UI", 14F), ForeColor = Muted,
                Location = new Point(0, 2)
            };
            var label = new Label { Text = row, AutoSize = true, ForeColor = Ink, Location = new Point(28, 7) };
            var status = new Label
                { Text = "Pending", AutoSize = true, ForeColor = Muted, Location = new Point(300, 5) };
            rowPanel.Controls.Add(marker);
            rowPanel.Controls.Add(label);
            rowPanel.Controls.Add(status);
            _progressRows.Controls.Add(rowPanel);
            _progressStatuses.Add(status);
            _progressMarkers.Add(marker);
        }

        if (_progressStatuses.Count > 0) {
            _progressStatuses[0].Text = "In progress";
            _progressStatuses[0].ForeColor = Blue;
            _progressMarkers[0].Text = "●";
            _progressMarkers[0].ForeColor = Blue;
        }

        _backButton.Visible = false;
        _cancelButton.Visible = true;
        _cancelButton.Enabled = false;
        _primaryButton.Visible = false;
        _cancelButton.Left = _actions.ClientSize.Width - _cancelButton.Width - _actions.Padding.Right - 8;
        _actions.Controls.Add(_cancelButton);
        _progressIndex = 0;
        _operationStarted = false;
        QueueOperationStart();
    }

    private void RenderComplete() {
        SetHeader(
            _mode == SetupMode.Install ? "Installation completed successfully" : "Uninstallation completed",
            _mode == SetupMode.Install
                ? "Open Work Standard is now installed on your system."
                : "Open Work Standard has been removed from your computer."
        );
        AddCompletionRows();
        if (_mode == SetupMode.Install) {
            AddInfo(
                _startAgentBox.Checked
                    ? "The Agent is running silently as a Windows service. You can manage it in Windows Services."
                    : "The Agent service is installed but was not started. You can start it in Windows Services.",
                0, 155
            );
            var services = new Button
                { Text = "Open Windows Services", Location = ContentPoint(0, 210), Size = new Size(160, 30) };
            services.Click += (_, _) => Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
            _body.Controls.Add(services);
        } else {
            AddInfo(
                _purgeDataBox.Checked
                    ? "Shared Agent data was preserved. Project .ows folders were preserved."
                    : "Shared Agent data was removed. Project .ows folders were preserved.", 0, 155
            );
        }

        AddActions("Back", "", "Finish", () => { }, Close, Close);
        _backButton.Visible = false;
        _cancelButton.Visible = false;
    }

    private void RenderFailure() {
        SetHeader(
            _mode == SetupMode.Install ? "Installation failed" : "Uninstallation failed",
            _mode == SetupMode.Install
                ? "OWS Setup could not complete the installation."
                : "OWS Setup could not complete cleanup."
        );
        var error = _lastError?.Message ?? "An unknown error occurred.";
        AddError(error, 0, 12);
        AddText("What happened?", 0, 78, 420, 22, true);
        AddText(
            _mode == SetupMode.Install
                ? "The installer stopped before all components were configured. Check administrator permissions and retry."
                : "Some components may still be present. Close running programs and retry cleanup.",
            0, 103, 420, 44, false, Muted
        );

        var details = new Button { Text = "Copy details", Location = ContentPoint(0, 158), Size = new Size(105, 30) };
        details.Click += (_, _) => Clipboard.SetText($"{Text}\n\n{_lastError}");
        _body.Controls.Add(details);
        AddActions("Back", "Cancel", "Retry", () => { }, Close, RetryOperation);
        _backButton.Visible = false;
    }

    private void RenderExisting() {
        _header.Visible = false;
        _body.Padding = new Padding(0);
        AddText("Open Work Standard is already installed", 0, 0, 560, 30, true, Ink, 15F);
        AddText("We detected an existing installation on this computer.", 0, 34, 560, 24, false, Muted);
        AddCard(
            "Repair", "Fix missing or corrupted files, services, or configuration.", 0, 62, "Repair",
            () => StartExistingOperation(false)
        );
        AddCard(
            "Reinstall", "Reinstall all components over the existing installation.", 0, 120, "Reinstall",
            () => StartExistingOperation(true)
        );
        AddCard(
            "Uninstall", "Remove OWS and choose whether shared Agent data is preserved.", 0, 178, "Uninstall",
            StartUninstallFromExisting
        );
        AddActions("", "Cancel", "", () => { }, Close, () => { });
        _backButton.Visible = false;
        _primaryButton.Visible = false;
    }

    private void AddCompletionRows() {
        var rows = new[] { "OWS Agent service", "OWS CLI", "Registry entries", "Installed application files" };
        var panel = new Panel { Location = ContentPoint(0, 8), Size = new Size(420, 140) };
        var y = 10;
        foreach (var row in rows) {
            var marker = new Label {
                Text = "✓", AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Green,
                Location = new Point(12, y)
            };
            var label = new Label { Text = row, AutoSize = true, ForeColor = Ink, Location = new Point(40, y + 2) };
            panel.Controls.Add(marker);
            panel.Controls.Add(label);
            y += 28;
        }

        _body.Controls.Add(panel);
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
        _body.Controls.Add(card);
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
        _body.Controls.Add(panel);
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
        _body.Controls.Add(panel);
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
        (parent ?? _body).Controls.Add(label);
    }

    private static Point ContentPoint(int x, int y) => new(ContentLeft + x, y);

    private void SetHeader(string heading, string supportingText) {
        _title.Text = heading;
        _subtitle.Text = supportingText;
    }

    private void AddActions(
        string back,
        string cancel,
        string primary,
        Action backAction,
        Action cancelAction,
        Action primaryAction
    ) {
        ConfigureActionButton(_backButton, back, backAction, false);
        ConfigureActionButton(_cancelButton, cancel, cancelAction, false);
        ConfigureActionButton(_primaryButton, primary, primaryAction, true);
        if (!string.IsNullOrEmpty(back)) {
            _backButton.Left = 0;
            _actions.Controls.Add(_backButton);
        }

        if (!string.IsNullOrEmpty(cancel)) {
            _cancelButton.Left = string.IsNullOrEmpty(primary)
                ? _actions.ClientSize.Width - _cancelButton.Width - _actions.Padding.Right - 8
                : _actions.ClientSize.Width - 205;
            _cancelButton.BackColor = Blue;
            _cancelButton.ForeColor = Color.White;
            _cancelButton.FlatStyle = FlatStyle.Flat;
            _cancelButton.FlatAppearance.BorderSize = 0;
            _actions.Controls.Add(_cancelButton);
        }

        if (!string.IsNullOrEmpty(primary)) {
            _primaryButton.Left = _actions.Width - 98;
            _actions.Controls.Add(_primaryButton);
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
        _rail.Controls.Clear();

        var labels = _mode == SetupMode.Install
            ? new[] { "Welcome", "Options", "Install", "Complete" }
            : new[] { "Welcome", "Options", "Uninstall", "Complete" };
        var active = _page switch {
            SetupPage.Welcome or SetupPage.Existing => 0,
            SetupPage.Options => 1,
            SetupPage.Progress or SetupPage.Failure => 2,
            SetupPage.Complete => 3,
            _ => 0
        };

        for (var i = 0; i < labels.Length; i++) {
            var row = new Panel { Location = new Point(25, 52 + i * 38), Size = new Size(140, 28) };
            var marker = new StepMarker {
                State = i < active ? StepState.Done : i == active ? StepState.Active : StepState.Pending,
                Location = new Point(0, 6), Size = new Size(12, 12)
            };
            var label = new Label {
                Text = labels[i], AutoSize = true, Location = new Point(24, 3), ForeColor = i <= active ? Ink : Muted,
                Font = new Font("Segoe UI", 8.5F)
            };
            row.Controls.Add(marker);
            row.Controls.Add(label);
            _rail.Controls.Add(row);
        }
    }

    private async void BrowseForInstallDirectory(object? sender, EventArgs e) {
        if (sender is not Button browse) {
            return;
        }

        browse.Enabled = false;
        try {
            var defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var currentPath = _installPathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(currentPath)) {
                var fullCurrentPath = Path.GetFullPath(currentPath);
                defaultPath = Directory.Exists(fullCurrentPath)
                    ? fullCurrentPath
                    : Directory.GetParent(fullCurrentPath)?.FullName ?? defaultPath;
            }

            var selectedPath = await PickFolderAsync(defaultPath);
            if (selectedPath is not null && !IsDisposed) {
                selectedPath = Path.GetFullPath(selectedPath);
                _installPathBox.Text = string.Equals(
                    Path.GetFileName(selectedPath),
                    "Open Work Standard",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? selectedPath
                    : Path.Combine(selectedPath, "Open Work Standard");
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
                    using var dialog = new FolderBrowserDialog();
                    dialog.SelectedPath = initialPath;
                    dialog.RootFolder = Environment.SpecialFolder.MyComputer;
                    dialog.AutoUpgradeEnabled = false;
                    dialog.Description = "Choose the parent folder for Open Work Standard";
                    dialog.ShowNewFolderButton = true;
                    dialog.UseDescriptionForTitle = true;
                    completion.SetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null);
                } catch (Exception exception) {
                    completion.SetException(exception);
                }
            }
        ) {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void GoInstallOptions() {
        _page = SetupPage.Options;
        Render();
    }

    private void GoInstallWelcome() {
        _page = SetupPage.Welcome;
        Render();
    }

    private void GoUninstallOptions() {
        _page = SetupPage.Options;
        Render();
    }

    private void GoUninstallWelcome() {
        _page = SetupPage.Welcome;
        Render();
    }

    private void GoCancel() => Close();

    private void StartExistingOperation(bool reinstall) {
        _installPathBox.Text = Program.GetInstallDirectory();
        _reinstallRequested = reinstall;
        _page = SetupPage.Progress;
        Render();
    }

    private void StartUninstallFromExisting() {
        _mode = SetupMode.Uninstall;
        _page = SetupPage.Options;
        Render();
    }

    private void StartOperation() {
        if (_operationStarted || IsDisposed) {
            return;
        }

        _operationStarted = true;
        try {
            if (_mode == SetupMode.Install) {
                var target = Path.GetFullPath(_installPathBox.Text.Trim());
                if (!Path.IsPathRooted(target) || !string.Equals(
                        Path.GetFileName(target), "Open Work Standard", StringComparison.OrdinalIgnoreCase
                    )) {
                    throw new InvalidOperationException("Choose an installation folder named 'Open Work Standard'.");
                }

                _ = RunOperationAsync(() => Program.Install(
                        target, _addCliBox.Checked, _startAgentBox.Checked, _reinstallRequested, ReportStep
                    )
                );
            } else {
                _ = RunOperationAsync(() => Program.Uninstall(!_purgeDataBox.Checked, ReportStep));
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
            _page = SetupPage.Complete;
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

        if (_progressStatuses.Count == 0) {
            return;
        }

        var index = Math.Min(_progressIndex, _progressStatuses.Count - 1);
        _progressIndex = Math.Min(_progressIndex + 1, _progressStatuses.Count);
        _progressStatuses[index].Text = "Completed";
        _progressStatuses[index].ForeColor = Green;
        _progressMarkers[index].Text = "✓";
        _progressMarkers[index].ForeColor = Green;
        if (index + 1 < _progressStatuses.Count) {
            _progressStatuses[index + 1].Text = "In progress";
            _progressStatuses[index + 1].ForeColor = Blue;
            _progressMarkers[index + 1].Text = "●";
            _progressMarkers[index + 1].ForeColor = Blue;
        }

        _progressBar.Value = Math.Min(100, (_progressIndex * 100) / _progressStatuses.Count);
        _progressPercent.Text = $"{_progressBar.Value}%";
    }

    private void CompleteProgress() {
        if (InvokeRequired) {
            Invoke(CompleteProgress);
            return;
        }

        _progressIndex = _progressStatuses.Count;
        _progressBar.Value = 100;
        _progressPercent.Text = "100%";
        for (var index = 0; index < _progressStatuses.Count; index++) {
            _progressStatuses[index].Text = "Completed";
            _progressStatuses[index].ForeColor = Green;
            _progressMarkers[index].Text = "✓";
            _progressMarkers[index].ForeColor = Green;
        }
    }

    private void ShowFailure(Exception exception) {
        if (InvokeRequired) {
            Invoke(new Action<Exception>(ShowFailure), exception);
            return;
        }

        _lastError = exception;
        _page = SetupPage.Failure;
        Render();
    }

    private void RetryOperation() {
        _progressIndex = 0;
        _operationStarted = false;
        _lastError = null;
        _page = SetupPage.Progress;
        Render();
    }

    private enum StepState {
        Pending,
        Active,
        Done
    }

    private sealed class StepMarker : Panel {
        internal StepState State;

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var color = State == StepState.Done ? Green :
                State == StepState.Active ? Blue : Color.FromArgb(150, 163, 180);
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, 1, 1, 10, 10);
            if (State == StepState.Pending) {
                using var inner = new SolidBrush(Color.FromArgb(248, 250, 253));
                e.Graphics.FillEllipse(inner, 3, 3, 6, 6);
            }
        }
    }
}
