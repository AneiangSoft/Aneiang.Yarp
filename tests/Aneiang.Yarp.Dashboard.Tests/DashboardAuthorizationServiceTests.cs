using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Aneiang.Yarp.Dashboard.Tests;

public class DashboardAuthorizationServiceTests
{
    [Fact]
    public async Task CheckAccessAsync_WhenAuthDisabled_ShouldReturnTrue()
    {
        // Arrange
        var options = new DashboardOptions
        {
            EnableAuth = false,
            AuthMode = DashboardAuthMode.None
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var service = new DashboardAuthorizationService(optionsMonitor);
        var context = new Mock<Microsoft.AspNetCore.Http.HttpContext>();

        // Act
        var result = await service.CheckAccessAsync(context.Object);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessAsync_WithValidApiKey_ShouldReturnTrue()
    {
        // Arrange
        var options = new DashboardOptions
        {
            EnableAuth = true,
            AuthMode = DashboardAuthMode.ApiKey,
            ApiKey = "test-api-key-123"
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var service = new DashboardAuthorizationService(optionsMonitor);
        
        var request = new Mock<Microsoft.AspNetCore.Http.HttpRequest>();
        request.Setup(r => r.Query["api_key"]).Returns("test-api-key-123");
        
        var context = new Mock<Microsoft.AspNetCore.Http.HttpContext>();
        context.Setup(c => c.Request).Returns(request.Object);

        // Act
        var result = await service.CheckAccessAsync(context.Object);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessAsync_WithInvalidApiKey_ShouldReturnFalse()
    {
        // Arrange
        var options = new DashboardOptions
        {
            EnableAuth = true,
            AuthMode = DashboardAuthMode.ApiKey,
            ApiKey = "test-api-key-123"
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var service = new DashboardAuthorizationService(optionsMonitor);
        
        var request = new Mock<Microsoft.AspNetCore.Http.HttpRequest>();
        request.Setup(r => r.Query["api_key"]).Returns("wrong-key");
        
        var context = new Mock<Microsoft.AspNetCore.Http.HttpContext>();
        context.Setup(c => c.Request).Returns(request.Object);

        // Act
        var result = await service.CheckAccessAsync(context.Object);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAccessAsync_WithCustomDelegate_ShouldInvokeDelegate()
    {
        // Arrange
        bool delegateCalled = false;
        var options = new DashboardOptions
        {
            EnableAuth = true,
            AuthMode = DashboardAuthMode.CustomDelegate,
            AuthorizeRequest = (ctx) =>
            {
                delegateCalled = true;
                return Task.FromResult(true);
            }
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var service = new DashboardAuthorizationService(optionsMonitor);
        var context = new Mock<Microsoft.AspNetCore.Http.HttpContext>();

        // Act
        var result = await service.CheckAccessAsync(context.Object);

        // Assert
        delegateCalled.Should().BeTrue();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessAsync_WhenAuthEnabledButNoMode_ShouldReturnFalse()
    {
        // Arrange
        var options = new DashboardOptions
        {
            EnableAuth = true,
            AuthMode = DashboardAuthMode.None
        };
        var optionsMonitor = CreateOptionsMonitor(options);
        var service = new DashboardAuthorizationService(optionsMonitor);
        var context = new Mock<Microsoft.AspNetCore.Http.HttpContext>();

        // Act
        var result = await service.CheckAccessAsync(context.Object);

        // Assert
        result.Should().BeFalse();
    }

    private static IOptionsMonitor<DashboardOptions> CreateOptionsMonitor(DashboardOptions options)
    {
        var mock = new Mock<IOptionsMonitor<DashboardOptions>>();
        mock.Setup(m => m.CurrentValue).Returns(options);
        return mock.Object;
    }
}
