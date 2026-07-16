using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

internal abstract class ConfigManagerBase
{
    protected readonly DynamicConfigState State;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly IDynamicConfigPersister Persister;
    protected readonly IDynamicConfigPublisher Publisher;
    protected readonly IConfigChangeAuditLog AuditLog;

    protected ConfigManagerBase(
        DynamicConfigState state,
        SemaphoreSlim semaphore,
        IDynamicConfigPersister persister,
        IDynamicConfigPublisher publisher,
        IConfigChangeAuditLog auditLog)
    {
        State = state;
        Semaphore = semaphore;
        Persister = persister;
        Publisher = publisher;
        AuditLog = auditLog;
    }

    protected async Task<RouteOperationResult> ExecuteWithLockAsync(
        string operationName,
        string? targetName,
        Func<GatewayDynamicConfig, Task<RouteOperationResult>> action)
    {
        await Semaphore.WaitAsync();
        try
        {
            State.EnsureInitialized();
            var result = await action(State.Config);
            if (result.Success)
                await SaveAndPublishAsync(operationName, targetName);
            return result;
        }
        finally { Semaphore.Release(); }
    }

    protected async Task<bool> ExecuteMetadataWithLockAsync(
        string operationName,
        string? targetName,
        Func<GatewayDynamicConfig, Task<bool>> action)
    {
        await Semaphore.WaitAsync();
        try
        {
            State.EnsureInitialized();
            var modified = await action(State.Config);
            if (modified)
                await SaveAndPublishAsync(operationName, targetName);
            return modified;
        }
        finally { Semaphore.Release(); }
    }

    protected async Task<T> ExecuteReadWithLockAsync<T>(Func<GatewayDynamicConfig, T> action)
    {
        await Semaphore.WaitAsync();
        try
        {
            State.EnsureInitialized();
            return action(State.Config);
        }
        finally { Semaphore.Release(); }
    }

    private async Task SaveAndPublishAsync(string operationName, string? targetName)
    {
        State.IncrementVersion();
        Publisher.Publish(State.Config, State.Version);
        try
        {
            await Persister.SaveAsync(State.Config, operationName, targetName);
        }
        catch (Exception ex)
        {
            LogPersistError(ex, operationName, targetName);
        }
    }

    protected abstract void LogPersistError(Exception ex, string operationName, string? targetName);
}
