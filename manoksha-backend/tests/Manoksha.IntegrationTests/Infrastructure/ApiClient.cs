using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Manoksha.Integrations.Sms;

namespace Manoksha.IntegrationTests.Infrastructure;

public sealed record Tokens(string AccessToken, string RefreshToken);

/// <summary>Thin HTTP helpers so tests read like the business scenarios they check.</summary>
public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<JsonNode> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text)!;
    }

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync())["code"]?.GetValue<string>();

    public static HttpClient Authorized(this ManokshaApiFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<HttpResponseMessage> InternalLoginRawAsync(this ManokshaApiFactory factory, string email, string password, string client = "admin") =>
        await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/login", new { email, password, client });

    public static async Task<Tokens> LoginAsync(this ManokshaApiFactory factory, string email, string password, string client = "admin")
    {
        var response = await factory.InternalLoginRawAsync(email, password, client);
        var body = await response.ReadJsonAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body.ToJsonString());
        body["status"]!.GetValue<string>().Should().Be("AUTHENTICATED", body.ToJsonString());
        return ToTokens(body);
    }

    public static Tokens ToTokens(JsonNode body) =>
        new(body["tokens"]!["accessToken"]!.GetValue<string>(), body["tokens"]!["refreshToken"]!.GetValue<string>());

    public static Task<Tokens> LoginOwnerAsync(this ManokshaApiFactory factory) =>
        factory.LoginAsync(ManokshaApiFactory.OwnerEmail, ManokshaApiFactory.OwnerPassword);

    /// <summary>Creates an internal user as the Owner, completes first-login password change and assigns a role.</summary>
    public static async Task<(Guid UserId, string Email, string Password)> CreateInternalUserAsync(
        this ManokshaApiFactory factory, string roleCode, Guid? branchId, string? password = null)
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var email = $"u{Guid.NewGuid():N}"[..13] + "@test.manoksha";
        var create = await owner.PostAsJsonAsync("/api/v1/admin/users", new { email, displayName = $"Test {roleCode}", reason = "test setup" });
        var created = await create.ReadJsonAsync();
        create.StatusCode.Should().Be(HttpStatusCode.Created, created.ToJsonString());
        var userId = created["userId"]!.GetValue<Guid>();
        var temporary = created["temporaryPassword"]!.GetValue<string>();

        var roleId = await factory.RoleIdAsync(roleCode);
        var assign = await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/role-assignments", new { roleId, branchId, reason = "test setup" });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, (await assign.ReadJsonAsync()).ToJsonString());

        password ??= "Test-Passw0rd-" + Guid.NewGuid().ToString("N")[..6];
        var login = await (await factory.InternalLoginRawAsync(email, temporary)).ReadJsonAsync();
        login["status"]!.GetValue<string>().Should().Be("PASSWORD_CHANGE_REQUIRED");
        var change = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/password/first-change",
            new { challengeToken = login["challengeToken"]!.GetValue<string>(), newPassword = password });
        change.StatusCode.Should().Be(HttpStatusCode.OK, (await change.ReadJsonAsync()).ToJsonString());
        return (userId, email, password);
    }

    public static async Task<Guid> RoleIdAsync(this ManokshaApiFactory factory, string roleCode)
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var roles = await (await owner.GetAsync("/api/v1/admin/roles")).ReadJsonAsync();
        return roles.AsArray().Single(r => r!["code"]!.GetValue<string>() == roleCode)!["id"]!.GetValue<Guid>();
    }

    public static string NewMobile() => "+9187" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string LastOtpFor(string e164)
    {
        var message = FakeSmsSender.LastMessageTo(e164) ?? throw new InvalidOperationException($"No SMS sent to {e164}");
        return message.Body[..6];
    }

    /// <summary>Registers a new customer via OTP and returns tokens.</summary>
    public static async Task<Tokens> RegisterCustomerAsync(this ManokshaApiFactory factory, string mobile)
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        var verify = await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = LastOtpFor(mobile) })).ReadJsonAsync();
        verify["status"]!.GetValue<string>().Should().Be("REGISTRATION_REQUIRED");
        var register = await client.PostAsJsonAsync("/api/v1/auth/customer/register",
            new { registrationToken = verify["challengeToken"]!.GetValue<string>(), fullName = "Test Customer", email = "customer@example.com" });
        var body = await register.ReadJsonAsync();
        register.StatusCode.Should().Be(HttpStatusCode.OK, body.ToJsonString());
        return ToTokens(body);
    }

    /// <summary>Internal user with a role AND an employee profile at the branch (via the Owner).</summary>
    public static async Task<(Guid UserId, string Email, string Password, Guid EmployeeId)> CreateEmployeeAsync(
        this ManokshaApiFactory factory, string roleCode, Guid branchId)
    {
        var (userId, email, password) = await factory.CreateInternalUserAsync(roleCode, branchId);
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var response = await owner.PostAsJsonAsync("/api/v1/admin/employees",
            new { fullName = "Emp " + email[..6], existingUserId = userId, assignedBranchId = branchId, reason = "test setup" });
        var body = await response.ReadJsonAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToJsonString());
        return (userId, email, password, body["employee"]!["id"]!.GetValue<Guid>());
    }

    public static async Task<HttpClient> OwnerClientAsync(this ManokshaApiFactory factory) =>
        factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);

    public static async Task<JsonNode> OkJsonAsync(this Task<HttpResponseMessage> call, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await call;
        var body = await response.ReadJsonAsync();
        response.StatusCode.Should().Be(expected, body.ToJsonString());
        return body;
    }
}
