namespace NannyCam.Core;

/// <summary>
/// A UI-neutral description of the current connection state.
/// </summary>
public sealed record ConnectionStatus(ConnectionState State, string Message)
{
    public static ConnectionStatus Disconnected { get; } = new(ConnectionState.Disconnected, "Disconnected");
}
