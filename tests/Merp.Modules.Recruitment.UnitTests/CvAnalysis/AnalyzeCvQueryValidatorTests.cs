using Merp.Modules.Recruitment.Application.CvAnalysis;

namespace Merp.Modules.Recruitment.UnitTests.CvAnalysis;

public sealed class AnalyzeCvQueryValidatorTests
{
    private readonly AnalyzeCvQueryValidator _validator = new();

    [Theory]
    [InlineData("cv.pdf")]
    [InlineData("CV.PDF")]
    [InlineData("özgeçmiş.docx")]
    public void Accepts_PdfAndDocx(string fileName) =>
        _validator.Validate(Query(fileName, new byte[10])).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("cv.doc", "recruitment.cv.unsupported_file")]
    [InlineData("cv.png", "recruitment.cv.unsupported_file")]
    [InlineData("cv", "recruitment.cv.unsupported_file")]
    public void Rejects_OtherFileTypes(string fileName, string code)
    {
        var result = _validator.Validate(Query(fileName, new byte[10]));

        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
        result.Errors[0].PropertyName.ShouldBe("file");
    }

    [Fact]
    public void Rejects_EmptyAndTooLargeFiles()
    {
        _validator.Validate(Query("cv.pdf", [])).Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("common.errors.required");

        var tooLarge = _validator.Validate(Query("cv.pdf", new byte[AnalyzeCvQuery.MaxFileBytes + 1])).Errors.ShouldHaveSingleItem();
        tooLarge.ErrorCode.ShouldBe("recruitment.cv.file_too_large");
        tooLarge.FormattedMessagePlaceholderValues["MaxMegabytes"].ShouldBe(5);
    }

    [Fact]
    public void Limits_TextFields()
    {
        var query = Query("cv.pdf", new byte[10]) with { JobRequirements = new string('x', AnalyzeCvQuery.MaxJobRequirementsLength + 1) };

        _validator.Validate(query).Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(AnalyzeCvQuery.JobRequirements));
    }

    private static AnalyzeCvQuery Query(string fileName, byte[] content) => new(fileName, content, null, null, null);
}
