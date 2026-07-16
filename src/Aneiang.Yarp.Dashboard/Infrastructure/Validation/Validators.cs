using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.Notification.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using FluentValidation;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Validation;

/// <summary>Validator for <see cref="LoginRequest"/>.</summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}

/// <summary>Validator for <see cref="LogSettingsUpdateRequest"/>.</summary>
public class LogSettingsUpdateRequestValidator : AbstractValidator<LogSettingsUpdateRequest>
{
    public LogSettingsUpdateRequestValidator()
    {
        When(x => x.LogMetaRetentionDays.HasValue, () =>
            RuleFor(x => x.LogMetaRetentionDays!.Value)
                .InclusiveBetween(1, 365).WithMessage("LogMetaRetentionDays must be between 1 and 365"));

        When(x => x.LogBodyRetentionDays.HasValue, () =>
            RuleFor(x => x.LogBodyRetentionDays!.Value)
                .InclusiveBetween(1, 365).WithMessage("LogBodyRetentionDays must be between 1 and 365"));

        When(x => x.LogSamplingRate.HasValue, () =>
            RuleFor(x => x.LogSamplingRate!.Value)
                .InclusiveBetween(0.0, 1.0).WithMessage("LogSamplingRate must be between 0.0 and 1.0"));

        When(x => x.LogMaxBodyLength.HasValue, () =>
            RuleFor(x => x.LogMaxBodyLength!.Value)
                .InclusiveBetween(256, 1048576).WithMessage("LogMaxBodyLength must be between 256 and 1048576"));

        When(x => x.LogBufferCapacity.HasValue, () =>
            RuleFor(x => x.LogBufferCapacity!.Value)
                .GreaterThanOrEqualTo(16).WithMessage("LogBufferCapacity must be at least 16"));

        When(x => !string.IsNullOrEmpty(x.MinLogLevel), () =>
            RuleFor(x => x.MinLogLevel!)
                .Must(level => new[] { "Debug", "Information", "Warning", "Error", "Critical" }
                    .Contains(level, StringComparer.OrdinalIgnoreCase))
                .WithMessage("MinLogLevel must be one of: Debug, Information, Warning, Error, Critical"));
    }
}

/// <summary>Validator for <see cref="ChannelRequest"/>.</summary>
public class ChannelRequestValidator : AbstractValidator<ChannelRequest>
{
    public ChannelRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Channel name is required")
            .MaximumLength(100).WithMessage("Channel name must not exceed 100 characters");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Channel type is required");

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("Channel URL is required")
            .Must(BeValidUrl).WithMessage("Channel URL must be a valid HTTP/HTTPS URL");
    }

    private static bool BeValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
