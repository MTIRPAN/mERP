using Merp.Application.Abstractions.Documents;

namespace Merp.Modules.Platform.Application.Documents;

/// <summary>The document types of all modules (D36).</summary>
public sealed class DocumentTypeRegistry(IEnumerable<IDocumentTypeProvider> providers)
{
    private readonly Dictionary<string, DocumentTypeDefinition> _types =
        providers.SelectMany(p => p.GetDocumentTypes()).ToDictionary(t => t.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<DocumentTypeDefinition> All => _types.Values;

    public DocumentTypeDefinition? Find(string? key) => key is not null && _types.TryGetValue(key, out var type) ? type : null;

    /// <summary>
    /// Prefix of a type's fixed texts (labels such as "Subject"): <c>{module}.documents.{name}.texts.</c>. Templates
    /// read them as <c>texts.*</c> in the document's language, with the common ones of Platform.
    /// </summary>
    public static string TextsPrefix(string key)
    {
        var dot = key.IndexOf('.', StringComparison.Ordinal);
        return $"{key[..dot]}.documents.{key[(dot + 1)..]}.texts.";
    }
}

/// <summary>
/// Document types of Platform: a general letter on the company's letterhead (PLAN P12). It proves the pipeline and
/// lets tenants print simple letters; modules add their own types (leave form L6, offer letter R6).
/// </summary>
public sealed class PlatformDocumentTypes : IDocumentTypeProvider
{
    public const string Letter = "platform.letter";

    private const string LetterBody = """
        {% render 'letterhead' %}
        <table class="meta">
          <tr><th>{{ texts.number }}</th><td>{{ letter.number }}</td><td class="date">{{ document.date }}</td></tr>
          <tr><th>{{ texts.subject }}</th><td colspan="2">{{ letter.subject }}</td></tr>
        </table>
        <div class="recipient">
          <strong>{{ letter.recipient.name }}</strong>
          {% for line in letter.recipient.address_lines %}<br>{{ line }}{% endfor %}
        </div>
        {% for paragraph in letter.paragraphs %}<p>{{ paragraph }}</p>{% endfor %}
        <div class="signature">
          <strong>{{ letter.signer.name }}</strong><br>{{ letter.signer.title }}
        </div>
        """;

    private const string LetterCss = """
        .meta { border-collapse: collapse; margin-bottom: 24px; }
        .meta th { text-align: left; padding: 2px 12px 2px 0; font-weight: 600; }
        .meta td { padding: 2px 12px 2px 0; }
        .meta .date { text-align: right; width: 100%; }
        .recipient { margin-bottom: 24px; }
        p { text-align: justify; line-height: 1.5; }
        .signature { margin-top: 48px; margin-left: 60%; }
        """;

    public IEnumerable<DocumentTypeDefinition> GetDocumentTypes() =>
    [
        new(
            Letter,
            "platform.documents.letter.name",
            LetterBody,
            LetterCss,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                // Made up; the Turkish letters check fonts and encoding in previews
                ["letter"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["number"] = "2026/0042",
                    ["subject"] = "Çalışma belgesi talebi hk.",
                    ["recipient"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["name"] = "Ayşe Yılmaz",
                        ["address_lines"] = new List<object?> { "Örnek Mah. Gül Sok. No:5", "Nilüfer / Bursa" },
                    },
                    ["paragraphs"] = new List<object?>
                    {
                        "İlgi tarihli dilekçeniz incelenmiş olup şirketimizdeki çalışma süreniz ve görev unvanınız ekte bilgilerinize sunulmuştur.",
                        "Gereğini rica ederiz.",
                    },
                    ["signer"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["name"] = "Çağrı Öztürk",
                        ["title"] = "İnsan Kaynakları Müdürü",
                    },
                },
            },
            [
                new("letter.number", "platform.documents.letter.fields.number"),
                new("letter.subject", "platform.documents.letter.fields.subject"),
                new("letter.recipient.name", "platform.documents.letter.fields.recipient_name"),
                new("letter.recipient.address_lines", "platform.documents.letter.fields.recipient_address_lines"),
                new("letter.paragraphs", "platform.documents.letter.fields.paragraphs"),
                new("letter.signer.name", "platform.documents.letter.fields.signer_name"),
                new("letter.signer.title", "platform.documents.letter.fields.signer_title"),
            ]),
    ];
}
