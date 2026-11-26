using System.Diagnostics;
using System.Text;

namespace TailShare;

/// <summary>A Taildrop target as printed by <c>tailscale file cp --targets</c>.</summary>
public sealed record Peer(string Address, string Name, string? Status)
{
    /// <summary>Lines look like "100.64.0.1\thost" or "100.64.0.2\thost\toffline; last seen 38h ago".</summary>
    public bool IsOnline => Status is null || !Status.StartsWith("offline", StringComparison.OrdinalIgnoreCase);

    public string Detail => Status is null ? Address : $"{Address} · {Status.Replace("; ", " · ")}";

    public double ItemOpacity => IsOnline ? 1.0 : 0.45;
}

public sealed record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    /// <summary>Best human-readable error text from the CLI output.</summary>
    public string Message
    {
        get
        {
            var text = (string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr).Trim();
            return text.Length == 0 ? $"tailscale exited with code {ExitCode}" : text;
        }
    }
}

public static class Tailscale
{
    public const string ExePath = @"C:\Program Files\Tailscale\tailscale.exe";

    public static bool IsInstalled => File.Exists(ExePath);

    public static async Task<IReadOnlyList<Peer>> GetTargetsAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var result = await RunAsync(["file", "cp", "--targets"], timeout.Token);
        if (!result.Ok)
            throw new InvalidOperationException(result.Message);

        return ParseTargets(result.StdOut);
    }

    public static IReadOnlyList<Peer> ParseTargets(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', StringSplitOptions.TrimEntries))
            .Where(f => f.Length >= 2 && f[0].Length > 0 && f[1].Length > 0)
            .Select(f => new Peer(f[0], f[1], f.Length > 2 && f[2].Length > 0 ? f[2] : null))
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>tailscale file cp &lt;files...&gt; &lt;peer&gt;:</summary>
    public static Task<CliResult> SendAsync(IEnumerable<string> files, Peer peer, CancellationToken ct) =>
        RunAsync(["file", "cp", .. files, $"{peer.Address}:"], ct);

    private static async Task<CliResult> RunAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start tailscale.exe");

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
