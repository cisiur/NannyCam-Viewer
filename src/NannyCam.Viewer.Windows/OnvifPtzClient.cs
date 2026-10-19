using System.Globalization;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NannyCam.Core;

namespace NannyCam.Viewer.Windows;

internal sealed record OnvifCameraConfiguration(
    Uri DeviceServiceEndpoint,
    Uri PtzServiceEndpoint,
    string ProfileToken,
    string NodeToken,
    string UserName,
    string Password)
{
    public static OnvifCameraConfiguration FromRtspUri(Uri rtspUri)
    {
        ArgumentNullException.ThrowIfNull(rtspUri);
        if (string.IsNullOrWhiteSpace(rtspUri.Host))
        {
            throw new ArgumentException("The RTSP URI must contain a camera host.", nameof(rtspUri));
        }

        Uri deviceService = new UriBuilder(Uri.UriSchemeHttp, rtspUri.Host, 8899, "/onvif/device_service").Uri;
        Uri ptzService = new UriBuilder(Uri.UriSchemeHttp, rtspUri.Host, 8899, "/onvif/PTZ").Uri;
        return new OnvifCameraConfiguration(deviceService, ptzService, "000", "000", "admin", string.Empty);
    }
}

internal interface IOnvifPtzClient
{
    Task ContinuousMoveAsync(PanTiltVelocity velocity, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

internal interface IOnvifCreatedTimeProvider
{
    Task<DateTimeOffset> GetCreatedAsync(CancellationToken cancellationToken);
}

internal sealed class OnvifPtzClient : IOnvifPtzClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private readonly OnvifCameraConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly IOnvifCreatedTimeProvider _createdTimeProvider;

    public OnvifPtzClient(OnvifCameraConfiguration configuration, HttpClient httpClient, IOnvifCreatedTimeProvider? createdTimeProvider = null)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _createdTimeProvider = createdTimeProvider ?? new OnvifDeviceCreatedTimeProvider(configuration, httpClient);
    }

    public async Task ContinuousMoveAsync(PanTiltVelocity velocity, CancellationToken cancellationToken)
    {
        DateTimeOffset created = await _createdTimeProvider.GetCreatedAsync(cancellationToken).ConfigureAwait(false);
        WsSecurityUsernameToken token = WsSecurityUsernameToken.Create(_configuration.UserName, _configuration.Password, created);
        string envelope = OnvifSoapEnvelope.BuildContinuousMove(_configuration.ProfileToken, velocity, token);
        await SendPtzRequestAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset created = await _createdTimeProvider.GetCreatedAsync(cancellationToken).ConfigureAwait(false);
        WsSecurityUsernameToken token = WsSecurityUsernameToken.Create(_configuration.UserName, _configuration.Password, created);
        string envelope = OnvifSoapEnvelope.BuildStop(_configuration.ProfileToken, token);
        await SendPtzRequestAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendPtzRequestAsync(string envelope, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using HttpRequestMessage request = new(HttpMethod.Post, _configuration.PtzServiceEndpoint)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml"),
        };
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The ONVIF PTZ service returned HTTP {(int)response.StatusCode}.");
        }
    }
}

/// <summary>
/// Uses the camera's Device service to derive the Created timestamp required by
/// WS-Security. This keeps the camera clock quirk outside PTZ movement logic.
/// </summary>
internal sealed class OnvifDeviceCreatedTimeProvider : IOnvifCreatedTimeProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
    private readonly OnvifCameraConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private DateTimeOffset _refreshAfter;
    private TimeSpan _cameraOffset;

    public OnvifDeviceCreatedTimeProvider(OnvifCameraConfiguration configuration, HttpClient httpClient, Func<DateTimeOffset>? utcNow = null)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<DateTimeOffset> GetCreatedAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _utcNow();
        if (now < _refreshAfter)
        {
            return now + _cameraOffset;
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _utcNow();
            if (now < _refreshAfter)
            {
                return now + _cameraOffset;
            }

            DateTimeOffset cameraTime = await GetCameraTimeAsync(cancellationToken).ConfigureAwait(false);
            now = _utcNow();
            _cameraOffset = cameraTime - now;
            _refreshAfter = now + RefreshInterval;
            return now + _cameraOffset;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<DateTimeOffset> GetCameraTimeAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using HttpRequestMessage request = new(HttpMethod.Post, _configuration.DeviceServiceEndpoint)
        {
            Content = new StringContent(OnvifSoapEnvelope.BuildGetSystemDateAndTime(), Encoding.UTF8, "application/soap+xml"),
        };
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The ONVIF Device service returned HTTP {(int)response.StatusCode} while reading camera time.");
        }

        string payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return ParseCameraTime(payload);
    }

    internal static DateTimeOffset ParseCameraTime(string payload)
    {
        XDocument document = XDocument.Parse(payload);
        XElement? dateTime = document.Descendants().FirstOrDefault(element => element.Name.LocalName is "UTCDateTime" or "LocalDateTime");
        if (dateTime is null)
        {
            throw new InvalidOperationException("The ONVIF Device response did not include a camera date/time.");
        }

        XElement? date = dateTime.Elements().FirstOrDefault(element => element.Name.LocalName == "Date");
        XElement? time = dateTime.Elements().FirstOrDefault(element => element.Name.LocalName == "Time");
        if (date is null || time is null)
        {
            throw new InvalidOperationException("The ONVIF Device response included an incomplete camera date/time.");
        }

        return new DateTimeOffset(
            ReadComponent(date, "Year"),
            ReadComponent(date, "Month"),
            ReadComponent(date, "Day"),
            ReadComponent(time, "Hour"),
            ReadComponent(time, "Minute"),
            ReadComponent(time, "Second"),
            TimeSpan.Zero);
    }

    private static int ReadComponent(XElement parent, string name)
    {
        string? value = parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int component)
            ? component
            : throw new InvalidOperationException($"The ONVIF Device response did not include a valid {name} value.");
    }
}

internal sealed record WsSecurityUsernameToken(string UserName, string PasswordDigest, string Nonce, string Created)
{
    internal const string PasswordDigestType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
    internal const string NonceEncodingType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

    public static WsSecurityUsernameToken Create(string userName, string password, DateTimeOffset created, Func<byte[]>? nonceFactory = null)
    {
        byte[] nonce = nonceFactory?.Invoke() ?? RandomNumberGenerator.GetBytes(16);
        if (nonce.Length == 0)
        {
            throw new ArgumentException("A WS-Security nonce must not be empty.", nameof(nonceFactory));
        }

        string createdText = created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        byte[] createdBytes = Encoding.UTF8.GetBytes(createdText);
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] digestInput = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
        Buffer.BlockCopy(nonce, 0, digestInput, 0, nonce.Length);
        Buffer.BlockCopy(createdBytes, 0, digestInput, nonce.Length, createdBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, digestInput, nonce.Length + createdBytes.Length, passwordBytes.Length);

        return new WsSecurityUsernameToken(
            userName,
            Convert.ToBase64String(SHA1.HashData(digestInput)),
            Convert.ToBase64String(nonce),
            createdText);
    }

    public string ToSoapHeaderXml() => $"""
        <wsse:Security soap:mustUnderstand="true">
          <wsse:UsernameToken>
            <wsse:Username>{Escape(UserName)}</wsse:Username>
            <wsse:Password Type="{PasswordDigestType}">{PasswordDigest}</wsse:Password>
            <wsse:Nonce EncodingType="{NonceEncodingType}">{Nonce}</wsse:Nonce>
            <wsu:Created>{Created}</wsu:Created>
          </wsse:UsernameToken>
        </wsse:Security>
        """;

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}

internal static class OnvifSoapEnvelope
{
    internal const string PanTiltVelocitySpace = "http://www.onvif.org/ver10/tptz/PanTiltSpaces/VelocityGenericSpace";

    public static string BuildContinuousMove(string profileToken, PanTiltVelocity velocity, WsSecurityUsernameToken token) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                       xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"
                       xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"
                       xmlns:tptz="http://www.onvif.org/ver20/ptz/wsdl"
                       xmlns:tt="http://www.onvif.org/ver10/schema">
          <soap:Header>{token.ToSoapHeaderXml()}</soap:Header>
          <soap:Body>
            <tptz:ContinuousMove>
              <tptz:ProfileToken>{Escape(profileToken)}</tptz:ProfileToken>
              <tptz:Velocity>
                <tt:PanTilt x="{FormatCoordinate(velocity.X)}" y="{FormatCoordinate(velocity.Y)}" space="{PanTiltVelocitySpace}" />
              </tptz:Velocity>
            </tptz:ContinuousMove>
          </soap:Body>
        </soap:Envelope>
        """;

    public static string BuildStop(string profileToken, WsSecurityUsernameToken token) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                       xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"
                       xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"
                       xmlns:tptz="http://www.onvif.org/ver20/ptz/wsdl">
          <soap:Header>{token.ToSoapHeaderXml()}</soap:Header>
          <soap:Body>
            <tptz:Stop>
              <tptz:ProfileToken>{Escape(profileToken)}</tptz:ProfileToken>
              <tptz:PanTilt>true</tptz:PanTilt>
              <tptz:Zoom>false</tptz:Zoom>
            </tptz:Stop>
          </soap:Body>
        </soap:Envelope>
        """;

    public static string BuildGetSystemDateAndTime() => """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
                       xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
          <soap:Body><tds:GetSystemDateAndTime /></soap:Body>
        </soap:Envelope>
        """;

    private static string FormatCoordinate(double value) => value.ToString("0.0###############", CultureInfo.InvariantCulture);

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
