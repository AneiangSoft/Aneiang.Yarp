namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>Result type for chat-with-tools processing.</summary>
public enum ChatResultType
{
    /// <summary>Plain text response (no pending action).</summary>
    Text,
    /// <summary>Write operation needs user confirmation.</summary>
    PendingAction,
    /// <summary>Error occurred.</summary>
    Error
}
