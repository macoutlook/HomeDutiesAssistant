using System.Net;
using System.Text;

namespace HomeDutiesAssistant.Templates;

/// <summary>
/// The content of one transactional email, independent of how it is marked up.
/// Callers describe what they want to say; <see cref="EmailTemplate"/> decides how it looks.
/// </summary>
/// <param name="Title">Headline, also used as the document title.</param>
/// <param name="Intro">The opening paragraph.</param>
/// <param name="ButtonText">Call-to-action label. Omit (with <paramref name="ButtonUrl"/>) for an email with no action.</param>
/// <param name="ButtonUrl">Where the call to action goes.</param>
/// <param name="Outro">Optional closing note, set smaller and muted.</param>
/// <param name="Preheader">Optional inbox preview line shown beside the subject. Falls back to <paramref name="Intro"/>.</param>
public sealed record EmailContent(
    string Title,
    string Intro,
    string? ButtonText = null,
    string? ButtonUrl = null,
    string? Outro = null,
    string? Preheader = null);

/// <summary>
/// Renders <see cref="EmailContent"/> into the branded HTML layout and a plain-text
/// equivalent. Both come from the same content, so the two parts of a
/// multipart/alternative message never drift apart.
/// </summary>
public static class EmailTemplate
{
    private const string LayoutResource = "HomeDutiesAssistant.Templates.EmailLayout.html";

    private static readonly Lazy<string> Layout = new(LoadLayout);

    public static string RenderHtml(EmailContent content)
    {
        var hasButton = !string.IsNullOrWhiteSpace(content.ButtonText)
                        && !string.IsNullOrWhiteSpace(content.ButtonUrl);
        var hasOutro = !string.IsNullOrWhiteSpace(content.Outro);

        var html = Layout.Value;
        html = Section(html, "Button", hasButton);
        html = Section(html, "Outro", hasOutro);

        // Everything substituted here is HTML-encoded: the confirmation link carries
        // a query string whose '&' would otherwise break the href.
        html = html.Replace("{{Preheader}}", Encode(content.Preheader ?? content.Intro));
        html = html.Replace("{{Title}}", Encode(content.Title));
        html = html.Replace("{{Intro}}", Encode(content.Intro));
        if (hasButton)
        {
            html = html.Replace("{{ButtonText}}", Encode(content.ButtonText));
            html = html.Replace("{{ButtonUrl}}", Encode(content.ButtonUrl));
        }

        if (hasOutro)
        {
            html = html.Replace("{{Outro}}", Encode(content.Outro));
        }

        return html;
    }

    /// <summary>The same message for clients that cannot render HTML.</summary>
    public static string RenderText(EmailContent content)
    {
        var text = new StringBuilder();
        text.AppendLine(content.Title);
        text.AppendLine(new string('=', content.Title.Length));
        text.AppendLine();
        text.AppendLine(content.Intro);

        if (!string.IsNullOrWhiteSpace(content.ButtonUrl))
        {
            text.AppendLine();
            text.AppendLine(string.IsNullOrWhiteSpace(content.ButtonText)
                ? content.ButtonUrl
                : $"{content.ButtonText}: {content.ButtonUrl}");
        }

        if (!string.IsNullOrWhiteSpace(content.Outro))
        {
            text.AppendLine();
            text.AppendLine(content.Outro);
        }

        text.AppendLine();
        text.AppendLine("--");
        text.AppendLine("Home Duties Assistant - your household, answered.");
        text.AppendLine("This is an automated message - replies aren't monitored.");
        return text.ToString();
    }

    /// <summary>
    /// Keeps or drops one optional block of the layout, marked in the HTML with
    /// <c>&lt;!--{{#Name}}--&gt;</c> … <c>&lt;!--{{/Name}}--&gt;</c>.
    /// </summary>
    private static string Section(string html, string name, bool keep)
    {
        var open = "<!--{{#" + name + "}}-->";
        var close = "<!--{{/" + name + "}}-->";

        var start = html.IndexOf(open, StringComparison.Ordinal);
        var end = html.IndexOf(close, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"The email layout has no '{name}' section.");
        }

        // Remove the later span first so the earlier index stays valid.
        return keep
            ? html.Remove(end, close.Length).Remove(start, open.Length)
            : html.Remove(start, end + close.Length - start);
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string LoadLayout()
    {
        using var stream = typeof(EmailTemplate).Assembly.GetManifestResourceStream(LayoutResource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{LayoutResource}' was not found. Is Templates/EmailLayout.html still marked <EmbeddedResource>?");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
