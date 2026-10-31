namespace Merp.Modules.Recruitment.Domain.Applications;

/// <summary>
/// A candidate e-mail a user wrote or personalized (PLAN A5, D58): it replaces the template's subject and body for this
/// e-mail only. <c>{candidate}</c>-style placeholders are filled when it is sent, so the candidate's name never has to
/// be in the text.
/// </summary>
/// <param name="Subject">One line.</param>
/// <param name="Body">Plain text; paragraphs separated by blank lines.</param>
public sealed record CandidateEmailText(string Subject, string Body)
{
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 5000;

    public static CandidateEmailText Create(string subject, string body) =>
        new(Text.Required(subject, SubjectMaxLength, nameof(subject)), Text.Required(body, BodyMaxLength, nameof(body)));
}
