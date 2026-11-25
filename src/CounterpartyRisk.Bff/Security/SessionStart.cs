using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Start and identifier of the session, fixed at sign-in. <c>IssuedUtc</c> cannot be used for the absolute
/// lifetime: the cookie handler moves it forward at every sliding renewal. The identifier keys the per-session
/// refresh lock; it is random and never leaves the server-side ticket.
/// </summary>
internal static class SessionStart
{
    private const string StartKey = "ccr.session_start";
    private const string IdKey = "ccr.session_id";

    public static void Stamp(AuthenticationProperties properties, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items.TryAdd(StartKey, now.ToString("o", CultureInfo.InvariantCulture));
        properties.Items.TryAdd(IdKey, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)));
    }

    /// <summary>Null when missing or unreadable: the caller must then end the session (fail closed).</summary>
    public static DateTimeOffset? Read(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Items.TryGetValue(StartKey, out var value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
            ? start
            : null;
    }

    public static string? Id(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Items.TryGetValue(IdKey, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }
}
