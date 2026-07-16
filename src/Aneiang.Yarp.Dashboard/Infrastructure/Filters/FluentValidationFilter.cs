using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Filters;

/// <summary>
/// Action filter that automatically validates [FromBody] parameters using
/// registered <see cref="IValidator{T}"/> instances. If validation fails,
/// throws <see cref="ValidationException"/> which is caught by <see cref="GlobalExceptionFilter"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="IServiceScopeFactory"/> to resolve scoped validator services
/// from within this singleton filter.
/// </remarks>
public class FluentValidationFilter : IAsyncActionFilter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public FluentValidationFilter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Validate each action argument that has a registered validator
        foreach (var (key, value) in context.ActionArguments)
        {
            if (value == null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(value.GetType());

            // Create a scope to resolve scoped validator services
            using var scope = _scopeFactory.CreateScope();
            var validator = scope.ServiceProvider.GetService(validatorType);
            if (validator == null) continue;

            var validateMethod = validatorType.GetMethod("ValidateAsync", new[] { value.GetType(), typeof(CancellationToken) });
            if (validateMethod == null) continue;

            var task = (Task)validateMethod.Invoke(validator, new object[] { value, context.HttpContext.RequestAborted })!;
            await task;

            var resultProperty = task.GetType().GetProperty("Result");
            var result = resultProperty?.GetValue(task) as FluentValidation.Results.ValidationResult;
            if (result != null && !result.IsValid)
            {
                var errors = result.Errors.Select(e => e.ErrorMessage).ToList();
                throw new Infrastructure.Exceptions.ValidationException(errors);
            }
        }

        await next();
    }
}
