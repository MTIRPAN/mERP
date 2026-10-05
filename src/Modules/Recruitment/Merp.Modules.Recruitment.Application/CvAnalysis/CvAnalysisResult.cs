using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Merp.Modules.Recruitment.Application.CvAnalysis;

// Output of the model. The JSON schema sent to the provider is generated from these types, so the descriptions are
// part of the prompt: change them together with CvAnalysisPrompt.Version. Enums are strings for the model and the API.

[Description("Structured, anonymized analysis of a CV for a recruiter.")]
public sealed record CvAnalysisResult(
    [property: Description("Two or three sentence professional summary. No names or personal details.")] string Summary,
    [property: Description("Most recent job title as written in the CV; empty if unknown.")] string CurrentTitle,
    [property: Description("Total professional experience in years with one decimal, overlapping periods counted once; null if it cannot be determined.")] decimal? TotalExperienceYears,
    [property: Description("Seniority suggested by the experience; Unknown if unclear.")] CvSeniority Seniority,
    [property: Description("Professional roles, most recent first.")] IReadOnlyList<CvExperience> Experiences,
    [property: Description("Education, most recent first.")] IReadOnlyList<CvEducation> Education,
    [property: Description("At most 25 skills, most relevant first.")] IReadOnlyList<CvSkill> Skills,
    IReadOnlyList<CvLanguage> Languages,
    IReadOnlyList<CvCertification> Certifications,
    [property: Description("Assessment against the job requirements; null when no requirements were given.")] CvMatch? Match,
    [property: Description("Five to eight job-related questions that verify claims and explore gaps.")] IReadOnlyList<CvInterviewQuestion> InterviewQuestions,
    [property: Description("Neutral points to clarify in the interview, such as unclear dates or missing information. Never speculate about personal reasons.")] IReadOnlyList<string> Clarifications);

[JsonConverter(typeof(JsonStringEnumConverter<CvSeniority>))]
public enum CvSeniority
{
    Unknown,
    Intern,
    Junior,
    MidLevel,
    Senior,
    Lead,
    Manager,
    Director,
    Executive,
}

public sealed record CvExperience(
    string Title,
    string Company,
    [property: Description("Start as YYYY-MM or YYYY; empty if unknown.")] string Start,
    [property: Description("End as YYYY-MM or YYYY; empty if current or unknown.")] string End,
    bool IsCurrent,
    [property: Description("Up to four achievements or responsibilities, briefly.")] IReadOnlyList<string> Highlights);

public sealed record CvEducation(
    string Institution,
    string Degree,
    string Field,
    CvEducationLevel Level,
    [property: Description("Graduation year; null if unknown or ongoing.")] int? GraduationYear,
    bool IsOngoing);

[JsonConverter(typeof(JsonStringEnumConverter<CvEducationLevel>))]
public enum CvEducationLevel
{
    HighSchool,
    Associate,
    Bachelor,
    Master,
    Doctorate,
    Certificate,
    Other,
}

public sealed record CvSkill(
    string Name,
    CvSkillCategory Category,
    CvSkillLevel Level,
    [property: Description("Short quote or paraphrase from the CV that shows the skill.")] string Evidence);

[JsonConverter(typeof(JsonStringEnumConverter<CvSkillCategory>))]
public enum CvSkillCategory
{
    Technical,
    Domain,
    Tool,
    Soft,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<CvSkillLevel>))]
public enum CvSkillLevel
{
    Unknown,
    Basic,
    Intermediate,
    Advanced,
    Expert,
}

public sealed record CvLanguage(string Language, CvLanguageLevel Level);

[JsonConverter(typeof(JsonStringEnumConverter<CvLanguageLevel>))]
public enum CvLanguageLevel
{
    Unknown,
    A1,
    A2,
    B1,
    B2,
    C1,
    C2,
    Native,
}

public sealed record CvCertification(string Name, string Issuer, [property: Description("Year obtained; null if unknown.")] int? Year);

public sealed record CvMatch(
    [property: Description("0-100: how well the CV's evidence covers the requirements, must-haves weighted more.")] int Score,
    CvRecommendation Recommendation,
    [property: Description("Two or three sentences explaining the assessment.")] string Summary,
    IReadOnlyList<CvRequirementAssessment> Requirements,
    [property: Description("Requirement-related strengths.")] IReadOnlyList<string> Strengths,
    [property: Description("Requirements not or only partly covered.")] IReadOnlyList<string> Gaps);

[JsonConverter(typeof(JsonStringEnumConverter<CvRecommendation>))]
public enum CvRecommendation
{
    StrongMatch,
    Match,
    PartialMatch,
    WeakMatch,
}

public sealed record CvRequirementAssessment(
    [property: Description("One requirement from the job, in its original wording.")] string Requirement,
    CvRequirementStatus Status,
    [property: Description("Evidence from the CV, or why it is missing or unclear.")] string Evidence);

[JsonConverter(typeof(JsonStringEnumConverter<CvRequirementStatus>))]
public enum CvRequirementStatus
{
    Met,
    Partial,
    NotMet,
    Unclear,
}

public sealed record CvInterviewQuestion(string Question, [property: Description("What the question checks.")] string Purpose);
