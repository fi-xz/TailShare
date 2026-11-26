using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TailShare;

public partial class App : Application
{
    private readonly Func<Task> _flow;

    /// <param name="flow">The send flow to run once XAML is up; the app exits when it finishes.</param>
    public App(Func<Task> flow)
    {
        _flow = flow;

        // The process has package identity, but the package root only holds the manifest and logos.
        // Pin XAML to the app's .pri next to the exe in the external location rather than relying
        // on MRT's packaged/unpackaged lookup rules.
        // TailShare.pri first: resources.pri next to the exe is the package's logo index (for the shell).
        var pri = new[] { "TailShare.pri", "resources.pri" }
            .Select(name => Path.Combine(AppContext.BaseDirectory, name))
            .FirstOrDefault(File.Exists);
        if (pri is not null)
            ResourceManagerRequested += (_, e) => e.CustomResourceManager = new ResourceManager(pri);

        InitializeComponent();

        // Closing the picker must not tear down the dispatcher: the toast and
        // ReportCompleted still run after it. Exit() is called explicitly.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await _flow();
        }
        catch (Exception ex)
        {
            Log.Write($"flow failed: {ex}");
            Toast.Show("Tailscale send failed", ex.Message);
        }
        finally
        {
            Log.Write("exit");
            Exit();
        }
    }
}
