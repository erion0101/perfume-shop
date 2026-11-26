using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

/// <summary>RFC 6238 TOTP, as an authenticator app computes it (SHA-1, 6 digits, 30 s).</summary>
internal static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32(base32Key.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant());
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in text.TrimEnd('='))
        {
            value = (value << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}

public sealed class StaffAccountTests(CustomersApiFactory factory)
{
    private const string TempPassword = "Temporary-Password-1!";
    private const string OwnPassword = "My-Own-Password-22!";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record StaffBody(Guid Id, string Email, string Role, bool IsActive, bool MustChangePassword, bool TwoFactorEnabled, DateTime? LastLoginAtUtc);

    private sealed record LoginBody(string AccessToken);

    private sealed record SetupBody(string SharedKey, string AuthenticatorUri);

    private sealed record CodesBody(IReadOnlyList<string> RecoveryCodes);

    private sealed record MeBody(bool TwoFactorEnabled, bool TwoFactorRequired, int RecoveryCodesLeft);

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password, string? code = null, string? recovery = null) =>
        client.PostAsJsonAsync("/api/auth/staff/login", new { email, password, twoFactorCode = code, recoveryCode = recovery }, ApiFactory.Json, Ct);

    private static async Task<string?> Code(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))?["code"]?.GetValue<string>();

    private async Task<StaffBody> CreateAsync(string role = Roles.Staff)
    {
        var email = $"new-{Guid.NewGuid():N}@perfumeshop.test";
        var response = await factory.CreateAdminClient().PostAsJsonAsync("/api/admin/staff", new { displayName = "Arta Berisha", email, role, temporaryPassword = TempPassword }, ApiFactory.Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffBody>(ApiFactory.Json, Ct))!;
    }

    private async Task<HttpClient> SignedInAsync(StaffBody staff, string? code = null)
    {
        var login = await Login(factory.CreateClient(), staff.Email, OwnPassword, code);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return factory.CreateClientWithToken((await login.Content.ReadFromJsonAsync<LoginBody>(ApiFactory.Json, Ct))!.AccessToken);
    }

    private async Task ChooseOwnPasswordAsync(StaffBody staff)
    {
        var initial = await factory.CreateClient().PostAsJsonAsync("/api/auth/staff/password/initial",
            new { email = staff.Email, temporaryPassword = TempPassword, newPassword = OwnPassword }, ApiFactory.Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, initial.StatusCode);
    }

    [Fact]
    public async Task New_staff_must_choose_their_own_password_before_signing_in()
    {
        var staff = await CreateAsync();
        Assert.True(staff.MustChangePassword);

        var first = await Login(factory.CreateClient(), staff.Email, TempPassword);
        Assert.Equal((HttpStatusCode.Forbidden, "Customers.Auth.PasswordChangeRequired"), (first.StatusCode, await Code(first)));

        var weak = await factory.CreateClient().PostAsJsonAsync("/api/auth/staff/password/initial", new { email = staff.Email, temporaryPassword = TempPassword, newPassword = "short" }, ApiFactory.Json, Ct);
        Assert.Equal("Customers.Staff.PasswordWeak", await Code(weak));

        await ChooseOwnPasswordAsync(staff);
        await SignedInAsync(staff);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.CreateClient(), staff.Email, TempPassword)).StatusCode); // the temporary one is gone

        var listed = (await factory.CreateAdminClient().GetFromJsonAsync<List<StaffBody>>("/api/admin/staff", ApiFactory.Json, Ct))!.Single(s => s.Id == staff.Id);
        Assert.Equal((false, true), (listed.MustChangePassword, listed.LastLoginAtUtc is not null));
    }

    [Fact]
    public async Task Only_admin_manages_staff_and_emails_are_unique()
    {
        var staff = await CreateAsync();
        var asStaff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));

        Assert.Equal(HttpStatusCode.Forbidden, (await asStaff.GetAsync("/api/admin/staff", Ct)).StatusCode);
        var duplicate = await factory.CreateAdminClient().PostAsJsonAsync("/api/admin/staff", new { displayName = "X", email = staff.Email.ToUpperInvariant(), role = "Staff", temporaryPassword = TempPassword }, ApiFactory.Json, Ct);
        Assert.Equal("Customers.Staff.EmailTaken", await Code(duplicate));
        var badRole = await factory.CreateAdminClient().PostAsJsonAsync("/api/admin/staff", new { displayName = "X", email = "x@perfumeshop.test", role = "Customer", temporaryPassword = TempPassword }, ApiFactory.Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
    }

    [Fact]
    public async Task Deactivated_staff_cannot_sign_in_and_lose_their_sessions()
    {
        var staff = await CreateAsync();
        await ChooseOwnPasswordAsync(staff);
        var admin = factory.CreateAdminClient();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/staff/{staff.Id}/deactivate", null, Ct)).StatusCode);
        var login = await Login(factory.CreateClient(), staff.Email, OwnPassword);
        Assert.Equal("Customers.Auth.Deactivated", await Code(login));
        Assert.Equal("Customers.Auth.InvalidCredentials", await Code(await Login(factory.CreateClient(), staff.Email, "Wrong-Password-99!"))); // wrong password: nothing revealed

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/staff/{staff.Id}/reactivate", null, Ct)).StatusCode);
        await SignedInAsync(staff);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var (email, password) = await factory.CreateStaffAccountAsync(Roles.Admin);
        var token = (await (await Login(factory.CreateClient(), email, password)).Content.ReadFromJsonAsync<LoginBody>(ApiFactory.Json, Ct))!.AccessToken;
        var me = factory.CreateClientWithToken(token);
        var self = (await me.GetFromJsonAsync<List<StaffBody>>("/api/admin/staff", ApiFactory.Json, Ct))!.Single(s => s.Email == email);

        Assert.Equal("Customers.Staff.CannotChangeSelf", await Code(await me.PostAsync($"/api/admin/staff/{self.Id}/deactivate", null, Ct)));
    }

    [Fact]
    public async Task Password_reset_gives_a_new_temporary_password()
    {
        var staff = await CreateAsync();
        await ChooseOwnPasswordAsync(staff);

        var reset = await factory.CreateAdminClient().PostAsJsonAsync($"/api/admin/staff/{staff.Id}/reset-password", new { temporaryPassword = "Another-Temp-Pass-3!" }, ApiFactory.Json, Ct);
        Assert.True((await reset.Content.ReadFromJsonAsync<StaffBody>(ApiFactory.Json, Ct))!.MustChangePassword);

        Assert.Equal("Customers.Auth.PasswordChangeRequired", await Code(await Login(factory.CreateClient(), staff.Email, "Another-Temp-Pass-3!")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.CreateClient(), staff.Email, OwnPassword)).StatusCode);
    }

    [Fact]
    public async Task Two_factor_sign_in_with_authenticator_and_recovery_codes()
    {
        var staff = await CreateAsync();
        await ChooseOwnPasswordAsync(staff);
        var client = await SignedInAsync(staff);

        var setup = (await (await client.PostAsync("/api/auth/staff/me/2fa/setup", null, Ct)).Content.ReadFromJsonAsync<SetupBody>(ApiFactory.Json, Ct))!;
        Assert.StartsWith("otpauth://totp/PerfumeShop%20Admin%3A", setup.AuthenticatorUri, StringComparison.Ordinal);
        Assert.Equal("Customers.Auth.InvalidTwoFactorCode", await Code(await client.PostAsJsonAsync("/api/auth/staff/me/2fa/enable", new { code = "000000" }, ApiFactory.Json, Ct)));

        var enabled = await client.PostAsJsonAsync("/api/auth/staff/me/2fa/enable", new { code = Totp.Code(setup.SharedKey) }, ApiFactory.Json, Ct);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var codes = (await enabled.Content.ReadFromJsonAsync<CodesBody>(ApiFactory.Json, Ct))!.RecoveryCodes;
        Assert.Equal(10, codes.Count);

        // Password alone is no longer enough.
        Assert.Equal("Customers.Auth.TwoFactorRequired", await Code(await Login(factory.CreateClient(), staff.Email, OwnPassword)));
        Assert.Equal("Customers.Auth.InvalidTwoFactorCode", await Code(await Login(factory.CreateClient(), staff.Email, OwnPassword, "123456")));
        var withCode = await SignedInAsync(staff, Totp.Code(setup.SharedKey));

        // A recovery code works exactly once.
        Assert.Equal(HttpStatusCode.OK, (await Login(factory.CreateClient(), staff.Email, OwnPassword, recovery: codes[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.CreateClient(), staff.Email, OwnPassword, recovery: codes[0])).StatusCode);
        var me = (await withCode.GetFromJsonAsync<MeBody>("/api/auth/staff/me", ApiFactory.Json, Ct))!;
        Assert.Equal((true, false, 9), (me.TwoFactorEnabled, me.TwoFactorRequired, me.RecoveryCodesLeft));

        // Staff may switch it off again (with a code).
        Assert.Equal(HttpStatusCode.NoContent, (await withCode.PostAsJsonAsync("/api/auth/staff/me/2fa/disable", new { code = Totp.Code(setup.SharedKey) }, ApiFactory.Json, Ct)).StatusCode);
        await SignedInAsync(staff);
    }

    [Fact]
    public async Task Changing_my_password_needs_the_current_one()
    {
        var staff = await CreateAsync();
        await ChooseOwnPasswordAsync(staff);
        var client = await SignedInAsync(staff);

        Assert.Equal("Customers.Staff.WrongPassword", await Code(await client.PostAsJsonAsync("/api/auth/staff/me/password", new { currentPassword = "Nope-Nope-123!", newPassword = "Brand-New-Pass-77!" }, ApiFactory.Json, Ct)));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/staff/me/password", new { currentPassword = OwnPassword, newPassword = "Brand-New-Pass-77!" }, ApiFactory.Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(factory.CreateClient(), staff.Email, "Brand-New-Pass-77!")).StatusCode);
    }
}

/// <summary>With Auth:Staff:RequireTwoFactorForAdmins (the production default) Admin pages need a second factor.</summary>
public sealed class AdminTwoFactorPolicyTests(CustomersApiFactory shared)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RequiredHost(string connectionString) : ApiFactory(connectionString)
    {
        protected override void ConfigureSettings(IDictionary<string, string?> settings) => settings["Auth:Staff:RequireTwoFactorForAdmins"] = "true";
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record SetupBody(string SharedKey);

    [Fact]
    public async Task Admin_without_two_factor_reaches_staff_pages_but_not_admin_pages_until_enabled()
    {
        await using var host = new RequiredHost(shared.ConnectionString);
        var (email, password) = await shared.CreateStaffAccountAsync(Roles.Admin);
        async Task<HttpClient> SignIn(string? code = null)
        {
            var login = await host.CreateClient().PostAsJsonAsync("/api/auth/staff/login", new { email, password, twoFactorCode = code }, ApiFactory.Json, Ct);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return host.CreateClientWithToken((await login.Content.ReadFromJsonAsync<LoginBody>(ApiFactory.Json, Ct))!.AccessToken);
        }

        var noMfa = await SignIn();
        Assert.Equal(HttpStatusCode.Forbidden, (await noMfa.GetAsync("/api/admin/staff", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await noMfa.GetAsync("/api/admin/customers", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await noMfa.GetAsync("/api/auth/staff/me", Ct)).StatusCode);

        var setup = (await (await noMfa.PostAsync("/api/auth/staff/me/2fa/setup", null, Ct)).Content.ReadFromJsonAsync<SetupBody>(ApiFactory.Json, Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await noMfa.PostAsJsonAsync("/api/auth/staff/me/2fa/enable", new { code = Totp.Code(setup.SharedKey) }, ApiFactory.Json, Ct)).StatusCode);

        var withMfa = await SignIn(Totp.Code(setup.SharedKey));
        Assert.Equal(HttpStatusCode.OK, (await withMfa.GetAsync("/api/admin/staff", Ct)).StatusCode);
        var disable = await withMfa.PostAsJsonAsync("/api/auth/staff/me/2fa/disable", new { code = Totp.Code(setup.SharedKey) }, ApiFactory.Json, Ct);
        Assert.Equal("Customers.Auth.TwoFactorRequiredForRole", JsonNode.Parse(await disable.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>());
    }
}
