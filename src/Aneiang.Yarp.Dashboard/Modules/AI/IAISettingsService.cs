namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Runtime AI settings service interface.
/// Allows reading/updating AI options without requiring application restart.
/// </summary>
public interface IAISettingsService
{
    /// <summary>Whether the "custom" provider choice is exposed to the frontend.</summary>
    bool AllowCustomProvider { get; }

    /// <summary>Returns current AI settings (API key masked).</summary>
    AISettingsDto GetSettings();

    /// <summary>
    /// Update AI settings at runtime.
    /// All numeric parameters are clamped to safe ranges.
    /// Settings are persisted to SQLite so they survive restarts.
    /// </summary>
    void UpdateSettings(AISettingsDto dto);
}
