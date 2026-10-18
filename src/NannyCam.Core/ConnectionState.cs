namespace NannyCam.Core;

/// <summary>
/// The observable lifecycle of a live camera connection.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}
