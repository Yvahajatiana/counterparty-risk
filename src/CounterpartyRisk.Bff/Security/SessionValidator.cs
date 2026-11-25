using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Runs on every request carrying a session cookie (<c>OnValidatePrincipal</c>):
/// <list type="bullet">
/// <item>ends the session after the absolute lifetime (10 h, SEC-092), whatever the sliding expiry says;</item>
/// <item>refreshes the access token server side shortly before it expires (refresh token rotation);</item>
/// <item>ends the session when the identity provider rejects the refresh token (account disabled, SSO session
/// ended, SEC-094).</item>
/// </list>
/// Refreshes are serialized per session: with rotation and no reuse (<c>refreshTokenMaxReuse=0</c>), parallel SPA
/// requests would otherwise present the same refresh token and all but one would be rejected. Inside the lock,
/// a request first reuses tokens just refreshed by another request of the same session.
/// </summary>
internal sealed partial class SessionValidator(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    IOptions<BffAuthenticationOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionValidator> logger)
{
    private readonly KeyedLock _locks = new();
    private readonly ConcurrentDictionary<string, SessionTokens> _refreshed = new(StringComparer.Ordinal);

    private enum RefreshOutcome
    {
        Refreshed,

        /// <summary>The identity provider refused the refresh token: the session is over.</summary>
        Rejected,

        /// <summary>Timeout, network or server error: the session is kept, the expired token is not used.</summary>
        Unavailable,
    }

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;

        if (SessionStart.Read(context.Properties) is not { } started || now - started > settings.AbsoluteLifetime)
        {
            await EndSessionAsync(context).ConfigureAwait(false);
            return;
        }

        var current = SessionTokens.From(context.Properties);
        if (current.IsFresh(now, settings.RefreshMargin))
        {
            return;
        }

        var sessionId = SessionStart.Id(context.Properties);
        if (sessionId is null)
        {
            await EndSessionAsync(context).ConfigureAwait(false);
            return;
        }

        RefreshOutcome outcome;
        SessionTokens? tokens;
        using (await _locks.AcquireAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            now = timeProvider.GetUtcNow();
            PruneExpired(now);
            if (_refreshed.TryGetValue(sessionId, out var latest) && latest.IsFresh(now, settings.RefreshMargin))
            {
                // Another request of this session refreshed meanwhile: reuse its rotated tokens.
                (outcome, tokens) = (RefreshOutcome.Refreshed, latest);
            }
            else
            {
                (outcome, tokens) = await RefreshAsync(current.RefreshToken, now, context.HttpContext.RequestAborted).ConfigureAwait(false);
                if (outcome == RefreshOutcome.Refreshed)
                {
                    _refreshed[sessionId] = tokens!;
                }
            }
        }

        switch (outcome)
        {
            case RefreshOutcome.Refreshed:
                tokens!.ApplyTo(context.Properties);
                context.ShouldRenew = true;
                break;
            case RefreshOutcome.Unavailable when current.IsValid(now):
                // Transient failure while the access token is still valid: keep using it, retry on the next request.
                LogRefreshUnavailable();
                break;
            case RefreshOutcome.Unavailable:
                // Fail closed for this request (401), but the session survives a transient failure.
                LogRefreshUnavailable();
                context.RejectPrincipal();
                break;
            default:
                LogRefreshRejected();
                await EndSessionAsync(context).ConfigureAwait(false);
                break;
        }
    }

    private async Task<(RefreshOutcome Outcome, SessionTokens? Tokens)> RefreshAsync(
        string? refreshToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return (RefreshOutcome.Rejected, null);
        }

        var oidc = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        try
        {
            var configuration = await OidcConfiguration.GetAsync(oidc, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = oidc.ClientId!,
                    ["client_secret"] = oidc.ClientSecret!,
                }),
            };
            using var response = await oidc.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                return (RefreshOutcome.Rejected, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (RefreshOutcome.Unavailable, null);
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false);
            if (token is not { AccessToken.Length: > 0, ExpiresIn: > 0 })
            {
                return (RefreshOutcome.Rejected, null);
            }

            return (RefreshOutcome.Refreshed, new SessionTokens(
                token.AccessToken,
                string.IsNullOrEmpty(token.RefreshToken) ? refreshToken : token.RefreshToken,
                now.AddSeconds(token.ExpiresIn)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser gave up: nothing to decide.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout (TaskCanceledException): fail closed for this request.
            return (RefreshOutcome.Unavailable, null);
        }
        catch (HttpRequestException)
        {
            return (RefreshOutcome.Unavailable, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return (RefreshOutcome.Rejected, null);
        }
        catch (InvalidOperationException)
        {
            // Metadata unavailable.
            return (RefreshOutcome.Unavailable, null);
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var entry in _refreshed)
        {
            if (!entry.Value.IsValid(now))
            {
                _refreshed.TryRemove(entry);
            }
        }
    }

    private static async Task EndSessionAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        // Removes the server-side ticket and expires the cookie.
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Refresh token rejected by the identity provider: session ended")]
    private partial void LogRefreshRejected();

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "Identity provider unavailable during token refresh")]
    private partial void LogRefreshUnavailable();

    internal sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record SessionTokens(string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt)
    {
        public static SessionTokens From(AuthenticationProperties properties)
        {
            var raw = properties.GetTokenValue("expires_at");
            DateTimeOffset? expiresAt = raw is not null
                && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
            return new SessionTokens(properties.GetTokenValue("access_token"), properties.GetTokenValue("refresh_token"), expiresAt);
        }

        public bool IsFresh(DateTimeOffset now, TimeSpan margin) =>
            !string.IsNullOrEmpty(AccessToken) && ExpiresAt is { } expires && expires - margin > now;

        public bool IsValid(DateTimeOffset now) =>
            !string.IsNullOrEmpty(AccessToken) && ExpiresAt is { } expires && expires > now;

        public void ApplyTo(AuthenticationProperties properties)
        {
            properties.UpdateTokenValue("access_token", AccessToken!);
            properties.UpdateTokenValue("refresh_token", RefreshToken!);
            properties.UpdateTokenValue("expires_at", ExpiresAt!.Value.ToString("o", CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>Async mutual exclusion per key; an entry lives only while someone holds or waits for it.</summary>
internal sealed class KeyedLock
{
    private readonly Dictionary<string, (SemaphoreSlim Semaphore, int Count)> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore;
        lock (_entries)
        {
            (SemaphoreSlim Semaphore, int Count) entry = _entries.TryGetValue(key, out var existing) ? existing : (new SemaphoreSlim(1, 1), 0);
            _entries[key] = (entry.Semaphore, entry.Count + 1);
            semaphore = entry.Semaphore;
        }

        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entered: false);
            throw;
        }

        return new Releaser(this, key);
    }

    private void Release(string key, bool entered)
    {
        lock (_entries)
        {
            var entry = _entries[key];
            if (entered)
            {
                entry.Semaphore.Release();
            }

            if (entry.Count == 1)
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
            else
            {
                _entries[key] = (entry.Semaphore, entry.Count - 1);
            }
        }
    }

    private sealed class Releaser(KeyedLock owner, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(key, entered: true);
            }
        }
    }
}
