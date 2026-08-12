using System.Diagnostics;

namespace CS3.ScanBridge;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IServiceProvider services;
    private readonly ISettingsStore store;
    private readonly string logDirectory;
    private readonly int activePort;
    private readonly NotifyIcon notifyIcon;
    private SettingsForm? form;
    private bool exiting;

    public TrayApplicationContext(IServiceProvider services, ISettingsStore store, string logDirectory, int activePort)
    {
        this.services = services;
        this.store = store;
        this.logDirectory = logDirectory;
        this.activePort = activePort;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open CS3 Scan Bridge", null, (_, _) => ShowSettings(false));
        menu.Items.Add("Test scanner", null, async (_, _) => await TestScannerAsync());
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogs());
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartupRegistration.IsEnabled() };
        startup.CheckedChanged += async (_, _) => await SetStartupAsync(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitBridge());

        notifyIcon = new NotifyIcon
        {
            Text = "CS3 Scan Bridge",
            Icon = AppIcon.Value,
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowSettings(false);
    }

    public void ShowSettings(bool configurationWarning)
    {
        if (form is null || form.IsDisposed)
            form = new SettingsForm(services, store, logDirectory, activePort, configurationWarning);
        if (!form.Visible) form.Show();
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
    }

    private async Task TestScannerAsync()
    {
        if (MessageBox.Show("Load a document in the scanner. Start a physical test scan?", "CS3 Scan Bridge",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            var result = await services.GetRequiredService<ScanCoordinator>()
                .ScanAsync(new ScanRequest("tray-test", "scanner-test.pdf"), CancellationToken.None);
            MessageBox.Show($"The scanner acquired {result.PageCount} page(s). The test data was discarded.",
                "CS3 Scan Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"The scanner test failed: {exception.Message}", "CS3 Scan Bridge",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SetStartupAsync(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            var settings = store.Current;
            settings.StartWithWindows = enabled;
            await store.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"The startup setting could not be changed: {exception.Message}", "CS3 Scan Bridge",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
    }

    private void ExitBridge()
    {
        exiting = true;
        notifyIcon.Visible = false;
        form?.AllowClose();
        form?.Close();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (!exiting) return;
        notifyIcon.Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) notifyIcon.Dispose();
        base.Dispose(disposing);
    }
}
