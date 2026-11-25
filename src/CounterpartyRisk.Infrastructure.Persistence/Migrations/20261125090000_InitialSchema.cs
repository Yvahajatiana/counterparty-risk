using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CounterpartyRisk.Infrastructure.Persistence.Migrations;

/// <summary>
/// #11: schemas, reference / portfolio / market / benchmark / calculation tables, monthly
/// partitions, row-level security per legal entity and least-privilege grants. The SQL lives in
/// <c>Migrations/Sql/20261125090000_InitialSchema/</c> and runs in file order, in one transaction.
/// </summary>
[DbContext(typeof(CounterpartyRiskDbContext))]
[Migration(MigrationId)]
public sealed class InitialSchema : Migration
{
    public const string MigrationId = "20261125090000_InitialSchema";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var file in SqlScripts.FilesOf(MigrationId))
        {
            migrationBuilder.Sql(SqlScripts.Load(MigrationId, file));
        }
    }

    /// <summary>Forward-only: history and results are never rolled back by a migration.</summary>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Migrations are forward-only: write a new migration instead.");
}
