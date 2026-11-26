using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.Activation;

namespace TailShare;

public static class Program
{
    /// <summary>"TailShare.exe --paths-file &lt;list&gt;": one path per line; the file is deleted after reading.</summary>
    public const string PathsFileSwitch = "--paths-file";

    [STAThread]
    private static int Main(string[] args)
    {
        // Started by COM for the Explorer context-menu command (com:ExeServer in AppxManifest.xml).
        if (ExplorerCommandServer.IsServerInvocation(args))
            return ExplorerCommandServer.Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        args = ExpandPathsFile(args);

        IActivatedEventArgs? activated = null;
        try
        {
            // Needs package identity (the sparse package). Throws when the exe is run outside of it.
            activated = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
        }
        catch (Exception ex)
        {
            Log.Write($"GetActivatedEventArgs failed (no package identity?): {ex.Message}");
        }

        Log.Write($"start: activation={activated?.Kind.ToString() ?? "none"} args={args.Length}");
        switch (activated)
        {
            case ShareTargetActivatedEventArgs share:
                StartApp(() => ShareFlow.RunAsync(share.ShareOperation));
                return 0;

            case { Kind: ActivationKind.ToastNotification }:
                // Clicking a result toast re-activates the app; nothing to do.
                return 0;

            case not ShareTargetActivatedEventArgs when args.Length > 0:
                // TailShare.exe <file|folder>... : same picker/send flow without the share broker
                // (the Explorer command launches this; also handy for manual testing).
                StartApp(() => ShareFlow.RunAsync(args));
                return 0;

            default:
                MessageBoxW(IntPtr.Zero,
                    activated is null
                        ? "TailShare is not registered. Run scripts\\register.ps1, then use Share in File Explorer."
                        : "Select files in File Explorer, choose Share, then pick \"Tailscale\".",
                    "TailShare", 0x40 /* MB_ICONINFORMATION */);
                return 0;
        }
    }

    private static void StartApp(Func<Task> flow) =>
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(flow);
        });

    private static string[] ExpandPathsFile(string[] args)
    {
        if (args.Length != 2 || args[0] != PathsFileSwitch) return args;
        try
        {
            var paths = File.ReadAllLines(args[1]).Where(l => l.Length > 0).ToArray();
            Log.Write($"{PathsFileSwitch}: {paths.Length} path(s)");
            return paths;
        }
        catch (Exception ex)
        {
            Log.Write($"{PathsFileSwitch} {args[1]} unreadable: {ex.Message}");
            return [];
        }
        finally
        {
            try { File.Delete(args[1]); } catch { }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
