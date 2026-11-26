namespace TailShare;

/// <summary>Append-only diagnostics at %TEMP%\TailShare\TailShare.log (the app has no console).</summary>
public static class Log
{
    private static readonly string FilePath =
        Path.Combine(Path.GetTempPath(), "TailShare", "TailShare.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > 1_000_000)
                info.Delete();
            File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
