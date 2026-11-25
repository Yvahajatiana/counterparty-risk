using CounterpartyRisk.Domain.Contracts.ReferenceData;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.ReferenceData;

public class LeiTests
{
    [Theory]
    [InlineData("529900T8BM49AURSDO55")]
    [InlineData("5493001KJTIIGC8Y1R12")]
    [InlineData("HWUPKR0MPOU8FGXBT394")]
    public void Create_accepts_a_lei_with_valid_check_digits(string value)
    {
        var result = Lei.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("529900T8BM49AURSDO56", "lei.invalid_checksum")]
    [InlineData("529900T8BM49AURSDO5", "lei.invalid_length")]
    [InlineData("529900T8BM49AURSDO555", "lei.invalid_length")]
    [InlineData("529900t8bm49aursdo55", "lei.invalid_format")]
    [InlineData("529900T8BM49AURSDOA5", "lei.invalid_format")]
    [InlineData("529900T8BM49AURS-O55", "lei.invalid_format")]
    [InlineData(null, "lei.invalid_length")]
    public void Create_returns_a_failure_without_throwing_for_an_invalid_lei(string? value, string expectedCode)
    {
        var result = Lei.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
    }
}
