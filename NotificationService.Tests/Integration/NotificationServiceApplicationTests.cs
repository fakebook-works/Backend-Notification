using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NotificationService.Tests.Integration;

public sealed class NotificationServiceApplicationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationDb"] =
                "Host=127.0.0.1;Port=5432;Database=notification_service_test;Username=postgres;Password=test",
            ["InternalAuthentication:GatewaySecret"] = GatewaySecret,
            ["InternalAuthentication:NotificationServiceSecret"] = NotificationServiceSecret,
            ["Snowflake:NodeId"] = "7"
        });

        _app = Program.BuildApplication(builder);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GraphQl_rejects_requests_without_trusted_gateway_headers()
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query = "{ __typename }" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GraphQl_accepts_a_trusted_gateway_request()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query = "{ __typename }" })
        };
        request.Headers.Add("X-Gateway-Secret", GatewaySecret);
        request.Headers.Add("X-User-Id", "42");

        var response = await _client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rootTypeName = document.RootElement
            .GetProperty("data")
            .GetProperty("__typename")
            .GetString();

        Assert.Equal("Query", rootTypeName);
    }

    [Fact]
    public async Task Federation_service_sdl_exposes_the_notification_entity_key()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = JsonContent.Create(new { query = "{ _service { sdl } }" })
        };
        request.Headers.Add("X-Gateway-Secret", GatewaySecret);
        request.Headers.Add("X-User-Id", "42");

        var response = await _client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sdl = document.RootElement
            .GetProperty("data")
            .GetProperty("_service")
            .GetProperty("sdl")
            .GetString();

        Assert.Contains("type Notification @key(fields: \"id\")", sdl);
        Assert.Contains("notificationCreated", sdl);
    }

    [Fact]
    public async Task Internal_rest_endpoint_rejects_a_missing_service_secret_before_database_access()
    {
        var response = await _client.PostAsJsonAsync("/internal/notifications", new
        {
            creatorId = 1,
            receiverId = 2,
            actionType = 0,
            objectId = 3
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Internal_rest_endpoint_validates_the_create_payload_before_database_access()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/notifications")
        {
            Content = JsonContent.Create(new
            {
                creatorId = 1,
                receiverId = 2,
                actionType = 0,
                objectId = 0
            })
        };
        request.Headers.Add("X-Internal-NotificationService-Secret", NotificationServiceSecret);
        request.Headers.Add("Idempotency-Key", "invalid-payload-test");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private const string GatewaySecret = "gateway-integration-test-secret";
    private const string NotificationServiceSecret = "notification-integration-test-secret";
}
