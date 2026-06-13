namespace Aneiang.Yarp.Services;

/// <summary>
/// Event notifier for gateway configuration changes.
/// Decoupled from persistence — subscribers receive notifications when config changes succeed.
/// </summary>
public interface IConfigChangeNotifier
{
    /// <summary>Raised when a config change audit entry is recorded (success only).</summary>
    event Action<string, string, string?, object?>? OnConfigChanged;
}
