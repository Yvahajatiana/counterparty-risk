using Npgsql;

namespace CounterpartyRisk.IntegrationTests.Database;

internal static class DatabaseAssert
{
    public const string InsufficientPrivilege = "42501";

    public const string NotNullViolation = "23502";

    public const string CheckViolation = "23514";

    public const string InvalidTextRepresentation = "22P02";

    public static async Task<PostgresException> FailsWithAsync(string sqlState, Func<Task> action)
    {
        var exception = await Should.ThrowAsync<PostgresException>(action);
        exception.SqlState.ShouldBe(sqlState, exception.MessageText);
        return exception;
    }
}
