using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Start of the session, fixed at sign-in. <c>IssuedUtc</c> cannot be used for the absolute lifetime:
/// the cookie handler moves it forward at every sliding renewal.
/// </summary>
internal static class SessionStart
{
    private const string Key = "ccr.session_start";

    public static void Stamp(AuthenticationProperties properties, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.Items.ContainsKey(Key))
        {
            properties.Items[Key] = now.ToString("o", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Null when missing or unreadable: the caller must then end the session (fail closed).</summary>
    public static DateTimeOffset? Read(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Items.TryGetValue(Key, out var value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
            ? start
            : null;
    }
}
