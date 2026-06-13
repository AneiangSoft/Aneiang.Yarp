using System.Text;
using Aneiang.Yarp.Models;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

/// <summary>
/// gRPC server-side auth interceptor. Validates credentials against <see cref="GatewayApiAuthOptions"/>.
/// Supports ApiKey (x-api-key header), BasicAuth (Authorization: Basic ...), and Bearer token.
/// Skips validation when auth is not configured (mode=None).
/// </summary>
internal class GrpcAuthInterceptor : Interceptor
{
    private readonly GatewayApiAuthOptions _options;
    private readonly ILogger<GrpcAuthInterceptor> _logger;

    public GrpcAuthInterceptor(
        IOptions<GatewayApiAuthOptions> options,
        ILogger<GrpcAuthInterceptor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (_options.Mode == GatewayApiAuthMode.None)
        {
            return await continuation(request, context);
        }

        if (!ValidateAuth(context))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or missing credentials"));
        }

        return await continuation(request, context);
    }

    private bool ValidateAuth(ServerCallContext context)
    {
        var headers = context.RequestHeaders;

        // Priority 1: Bearer Token
        var authHeader = headers.GetValue("authorization");
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..];
                if (!string.IsNullOrWhiteSpace(_options.Password) &&
                    string.Equals(token, _options.Password, StringComparison.Ordinal))
                {
                    return true;
                }
                _logger.LogDebug("gRPC auth failed: invalid Bearer token");
                return false;
            }

            if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                var credential = authHeader["Basic ".Length..];
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credential));
                    var parts = decoded.Split(':', 2);
                    if (parts.Length == 2 &&
                        string.Equals(parts[0], _options.Username, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(parts[1], _options.Password, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Invalid base64
                }
                _logger.LogDebug("gRPC auth failed: invalid Basic credentials");
                return false;
            }
        }

        // Priority 2: API Key
        var apiKey = headers.GetValue(_options.ApiKeyHeaderName);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (!string.IsNullOrWhiteSpace(_options.ApiKey) &&
                string.Equals(apiKey, _options.ApiKey, StringComparison.Ordinal))
            {
                return true;
            }
            _logger.LogDebug("gRPC auth failed: invalid API key");
            return false;
        }

        _logger.LogDebug("gRPC auth failed: no credentials provided");
        return false;
    }
}
