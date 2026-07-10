using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Shared base class for <see cref="RouteConfigManager"/> and <see cref="ClusterConfigManager"/>.
/// Provides the common lock/publish/persist pattern so each CRUD method only needs to
/// implement its domain logic.
/// </summary>
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

    /// <summary>
    /// Execute a CRUD operation under lock. The <paramref name="action"/> receives the mutable
    /// <see cref="GatewayDynamicConfig"/> and returns a result. On success the config is
    /// versioned, published to YARP, and persisted (best-effort).
    /// </summary>
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

    /// <summary>
    /// Execute a metadata update under lock. Returns true when <paramref name="action"/>
    /// reports a modification was made.
    /// </summary>
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

    /// <summary>
    /// Execute a read-only operation under lock.
    /// </summary>
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

    /// <summary>
    /// Version bump, publish, and best-effort persist. Must be called while holding the lock.
    /// </summary>
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
            // Persistence is best-effort; in-memory state is authoritative.
            // Next CRUD operation will retry the full save.
            LogPersistError(ex, operationName, targetName);
        }
    }

    /// <summary>Overridden by subclasses to provide typed logger for persist errors.</summary>
    protected abstract void LogPersistError(Exception ex, string operationName, string? targetName);
}
