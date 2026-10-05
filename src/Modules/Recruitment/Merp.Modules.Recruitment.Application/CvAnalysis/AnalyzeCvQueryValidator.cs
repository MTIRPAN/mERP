using FluentValidation;
using FluentValidation.Results;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

internal sealed class AnalyzeCvQueryValidator : AbstractValidator<AnalyzeCvQuery>
{
    private const string FileField = "file";

    public AnalyzeCvQueryValidator()
    {
        RuleFor(q => q).Custom((query, context) =>
        {
            if (query.Content.IsEmpty)
            {
                context.AddFailure(Failure(CommonErrors.Required.Code));
            }
            else if (query.Content.Length > AnalyzeCvQuery.MaxFileBytes)
            {
                context.AddFailure(Failure(CvAnalysisErrors.FileTooLargeCode, ("MaxMegabytes", AnalyzeCvQuery.MaxFileBytes / 1024 / 1024)));
            }

            if (!AnalyzeCvQuery.AllowedExtensions.Contains(Path.GetExtension(query.FileName), StringComparer.OrdinalIgnoreCase))
            {
                context.AddFailure(Failure(CvAnalysisErrors.UnsupportedFileCode));
            }
        });

        RuleFor(q => q.JobTitle).MaximumLength(AnalyzeCvQuery.MaxJobTitleLength);
        RuleFor(q => q.JobRequirements).MaximumLength(AnalyzeCvQuery.MaxJobRequirementsLength);
        RuleFor(q => q.CandidateName).MaximumLength(AnalyzeCvQuery.MaxCandidateNameLength);
    }

    private static ValidationFailure Failure(string code, params (string Key, object Value)[] args) =>
        new(FileField, code)
        {
            ErrorCode = code,
            FormattedMessagePlaceholderValues = args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
        };
}
