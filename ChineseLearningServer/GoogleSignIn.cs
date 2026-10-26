using Google.Apis.Auth;

namespace ChineseLearningServer;

// Who a "Sign in with Google" credential belongs to: Google's id for the
// account (stable even if the address changes), its verified Gmail address,
// and the name on the Google account (e.g. "Lại Hoài"), when Google gives one.
public sealed record GoogleIdentity(string Subject, string Email, string? Name = null);

public interface IGoogleTokenValidator
{
    // The account a credential from the Sign in with Google button proves, or
    // null when it is not a valid, unexpired Google token for this site.
    Task<GoogleIdentity?> ValidateAsync(string credential, string clientId);
}

// Checks the credential's signature against Google's published keys, that it
// was issued for this site's client ID, has not expired and carries a verified
// address.
public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string credential, string clientId)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                credential,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            return payload.EmailVerified && !string.IsNullOrEmpty(payload.Email)
                ? new GoogleIdentity(payload.Subject, payload.Email.ToLowerInvariant(), payload.Name)
                : null;
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
