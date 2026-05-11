using Aneiang.Yarp.Dashboard.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Aneiang.Yarp.Dashboard.Tests;

public class LogSanitizerTests
{
    private readonly LogSanitizer _sanitizer;

    public LogSanitizerTests()
    {
        _sanitizer = new LogSanitizer(Microsoft.Extensions.Options.Options.Create(new Models.DashboardOptions()));
    }

    [Fact]
    public void SanitizeHeaders_ShouldRemoveSensitiveHeaders()
    {
        // Arrange
        var headers = new HeaderDictionary
        {
            { "Authorization", "Bearer secret-token" },
            { "Cookie", "session=abc123" },
            { "Content-Type", "application/json" },
            { "X-Custom-Header", "safe-value" }
        };

        // Act
        var result = _sanitizer.SanitizeHeaders(headers);

        // Assert
        result.Should().NotContainKey("Authorization");
        result.Should().NotContainKey("Cookie");
        result.Should().ContainKey("Content-Type");
        result.Should().ContainKey("X-Custom-Header");
        result["Content-Type"].Should().Be("application/json");
    }

    [Fact]
    public void SanitizeJsonBody_ShouldMaskSensitiveFields()
    {
        // Arrange
        var json = @"{
            ""username"": ""john"",
            ""password"": ""secret123"",
            ""token"": ""abc-token"",
            ""email"": ""john@example.com""
        }";

        // Act
        var result = _sanitizer.SanitizeJsonBody(json);

        // Assert
        result.Should().Contain("\"password\": \"***\"");
        result.Should().Contain("\"token\": \"***\"");
        result.Should().Contain("\"username\": \"john\"");
        result.Should().Contain("\"email\": \"john@example.com\"");
    }

    [Fact]
    public void SanitizeJsonBody_WithInvalidJson_ShouldReturnOriginal()
    {
        // Arrange
        var invalidJson = "not valid json";

        // Act
        var result = _sanitizer.SanitizeJsonBody(invalidJson);

        // Assert
        result.Should().Be(invalidJson);
    }

    [Fact]
    public void TruncateBody_ShouldTruncateLongContent()
    {
        // Arrange
        var longContent = new string('A', 5000);

        // Act
        var result = _sanitizer.TruncateBody(longContent);

        // Assert
        result.Length.Should().BeLessOrEqualTo(2048);
        result.Should().EndWith("... [truncated]");
    }

    [Fact]
    public void TruncateBody_ShouldNotTruncateShortContent()
    {
        // Arrange
        var shortContent = "short content";

        // Act
        var result = _sanitizer.TruncateBody(shortContent);

        // Assert
        result.Should().Be(shortContent);
    }

    [Fact]
    public void SanitizeUrl_ShouldRemoveSensitiveQueryParameters()
    {
        // Arrange
        var url = "https://api.example.com/path?token=secret&api_key=key123&name=value";

        // Act
        var result = _sanitizer.SanitizeUrl(url);

        // Assert
        result.Should().NotContain("token=secret");
        result.Should().NotContain("api_key=key123");
        result.Should().Contain("name=value");
    }
}
