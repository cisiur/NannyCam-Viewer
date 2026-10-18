namespace NannyCam.Core;

/// <summary>
/// Minimal validation before a URI is given to a native RTSP implementation.
/// </summary>
public static class RtspUriValidator
{
    public static bool TryValidate(string? value, out Uri? uri, out string error)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Enter an RTSP URI.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = "Enter an absolute rtsp:// URI with a host.";
            return false;
        }

        uri = parsed;
        error = string.Empty;
        return true;
    }
}
