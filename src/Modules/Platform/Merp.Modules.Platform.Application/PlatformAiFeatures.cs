namespace Merp.Modules.Platform.Application;

/// <summary>AI features of the Platform module; tenants enable them one by one (D24).</summary>
public static class PlatformAiFeatures
{
    /// <summary>Drafts of the missing translations of a language (docs/domain/yapay-zeka.md §3.7, D60).</summary>
    public const string TranslationDrafts = "platform.translation_drafts";

    /// <summary>A summary of an approval request with its attention points for the approver (docs/domain/yapay-zeka.md §3.8, D61).</summary>
    public const string ApprovalSummary = "platform.approval_summary";

    /// <summary>Where the columns of an import are among an uploaded file's headers (docs/domain/yapay-zeka.md §3.9, D62).</summary>
    public const string ImportMapping = "platform.import_mapping";

    /// <summary>A document template drafted from a description (docs/domain/yapay-zeka.md §3.10, D63).</summary>
    public const string DocumentTemplateDraft = "platform.document_template_draft";

    /// <summary>The Ctrl+K assistant: read-only questions and navigation in natural language (docs/domain/yapay-zeka.md §3.12, D65).</summary>
    public const string Assistant = "platform.assistant";
}
