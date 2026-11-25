using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Server-side session store: the <c>__Host-ccr</c> cookie only holds a random 256-bit key (SEC-089); the
/// authentication ticket, with the tokens, stays in an <see cref="IDistributedCache"/>, encrypted by Data
/// Protection (AES-256-GCM). Removing the entry revokes the session (logout, back-channel logout, SEC-093).
/// </summary>
/// <remarks>
/// The cache is in memory until #11 provides the PostgreSQL table <c>session.ticket</c> (role
/// <c>cr_bff_session</c>). Index updates are not atomic: a concurrent login can briefly exceed the
/// session limit, never bypass revocation of an existing key.
/// </remarks>
internal sealed partial class ServerSideTicketStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    IOptions<BffAuthenticationOptions> options,
    ILogger<ServerSideTicketStore> logger) : ITicketStore
{
    private const string TicketPrefix = "ccr:ticket:";
    private const string SubjectIndexPrefix = "ccr:session:sub:";
    private const string SessionIndexPrefix = "ccr:session:sid:";
    private const int KeyBytes = 32;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("CounterpartyRisk.Bff.SessionTicket.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket) => await StoreAsync(ticket, CancellationToken.None).ConfigureAwait(false);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(KeyBytes));
        await WriteAsync(key, ticket, cancellationToken).ConfigureAwait(false);

        var subject = ticket.Principal.FindFirst("sub")?.Value;
        if (subject is not null)
        {
            var evicted = await AddToIndexAsync(SubjectIndexPrefix + Hash(subject), key, options.Value.MaxConcurrentSessions, cancellationToken)
                .ConfigureAwait(false);
            foreach (var oldKey in evicted)
            {
                await cache.RemoveAsync(TicketPrefix + oldKey, cancellationToken).ConfigureAwait(false);
                LogSessionEvicted();
            }
        }

        var sessionId = ticket.Principal.FindFirst("sid")?.Value;
        if (sessionId is not null)
        {
            await AddToIndexAsync(SessionIndexPrefix + Hash(sessionId), key, int.MaxValue, cancellationToken).ConfigureAwait(false);
        }

        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return IsWellFormedKey(key) ? WriteAsync(key, ticket, cancellationToken) : Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsWellFormedKey(key))
        {
            return null;
        }

        var protectedBytes = await cache.GetAsync(TicketPrefix + key, cancellationToken).ConfigureAwait(false);
        if (protectedBytes is null)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(protectedBytes));
        }
        catch (CryptographicException)
        {
            // Key ring rotated or entry tampered with: the session is simply gone (fail closed).
            await cache.RemoveAsync(TicketPrefix + key, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        IsWellFormedKey(key) ? cache.RemoveAsync(TicketPrefix + key, cancellationToken) : Task.CompletedTask;

    /// <summary>Back-channel logout by Keycloak session id (<c>sid</c>).</summary>
    public Task<int> RevokeSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        RevokeIndexAsync(SessionIndexPrefix + Hash(sessionId), cancellationToken);

    /// <summary>Back-channel logout by subject: every session of the user.</summary>
    public Task<int> RevokeSubjectAsync(string subject, CancellationToken cancellationToken) =>
        RevokeIndexAsync(SubjectIndexPrefix + Hash(subject), cancellationToken);

    private async Task WriteAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var absoluteEnd = (SessionStart.Read(ticket.Properties) ?? now) + options.Value.AbsoluteLifetime;
        var expires = ticket.Properties.ExpiresUtc ?? now + options.Value.IdleTimeout;
        var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpiration = expires < absoluteEnd ? expires : absoluteEnd };

        var payload = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        await cache.SetAsync(TicketPrefix + key, payload, entryOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds <paramref name="key"/> to an index and returns the keys evicted beyond <paramref name="limit"/>.</summary>
    private async Task<IReadOnlyList<string>> AddToIndexAsync(string indexKey, string key, int limit, CancellationToken cancellationToken)
    {
        var entries = await ReadIndexAsync(indexKey, cancellationToken).ConfigureAwait(false);

        // Drop keys whose ticket already expired or was removed.
        var live = new List<string>();
        foreach (var entry in entries)
        {
            if (await cache.GetAsync(TicketPrefix + entry, cancellationToken).ConfigureAwait(false) is not null)
            {
                live.Add(entry);
            }
        }

        live.Add(key);
        var evicted = live.Count > limit ? live.Take(live.Count - limit).ToList() : [];
        var kept = live.Skip(evicted.Count).ToList();

        var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.AbsoluteLifetime };
        await cache.SetAsync(indexKey, JsonSerializer.SerializeToUtf8Bytes(kept), entryOptions, cancellationToken).ConfigureAwait(false);
        return evicted;
    }

    private async Task<int> RevokeIndexAsync(string indexKey, CancellationToken cancellationToken)
    {
        var entries = await ReadIndexAsync(indexKey, cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            await cache.RemoveAsync(TicketPrefix + entry, cancellationToken).ConfigureAwait(false);
        }

        await cache.RemoveAsync(indexKey, cancellationToken).ConfigureAwait(false);
        return entries.Count;
    }

    private async Task<List<string>> ReadIndexAsync(string indexKey, CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync(indexKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(bytes)?.Where(IsWellFormedKey).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Index keys never contain the raw identifier.</summary>
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>A key is exactly 43 base64url characters (256 bits); anything else is not looked up.</summary>
    private static bool IsWellFormedKey(string? key) =>
        key is { Length: 43 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Oldest session revoked: concurrent session limit reached")]
    private partial void LogSessionEvicted();
}

/// <summary>Wires the store into the cookie handler (the cookie then carries the session key only).</summary>
internal sealed class ConfigureCookieSessionStore(ServerSideTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name == CookieAuthenticationDefaults.AuthenticationScheme)
        {
            options.SessionStore = store;
        }
    }
}
