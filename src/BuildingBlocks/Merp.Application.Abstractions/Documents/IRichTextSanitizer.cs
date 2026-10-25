namespace Merp.Application.Abstractions.Documents;

/// <summary>
/// Cleans rich text (HTML from the TipTap editor, D49) before it is saved: everything but a small set of formatting
/// tags is removed, so the text is safe on public pages, in e-mails and in PDFs.
/// </summary>
public interface IRichTextSanitizer
{
    /// <summary>
    /// The HTML with only paragraphs, line breaks, bold, italic, underline, strike, headings 2–4, lists, quotes and
    /// links (http, https, mailto; opened in a new tab without referrer). Null when nothing but empty paragraphs is left.
    /// </summary>
    string? Sanitize(string? html);
}
