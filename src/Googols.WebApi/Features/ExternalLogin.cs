using System.Security.Claims;
using Googols.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Googols.WebApi.Features;

/// <summary>
/// "Continue with Google". The browser is sent to Google, comes back to /signin-google (handled by the
/// Google middleware), then to the callback below, which signs the user in with the normal Identity cookie.
/// A Google account is linked to an existing Googols account with the same address only when Google says
/// the address is verified; otherwise a new account is created.
/// </summary>
public static class ExternalLoginEndpoints
{
    public const string GoogleScheme = "Google";

    public static void MapExternalLoginEndpoints(this RouteGroupBuilder v1, bool googleEnabled)
    {
        var g = v1.MapGroup("/auth").WithTags("Auth");

        g.MapGet("/providers", () => Results.Ok(googleEnabled ? new[] { GoogleScheme } : []));

        if (!googleEnabled)
        {
            return;
        }

        g.MapGet("/external/google", (string? returnUrl, SignInManager<AppUser> signIn) =>
        {
            var callback = $"/api/v1/auth/external/callback?returnUrl={Uri.EscapeDataString(SafeReturn(returnUrl))}";
            var props = signIn.ConfigureExternalAuthenticationProperties(GoogleScheme, callback);
            return Results.Challenge(props, [GoogleScheme]);
        }).RequireRateLimiting("auth");

        g.MapGet("/external/callback", async (string? returnUrl, SignInManager<AppUser> signIn, UserManager<AppUser> users,
            ILogger<AppUser> log) =>
        {
            var back = SafeReturn(returnUrl);
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
            {
                return Results.Redirect("/login?error=google");
            }

            // Already linked: sign straight in (lockout still applies).
            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                return Results.Redirect(back);
            }
            if (result.IsLockedOut || result.IsNotAllowed)
            {
                return Results.Redirect("/login?error=locked");
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                return Results.Redirect("/login?error=google-email");
            }
            var verified = string.Equals(info.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);

            var user = await users.FindByEmailAsync(email);
            if (user is not null && !verified)
            {
                // Never attach an unverified Google address to someone else's existing account.
                return Results.Redirect("/login?error=google-unverified");
            }
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = verified,
                    FullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "",
                    CreatedAtUtc = DateTime.UtcNow,
                };
                var created = await users.CreateAsync(user);
                if (!created.Succeeded)
                {
                    log.LogWarning("Google sign-up failed: {Errors}", string.Join("; ", created.Errors.Select(e => e.Code)));
                    return Results.Redirect("/login?error=google");
                }
            }
            if (await users.IsLockedOutAsync(user))
            {
                return Results.Redirect("/login?error=locked");
            }
            var linked = await users.AddLoginAsync(user, info);
            if (!linked.Succeeded)
            {
                return Results.Redirect("/login?error=google");
            }
            await signIn.SignOutAsync(); // clear the temporary external cookie
            await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
            return Results.Redirect(back);
        });
    }

    /// <summary>Only same-site paths, so the login can't be used to bounce users to another website.</summary>
    public static string SafeReturn(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
