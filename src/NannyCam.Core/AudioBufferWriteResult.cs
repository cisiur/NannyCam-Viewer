namespace NannyCam.Core;

/// <summary>
/// Describes whether an incoming audio write caused stale audio recovery.
/// </summary>
public readonly record struct AudioBufferWriteResult(int AcceptedBytes, int DroppedBytes, bool RecoveredToLive);
