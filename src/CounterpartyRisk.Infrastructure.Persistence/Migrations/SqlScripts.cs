using System.Reflection;

namespace CounterpartyRisk.Infrastructure.Persistence.Migrations;

/// <summary>Loads the SQL files embedded under <c>Migrations/Sql/&lt;migration id&gt;/</c>.</summary>
internal static class SqlScripts
{
    private const string Prefix = "Sql/";

    private static readonly Assembly Assembly = typeof(SqlScripts).Assembly;

    /// <summary>File names of one migration, in execution order (the files are numbered).</summary>
    public static IReadOnlyList<string> FilesOf(string migrationId) =>
        [.. Assembly.GetManifestResourceNames()
            .Select(Normalize)
            .Where(name => name.StartsWith($"{Prefix}{migrationId}/", StringComparison.Ordinal))
            .Select(name => name[($"{Prefix}{migrationId}/".Length)..])
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Files of a migration, failing when there is none: a packaging mistake must never record a
    /// migration as applied without running any DDL.
    /// </summary>
    public static IReadOnlyList<string> RequireFilesOf(string migrationId)
    {
        var files = FilesOf(migrationId);
        return files.Count > 0
            ? files
            : throw new InvalidOperationException($"No embedded SQL script found for migration {migrationId}.");
    }

    public static string Load(string migrationId, string fileName)
    {
        var expected = $"{Prefix}{migrationId}/{fileName}";
        // The logical name keeps the OS directory separator: compare in a normalised form.
        var resource = Assembly.GetManifestResourceNames().SingleOrDefault(name => Normalize(name) == expected)
            ?? throw new InvalidOperationException($"Embedded SQL script not found: {expected}");

        using var stream = Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Normalize(string name) => name.Replace('\\', '/');
}
