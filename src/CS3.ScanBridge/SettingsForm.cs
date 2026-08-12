using System.Diagnostics;
using System.Reflection;

namespace CS3.ScanBridge;

public sealed class SettingsForm : Form
{
    private readonly IServiceProvider services;
    private readonly ISettingsStore store;
    private readonly string logDirectory;
    private readonly int activePort;
    private readonly ComboBox scanner = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
    private readonly ComboBox dpi = Combo(["150", "200", "300", "600"]);
    private readonly ComboBox colour = Combo(Enum.GetNames<ScanColourMode>());
    private readonly ComboBox paper = Combo(Enum.GetNames<ScanPaperSize>());
    private readonly CheckBox duplex = new() { Text = "Enabled", AutoSize = true };
    private readonly NumericUpDown quality = Number(1, 100);
    private readonly NumericUpDown timeout = Number(10, 600);
    private readonly NumericUpDown pages = Number(1, 100);
    private readonly NumericUpDown port = Number(1024, 65535);
    private readonly TextBox origins = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 58, Width = 310 };
    private readonly CheckBox startup = new() { Text = "Start automatically when I sign in", AutoSize = true };
    private readonly Label serviceStatus = ValueLabel();
    private readonly Label address = ValueLabel();
    private readonly Label version = ValueLabel();
    private readonly Label availability = ValueLabel();
    private readonly Label bridgeState = ValueLabel();
    private readonly Label lastScan = ValueLabel();
    private readonly Label lastResult = ValueLabel();
    private readonly Label lastError = ValueLabel();
    private readonly Label warning = new() { AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(520, 0) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private bool allowClose;

    public SettingsForm(IServiceProvider services, ISettingsStore store, string logDirectory, int activePort, bool configurationWarning)
    {
        this.services = services;
        this.store = store;
        this.logDirectory = logDirectory;
        this.activePort = activePort;
        Text = "CS3 Scan Bridge";
        Icon = AppIcon.Value;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(650, 720);
        Size = new Size(690, 790);
        FormClosing += OnFormClosing;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        AddSection(layout, "Status");
        AddRow(layout, "Service", serviceStatus);
        AddRow(layout, "Listening address", address);
        AddRow(layout, "Version", version);
        AddRow(layout, "Scanner availability", availability);
        AddRow(layout, "Bridge state", bridgeState);
        AddRow(layout, "Last scan", lastScan);
        AddRow(layout, "Last result", lastResult);
        AddRow(layout, "Last error ID", lastError);
        AddSection(layout, "Scan settings");
        AddRow(layout, "Scanner source", scanner);
        AddRow(layout, "DPI", dpi);
        AddRow(layout, "Colour mode", colour);
        AddRow(layout, "Paper size", paper);
        AddRow(layout, "Duplex", duplex);
        AddRow(layout, "JPEG quality", quality);
        AddRow(layout, "Timeout (seconds)", timeout);
        AddRow(layout, "Maximum pages", pages);
        AddRow(layout, "Listener port", port);
        AddRow(layout, "Allowed CS3 origins", origins);
        AddRow(layout, "Windows startup", startup);
        layout.Controls.Add(warning, 0, layout.RowCount);
        layout.SetColumnSpan(warning, 2);
        layout.RowCount++;

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        buttons.Controls.Add(MakeButton("Save", async (_, _) => await SaveAsync()));
        buttons.Controls.Add(MakeButton("Test scanner", async (_, _) => await TestScannerAsync()));
        buttons.Controls.Add(MakeButton("Open logs", (_, _) => OpenLogs()));
        buttons.Controls.Add(MakeButton("Close", (_, _) => Hide()));
        layout.Controls.Add(buttons, 0, layout.RowCount);
        layout.SetColumnSpan(buttons, 2);

        LoadSettings();
        address.Text = $"http://127.0.0.1:{activePort}";
        version.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        serviceStatus.Text = "Running";
        if (configurationWarning)
            warning.Text = "Configuration required: select a scanner and add at least one exact CS3 origin, then select Save.";
        Shown += async (_, _) => await RefreshScannersAsync();
        timer.Tick += async (_, _) => await RefreshStatusAsync();
        timer.Start();
    }

    private void LoadSettings()
    {
        var settings = store.Current;
        dpi.SelectedItem = settings.Dpi.ToString();
        colour.SelectedItem = settings.ColourMode.ToString();
        paper.SelectedItem = settings.PaperSize.ToString();
        duplex.Checked = settings.Duplex;
        quality.Value = settings.JpegQuality;
        timeout.Value = settings.ScanTimeoutSeconds;
        pages.Value = settings.MaximumPages;
        port.Value = settings.ListenerPort;
        origins.Lines = [.. settings.AllowedOrigins];
        startup.Checked = StartupRegistration.IsEnabled();
    }

    private async Task RefreshScannersAsync()
    {
        try
        {
            var scannerService = services.GetRequiredService<IScannerService>();
            var devices = await scannerService.GetScannersAsync(CancellationToken.None);
            var current = store.Current;
            var selectedId = (scanner.SelectedItem as ScannerChoice)?.Id ?? current.ScannerDeviceId;
            var selectedProvider = (scanner.SelectedItem as ScannerChoice)?.Provider ?? current.ScannerProvider;
            scanner.Items.Clear();
            foreach (var device in devices) scanner.Items.Add(new ScannerChoice(device.Id, device.Name, device.Provider));
            scanner.SelectedItem = scanner.Items.Cast<ScannerChoice>().FirstOrDefault(value =>
                value.Provider == selectedProvider && value.Id == selectedId);
            availability.Text = scanner.Items.Count == 0 ? "No WIA or TWAIN scanner source registered" : $"{scanner.Items.Count} scanner source(s) registered";
        }
        catch { availability.Text = "Scanner enumeration failed"; }
    }

    private async Task RefreshStatusAsync()
    {
        var status = services.GetRequiredService<BridgeStatus>();
        bridgeState.Text = status.Busy ? "Busy" : "Ready";
        lastScan.Text = status.LastScanTime?.LocalDateTime.ToString("g") ?? "Never";
        lastResult.Text = status.LastScanResult ?? "None";
        lastError.Text = status.LastErrorId ?? "None";
        if (!status.Busy && scanner.Items.Count == 0) await RefreshScannersAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            if (scanner.SelectedItem is not ScannerChoice selected) throw new ArgumentException("Select a WIA or TWAIN scanner.");
            var settings = new AppSettings
            {
                ScannerProvider = selected.Provider,
                ScannerDeviceId = selected.Id,
                ScannerName = selected.Name,
                Dpi = int.Parse((string)dpi.SelectedItem!),
                ColourMode = Enum.Parse<ScanColourMode>((string)colour.SelectedItem!),
                PaperSize = Enum.Parse<ScanPaperSize>((string)paper.SelectedItem!),
                Duplex = duplex.Checked,
                JpegQuality = (int)quality.Value,
                ScanTimeoutSeconds = (int)timeout.Value,
                MaximumPages = (int)pages.Value,
                ListenerPort = (int)port.Value,
                AllowedOrigins = origins.Lines.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
                StartWithWindows = startup.Checked
            };
            await store.SaveAsync(settings);
            StartupRegistration.SetEnabled(settings.StartWithWindows);
            warning.Text = settings.ListenerPort == activePort ? "Settings saved." : "Settings saved. Restart CS3 Scan Bridge to use the new listener port.";
        }
        catch (Exception exception) { warning.Text = $"Settings were not saved: {exception.Message}"; }
    }

    private async Task TestScannerAsync()
    {
        if (MessageBox.Show("Load a document in the scanner. Start a physical test scan?", "CS3 Scan Bridge",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var result = await services.GetRequiredService<ScanCoordinator>()
                .ScanAsync(new ScanRequest("settings-test", "scanner-test.pdf"), CancellationToken.None);
            MessageBox.Show($"The scanner acquired {result.PageCount} page(s). The test data was discarded.", "CS3 Scan Bridge");
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Scanner test failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
    }

    public void AllowClose() => allowClose = true;

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (allowClose) return;
        eventArgs.Cancel = true;
        Hide();
    }

    private static ComboBox Combo(IEnumerable<string> items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        combo.Items.AddRange(items.Cast<object>().ToArray());
        return combo;
    }

    private static NumericUpDown Number(int minimum, int maximum) => new() { Minimum = minimum, Maximum = maximum, Width = 100 };
    private static Label ValueLabel() => new() { AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
    private static Button MakeButton(string text, EventHandler handler) { var button = new Button { Text = text, AutoSize = true }; button.Click += handler; return button; }
    private static void AddSection(TableLayoutPanel layout, string text)
    {
        var label = new Label { Text = text, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 10, 0, 4) };
        layout.Controls.Add(label, 0, layout.RowCount);
        layout.SetColumnSpan(label, 2);
        layout.RowCount++;
    }
    private static void AddRow(TableLayoutPanel layout, string name, Control value)
    {
        layout.Controls.Add(new Label { Text = name, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, layout.RowCount);
        layout.Controls.Add(value, 1, layout.RowCount);
        layout.RowCount++;
    }

    private sealed record ScannerChoice(string Id, string Name, ScannerProvider Provider)
    {
        public override string ToString() => $"[{Provider.ToString().ToUpperInvariant()}] {Name}";
    }
}
