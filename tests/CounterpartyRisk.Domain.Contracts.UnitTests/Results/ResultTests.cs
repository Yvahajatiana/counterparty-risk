using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Results;

public class ResultTests
{
    [Fact]
    public void Success_exposes_value_and_no_error()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_exposes_error_and_no_value()
    {
        var error = Error.NotFound("test.not_found", "Not found.");

        Result<int> result = error;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Validation_error_does_not_keep_attempted_values()
    {
        var error = new ValidationError([new FieldError("Amount", "NotEmpty", "Amount is required.")]);

        error.Type.ShouldBe(ErrorType.Validation);
        error.Errors.ShouldHaveSingleItem().Field.ShouldBe("Amount");
    }
}
