using System.Security;
using Windows.UI.Notifications;

namespace TailShare;

public static class Toast
{
    /// <summary>Shows a toast. Uses the package identity (display name "Tailscale" + logo).</summary>
    public static void Show(string title, string body)
    {
        Log.Write($"toast: {title} | {body}");
        try
        {
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml(
                "<toast><visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(title)}</text>" +
                $"<text>{SecurityElement.Escape(Truncate(body, 300))}</text>" +
                "</binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml));
        }
        catch (Exception ex)
        {
            // No identity / notifications disabled: nothing sensible to fall back to.
            Log.Write($"toast failed: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
