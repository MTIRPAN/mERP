using Merp.Application.Abstractions.Documents;

namespace Merp.Modules.Recruitment.Application.Offers;

/// <summary>The offer letter Recruitment prints (D36, D53); tenants may write their own template of it.</summary>
public sealed class OfferLetterType : IDocumentTypeProvider
{
    public const string Key = "recruitment.offer_letter";

    private const string Css = """
        h1 { font-size: 18px; text-align: center; margin: 16px 0; }
        p { font-size: 12px; line-height: 1.6; }
        .meta { font-size: 12px; text-align: right; }
        table { width: 100%; border-collapse: collapse; font-size: 12px; margin: 12px 0; }
        th, td { border: 1px solid #bfbfbf; padding: 6px 8px; text-align: left; vertical-align: top; }
        th { background: #f5f5f5; width: 32%; }
        .signatures { display: flex; justify-content: space-between; margin-top: 56px; font-size: 12px; }
        .signatures div { width: 40%; text-align: center; border-top: 1px solid #595959; padding-top: 6px; }
        """;

    private const string Body = """
        {% render 'letterhead' %}
        <p class="meta">{{ texts.number }}: {{ offer.number }}<br>{{ texts.date }}: {{ offer.date }}</p>
        <h1>{{ texts.title }}</h1>
        <p>{{ texts.greeting }} {{ candidate.name }},</p>
        <p>{{ texts.intro }}</p>
        <table>
          <tr><th>{{ texts.position }}</th><td>{{ offer.position }}</td></tr>
          {% if offer.grade != "" %}<tr><th>{{ texts.grade }}</th><td>{{ offer.grade }}</td></tr>{% endif %}
          <tr><th>{{ texts.start_date }}</th><td>{{ offer.start_date }}</td></tr>
          <tr><th>{{ texts.salary }}</th><td>{{ offer.salary }} ({{ offer.period }})</td></tr>
          {% if offer.benefits != "" %}<tr><th>{{ texts.benefits }}</th><td>{{ offer.benefits }}</td></tr>{% endif %}
          <tr><th>{{ texts.expires_on }}</th><td>{{ offer.expires_on }}</td></tr>
        </table>
        <p>{{ texts.closing }}</p>
        <div class="signatures">
          <div>{{ texts.employer_signature }}<br>{{ company.title }}</div>
          <div>{{ texts.candidate_signature }}<br>{{ candidate.name }}</div>
        </div>
        """;

    public IEnumerable<DocumentTypeDefinition> GetDocumentTypes() =>
    [
        new(
            Key,
            "recruitment.documents.offer_letter.name",
            Body,
            Css,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["candidate"] = Row(("name", "Ayşe Yılmaz"), ("email", "ayse@ornek.test")),
                ["offer"] = Row(
                    ("number", "TKL-2026-00012"),
                    ("date", "29.10.2026"),
                    ("position", "Satış Uzmanı"),
                    ("grade", "M2"),
                    ("start_date", "01.12.2026"),
                    ("salary", "90.000,00 TRY"),
                    ("period", "aylık brüt"),
                    ("benefits", "Özel sağlık sigortası, yemek kartı"),
                    ("expires_on", "06.11.2026")),
            },
            [
                new("candidate.name", "recruitment.documents.offer_letter.fields.candidate_name"),
                new("candidate.email", "recruitment.documents.offer_letter.fields.candidate_email"),
                new("offer", "recruitment.documents.offer_letter.fields.offer"),
            ]),
    ];

    internal static Dictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
}
