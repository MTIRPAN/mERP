using Merp.SharedKernel.Results;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public class ResultTests
{
    private static readonly Error SampleError = Error.Conflict("test.errors.sample");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.ErrorOrNull.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_ExposesError()
    {
        Result result = SampleError;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void GenericFailure_ThrowsOnValueAccess()
    {
        Result<int> result = SampleError;

        Should.Throw<InvalidOperationException>(() => result.Value).Message.ShouldContain("test.errors.sample");
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<string> result = "ok";

        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void Map_And_Bind_PropagateErrors()
    {
        Result<int> ok = 2;
        Result<int> failed = SampleError;

        ok.Map(x => x * 10).Value.ShouldBe(20);
        ok.Bind(x => Result.Success(x.ToString(System.Globalization.CultureInfo.InvariantCulture))).Value.ShouldBe("2");
        failed.Map(x => x * 10).Error.ShouldBe(SampleError);
        failed.Bind(x => Result.Success(x)).Error.ShouldBe(SampleError);
    }

    [Fact]
    public void ValidationWithFields_UsesCommonCode()
    {
        var error = Error.Validation([new FieldError("name", "common.errors.required")]);

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe(CommonErrors.ValidationCode);
        error.Fields.Single().Field.ShouldBe("name");
    }

    [Fact]
    public void ForField_WrapsErrorAsFieldError()
    {
        var error = CommonErrors.MaxLength(50).ForField("title");

        var field = error.Fields.Single();
        field.Field.ShouldBe("title");
        field.Code.ShouldBe("common.errors.max_length");
        field.Args["maxLength"].ShouldBe(50);
    }
}
