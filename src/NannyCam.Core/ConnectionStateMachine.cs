namespace NannyCam.Core;

/// <summary>
/// Provides the small, thread-safe state policy shared by camera frontends.
/// </summary>
public sealed class ConnectionStateMachine
{
    private readonly object _sync = new();
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    public ConnectionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public bool TryBeginConnecting()
    {
        lock (_sync)
        {
            if (_status.State is not (ConnectionState.Disconnected or ConnectionState.Error))
            {
                return false;
            }

            _status = new ConnectionStatus(ConnectionState.Connecting, "Connecting…");
            return true;
        }
    }

    public void MarkConnected() => Set(ConnectionState.Connected, "Connected");

    public void MarkDisconnected() => Set(ConnectionState.Disconnected, "Disconnected");

    public void MarkError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Set(ConnectionState.Error, message);
    }

    private void Set(ConnectionState state, string message)
    {
        lock (_sync)
        {
            _status = new ConnectionStatus(state, message);
        }
    }
}
