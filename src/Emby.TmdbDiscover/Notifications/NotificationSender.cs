using System;
using System.Net;
using System.Net.Mail;
using Emby.TmdbDiscover.Configuration;
using MediaBrowser.Common.Net;

namespace Emby.TmdbDiscover.Notifications
{
    public sealed class NotificationResult
    {
        public bool EmailSuccess { get; set; }
        public string EmailMessage { get; set; }
        public bool WebhookSuccess { get; set; }
        public string WebhookMessage { get; set; }

        // Distinguishes "you have not set this up" from "it was set up and failed".
        // Both are non-success, but only the second is an error worth alarming about,
        // and the webhook is optional so an unconfigured one is not a problem at all.
        public bool EmailConfigured { get; set; }
        public bool WebhookConfigured { get; set; }
    }

    public static class NotificationSender
    {
        public static NotificationResult Send(
            IHttpClient httpClient,
            PluginOptions opts,
            string subject,
            string body,
            string webhookTitle,
            string webhookMessage,
            string url,
            string tag)
        {
            var result = new NotificationResult();

            var missing = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(opts.SmtpHost)) missing.Add("Server");
            if (string.IsNullOrWhiteSpace(opts.NotificationToEmail)) missing.Add("To address");

            result.EmailConfigured = missing.Count == 0;
            result.WebhookConfigured = !string.IsNullOrWhiteSpace(opts.NotificationWebhookUrl);

            result.EmailMessage = "Not set up - fill in " + string.Join(" and ", missing.ToArray())
                + " under Email (SMTP), then Save Settings";
            result.WebhookMessage = "Not set up (optional) - no webhook URL";

            if (result.EmailConfigured)
            {
                try
                {
                    var fromAddr = !string.IsNullOrWhiteSpace(opts.NotificationFromEmail)
                        ? opts.NotificationFromEmail
                        : opts.NotificationToEmail;

                    using (var smtp = new SmtpClient(opts.SmtpHost, opts.SmtpPort))
                    {
                        smtp.EnableSsl = opts.SmtpUseSsl;
                        smtp.Timeout = 15000;

                        if (!string.IsNullOrWhiteSpace(opts.SmtpUsername))
                            smtp.Credentials = new NetworkCredential(opts.SmtpUsername, opts.SmtpPassword ?? "");

                        using (var msg = new MailMessage(fromAddr, opts.NotificationToEmail, subject, body))
                        {
                            smtp.Send(msg);
                        }
                    }

                    result.EmailSuccess = true;
                    result.EmailMessage = "Email sent to " + opts.NotificationToEmail;
                }
                catch (Exception ex)
                {
                    result.EmailMessage = Explain(ex, opts);
                }
            }

            if (!string.IsNullOrWhiteSpace(opts.NotificationWebhookUrl))
            {
                try
                {
                    var json = "{\"title\":\"" + EscapeJson(webhookTitle) + "\","
                        + "\"message\":\"" + EscapeJson(webhookMessage) + "\","
                        + "\"url\":\"" + EscapeJson(url ?? "") + "\","
                        + "\"tags\":[\"" + EscapeJson(tag ?? "") + "\"]"
                        + "}";

                    var request = new HttpRequestOptions
                    {
                        Url = opts.NotificationWebhookUrl,
                        RequestContentType = "application/json",
                        RequestContent = json.AsMemory(),
                        LogErrorResponseBody = false
                    };

                    httpClient.Post(request).GetAwaiter().GetResult();

                    result.WebhookSuccess = true;
                    result.WebhookMessage = "Webhook sent to " + opts.NotificationWebhookUrl;
                }
                catch (Exception ex)
                {
                    result.WebhookMessage = "Webhook failed: " + ex.Message;
                }
            }

            return result;
        }

        /// <summary>
        /// Turns an SMTP failure into something a person can act on. The raw
        /// exception text from System.Net.Mail is usually too vague to be useful
        /// ("Failure sending mail"), and the real cause sits in InnerException.
        /// </summary>
        private static string Explain(Exception ex, PluginOptions opts)
        {
            var deepest = ex;
            while (deepest.InnerException != null) deepest = deepest.InnerException;
            var detail = deepest.Message;
            var target = opts.SmtpHost + ":" + opts.SmtpPort;

            // System.Net.Mail's SmtpClient only speaks STARTTLS. It cannot do
            // implicit TLS, which is what port 465 expects, so that combination
            // always fails -- usually with a timeout or an abrupt disconnect.
            if (opts.SmtpPort == 465 && opts.SmtpUseSsl)
            {
                return "Port 465 is not supported (it needs implicit TLS, which Emby's mail "
                     + "library cannot do). Use port 587 with SSL ticked, or port 25 with SSL "
                     + "unticked. [" + detail + "]";
            }

            if (deepest is System.Net.Sockets.SocketException
                || detail.IndexOf("actively refused", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("No connection could be made", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Nothing is listening on " + target + ", or it is not reachable from the "
                     + "Emby server. Note that 'localhost' means the machine Emby runs on (your "
                     + "NAS), not your PC. [" + detail + "]";
            }

            if (detail.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Timed out connecting to " + target + ". A firewall may be dropping the "
                     + "connection, or the port may be wrong. [" + detail + "]";
            }

            if (detail.IndexOf("does not support secure", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "The server at " + target + " does not offer STARTTLS. Untick SSL and try "
                     + "again. [" + detail + "]";
            }

            if (detail.IndexOf("5.7.", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("not authenticated", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "The server rejected the login. Check Username and Password (for Gmail "
                     + "this must be an app password, not your account password). [" + detail + "]";
            }

            if (detail.IndexOf("5.5.1", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("relay", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "The server refused to relay this message. It may not accept mail for "
                     + opts.NotificationToEmail + " from this sender. [" + detail + "]";
            }

            if (detail.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("5.1.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "The server rejected an address. Check the From and To addresses. ["
                     + detail + "]";
            }

            return "Sending failed: " + detail;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
