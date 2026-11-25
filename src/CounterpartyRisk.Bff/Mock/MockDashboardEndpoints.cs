namespace CounterpartyRisk.Bff.Mock;

/// <summary>
/// Read endpoints of the dashboard, served from synthetic data while the Api is being built.
/// Each route is replaced by a reverse-proxy route to the Api once the matching use case exists.
/// </summary>
internal static class MockDashboardEndpoints
{
    public static IServiceCollection AddMockDashboard(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MockDashboardData>();
        return services;
    }

    public static IEndpointRouteBuilder MapMockDashboard(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard");

        group.MapGet("/summary", (MockDashboardData data) => TypedResults.Ok(data.Summary()));

        group.MapGet("/current-exposure", (MockDashboardData data) => TypedResults.Ok(data.CurrentExposure()));

        group.MapGet("/future-exposure", IResult (MockDashboardData data, string? counterpartyId) =>
        {
            var id = counterpartyId ?? MockDashboardData.Counterparties[0].Id;
            // Allow-list validation: only known identifiers reach the data layer.
            return MockDashboardData.IsKnownCounterparty(id)
                ? TypedResults.Ok(data.FutureExposure(id))
                : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown counterparty");
        });

        group.MapGet("/limits", (MockDashboardData data) => TypedResults.Ok(data.Limits()));

        group.MapGet("/history", IResult (MockDashboardData data, string? indicator, string? frequency) =>
        {
            var indicatorOk = TryParse(indicator, HistoryIndicator.Ead, out var parsedIndicator);
            var frequencyOk = TryParse(frequency, HistoryFrequency.Daily, out var parsedFrequency);
            return indicatorOk && frequencyOk
                ? TypedResults.Ok(data.History(parsedIndicator, parsedFrequency))
                : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid indicator or frequency");
        });

        return endpoints;
    }

    /// <summary>
    /// Strict enum parsing: exact member names only (case-insensitive). Enum.TryParse would also
    /// accept numbers, padded or signed values and comma-separated flag lists.
    /// </summary>
    private static bool TryParse<TEnum>(string? value, TEnum fallback, out TEnum result)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            result = fallback;
            return true;
        }
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            {
                result = Enum.Parse<TEnum>(name);
                return true;
            }
        }

        result = fallback;
        return false;
    }
}
