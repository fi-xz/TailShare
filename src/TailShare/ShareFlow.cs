using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;

namespace TailShare;

public static class ShareFlow
{
    /// <summary>Handles one share activation end to end. Always reports completion (or error) to the share broker.</summary>
    public static async Task RunAsync(ShareOperation op)
    {
        var files = new List<string>();
        var folders = new List<string>();
        string? tempDir = null;

        try
        {
            if (!op.Data.Contains(StandardDataFormats.StorageItems))
            {
                op.ReportCompleted();
                Toast.Show("Nothing to send", "Only files can be sent with Tailscale.");
                return;
            }

            foreach (var item in await op.Data.GetStorageItemsAsync())
            {
                switch (item)
                {
                    case StorageFolder folder:
                        folders.Add(folder.Name);
                        break;

                    case StorageFile file when !string.IsNullOrEmpty(file.Path) && File.Exists(file.Path):
                        files.Add(file.Path);
                        break;

                    case StorageFile file:
                        // Virtual / brokered item without a usable path: materialize a copy.
                        tempDir ??= Directory.CreateDirectory(
                            Path.Combine(Path.GetTempPath(), "TailShare", Guid.NewGuid().ToString("N"))).FullName;
                        var dest = await StorageFolder.GetFolderFromPathAsync(tempDir);
                        var copy = await file.CopyAsync(dest, file.Name, NameCollisionOption.GenerateUniqueName);
                        files.Add(copy.Path);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            op.ReportError(ex.Message);
            Toast.Show("Couldn't read the shared files", ex.Message);
            DeleteTemp(tempDir);
            return;
        }

        // Releases the Explorer share UI; everything below runs on our own time.
        op.ReportDataRetrieved();

        try
        {
            await PickAndSendAsync(files, folders);
        }
        finally
        {
            DeleteTemp(tempDir);
            op.ReportCompleted();
        }
    }

    /// <summary>Same flow for paths passed on the command line.</summary>
    public static Task RunAsync(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var folders = new List<string>();
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            if (Directory.Exists(path)) folders.Add(Path.GetFileName(path.TrimEnd('\\')));
            else if (File.Exists(path)) files.Add(path);
        }
        return PickAndSendAsync(files, folders);
    }

    private static async Task PickAndSendAsync(List<string> files, List<string> folders)
    {
        Log.Write($"files={files.Count} folders={folders.Count}");
        if (files.Count == 0)
        {
            Toast.Show("Folders can't be sent",
                $"Taildrop only sends files. Skipped: {string.Join(", ", folders)}");
            return;
        }

        if (!Tailscale.IsInstalled)
        {
            Toast.Show("Tailscale not found", $"Expected {Tailscale.ExePath}");
            return;
        }

        IReadOnlyList<Peer> peers;
        try
        {
            peers = await Tailscale.GetTargetsAsync();
        }
        catch (Exception ex)
        {
            Toast.Show("Can't list Tailscale devices", ex.Message);
            return;
        }

        if (peers.Count == 0)
        {
            Toast.Show("No devices to send to", "No Taildrop targets were found on your tailnet.");
            return;
        }

        var summary = Describe(files, folders);
        var window = new PeerPickerWindow(peers, summary, LastPeer);
        var peer = await window.PickAsync();
        if (peer is null)
        {
            window.CloseIfOpen();
            return;
        }

        LastPeer = peer.Name;
        window.ShowSending(peer, files.Count);
        Log.Write($"sending {files.Count} file(s) to {peer.Name} ({peer.Address})");

        CliResult? result = null;
        try
        {
            result = await Tailscale.SendAsync(files, peer, window.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        window.CloseIfOpen();

        if (result is null)
            Toast.Show("Send cancelled", $"Stopped sending to {peer.Name}. Files already transferred stay on the device.");
        else if (result.Ok)
            Toast.Show($"Sent to {peer.Name}", summary);
        else
            Toast.Show($"Couldn't send to {peer.Name}", result.Message);
    }

    /// <summary>"photo.jpg · 2.1 MB" / "3 files · 12.4 MB", plus a note about skipped folders.</summary>
    private static string Describe(List<string> files, List<string> folders)
    {
        long bytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        var text = files.Count == 1
            ? $"{Path.GetFileName(files[0])} · {FormatSize(bytes)}"
            : $"{files.Count} files · {FormatSize(bytes)}";

        if (folders.Count > 0)
            text += folders.Count == 1
                ? $" (skipped folder: {folders[0]})"
                : $" (skipped {folders.Count} folders)";
        return text;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    private static void DeleteTemp(string? dir)
    {
        if (dir is null) return;
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    /// <summary>Last chosen device name, preselected next time (stored in the package's LocalSettings).</summary>
    private static string? LastPeer
    {
        get
        {
            try { return ApplicationData.Current.LocalSettings.Values["LastPeer"] as string; }
            catch { return null; }
        }
        set
        {
            try { ApplicationData.Current.LocalSettings.Values["LastPeer"] = value; }
            catch { }
        }
    }
}
