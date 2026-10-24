using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TapoDesktop.Services;

/// <summary>
/// Minimal ONVIF SOAP 1.2 Media1/PTZ client for a camera owned by the operator.
/// Interoperability varies by firmware; tested network access to a TC70 is still required.
/// Credentials are held in memory and never saved by this prototype.
/// </summary>
public sealed class OnvifPtzClient : IDisposable
{
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Security = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private static readonly XNamespace Utility = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private static readonly XNamespace Device = "http://www.onvif.org/ver10/device/wsdl";
    private static readonly XNamespace Media = "http://www.onvif.org/ver10/media/wsdl";
    private static readonly XNamespace Ptz = "http://www.onvif.org/ver20/ptz/wsdl";
    private static readonly XNamespace Types = "http://www.onvif.org/ver10/schema";
    private const string PasswordDigestUri = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
    private const string Base64Uri = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

    private readonly string _username;
    private readonly string _password;
    private readonly string _host;
    private readonly int _port;
    private readonly Uri _deviceUri;
    private Uri? _ptzUri;
    private string? _profileToken;

    public OnvifPtzClient(string cameraIpv4, int port, string username, string password)
    {
        _host = cameraIpv4;
        _port = port;
        _username = username;
        _password = password;
        _deviceUri = new Uri($"http://{cameraIpv4}:{port}/onvif/device_service");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var caps = await SendAsync(_deviceUri, Device + "GetCapabilities",
            new XElement(Device + "GetCapabilities", new XElement(Device + "Category", "All")), cancellationToken);
        var capsNode = caps.Descendants().FirstOrDefault(n => n.Name.LocalName == "Capabilities")
            ?? throw new InvalidOperationException("ONVIF capabilities not found.");
        string mediaAddress = ChildAddress(capsNode, "Media");
        string ptzAddress = ChildAddress(capsNode, "PTZ");
        var mediaUri = TrustedServiceUrl(mediaAddress);
        _ptzUri = TrustedServiceUrl(ptzAddress);

        var profiles = await SendAsync(mediaUri, Media + "GetProfiles", new XElement(Media + "GetProfiles"), cancellationToken);
        var allProfiles = profiles.Descendants().Where(n => n.Name.LocalName == "Profiles").ToList();
        // A PTZ-enabled media profile is needed by the ONVIF PTZ service.
        var profile = allProfiles.FirstOrDefault(p => p.Elements().Any(x => x.Name.LocalName == "PTZConfiguration"));
        _profileToken = profile?.Attribute("token")?.Value;
        if (string.IsNullOrWhiteSpace(_profileToken))
            throw new InvalidOperationException("No PTZ-enabled ONVIF media profile was returned by the camera.");
    }

    private static string ChildAddress(XElement caps, string service)
    {
        var node = caps.Elements().FirstOrDefault(x => x.Name.LocalName == service);
        var address = node?.Elements().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value;
        return !string.IsNullOrWhiteSpace(address)
            ? address
            : throw new InvalidOperationException($"Camera did not advertise ONVIF {service} service.");
    }

    private Uri TrustedServiceUrl(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !uri.Host.Equals(_host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != _port)
            throw new InvalidOperationException("Camera advertised an unexpected ONVIF service URL. Check IP/firmware.");
        return uri;
    }

    public async Task PulseMoveAsync(double pan, double tilt, TimeSpan duration, CancellationToken ct = default)
    {
        if (_ptzUri is null || _profileToken is null)
            throw new InvalidOperationException("Initialize ONVIF first.");
        // This camera's firmware returns HTTP 500 (and dumps raw debug text instead of a
        // clean SOAP fault) when ContinuousMove includes a fractional-second Timeout, so the
        // app relies solely on its own Task.Delay + explicit StopAsync below to end the move.
        var body = new XElement(Ptz + "ContinuousMove",
            new XElement(Ptz + "ProfileToken", _profileToken),
            new XElement(Ptz + "Velocity", new XElement(Types + "PanTilt",
                new XAttribute("x", pan.ToString("0.###", CultureInfo.InvariantCulture)),
                new XAttribute("y", tilt.ToString("0.###", CultureInfo.InvariantCulture)))));
        try
        {
            await SendAsync(_ptzUri, Ptz + "ContinuousMove", body, ct);
            await Task.Delay(duration, ct);
        }
        finally
        {
            // Also send an explicit stop even if a movement request fails mid-way.
            await StopAsync(CancellationToken.None);
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_ptzUri is null || _profileToken is null) return;
        var body = new XElement(Ptz + "Stop",
            new XElement(Ptz + "ProfileToken", _profileToken),
            new XElement(Ptz + "PanTilt", true));
        await SendAsync(_ptzUri, Ptz + "Stop", body, ct);
    }

    private async Task<XDocument> SendAsync(Uri endpoint, XName action, XElement payload, CancellationToken ct)
    {
        string created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        byte[] createdBytes = Encoding.UTF8.GetBytes(created);
        byte[] pwdBytes = Encoding.UTF8.GetBytes(_password);
        byte[] input = new byte[nonce.Length + createdBytes.Length + pwdBytes.Length];
        Buffer.BlockCopy(nonce, 0, input, 0, nonce.Length);
        Buffer.BlockCopy(createdBytes, 0, input, nonce.Length, createdBytes.Length);
        Buffer.BlockCopy(pwdBytes, 0, input, nonce.Length + createdBytes.Length, pwdBytes.Length);
        string digest = Convert.ToBase64String(SHA1.HashData(input)); // Required by WS-UsernameToken Digest specification.
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XElement(Soap + "Header",
                    new XElement(Security + "Security",
                        new XAttribute(Soap + "mustUnderstand", "1"),
                        new XElement(Security + "UsernameToken",
                            new XElement(Security + "Username", _username),
                            new XElement(Security + "Password", new XAttribute("Type", PasswordDigestUri), digest),
                            new XElement(Security + "Nonce", new XAttribute("EncodingType", Base64Uri), Convert.ToBase64String(nonce)),
                            new XElement(Utility + "Created", created)))),
                new XElement(Soap + "Body", payload)));
        byte[] bodyBytes = Encoding.UTF8.GetBytes(envelope.ToString(SaveOptions.DisableFormatting));
        // Written with correct CRLF framing regardless of how the camera's own server responds.
        // The camera's primitive HTTP parser breaks on an action= parameter or a SOAPAction
        // header, so neither is sent — a bare Content-Type is all this firmware tolerates.
        string requestHeader =
            $"POST {endpoint.PathAndQuery} HTTP/1.1\r\n" +
            $"Host: {endpoint.Host}:{endpoint.Port}\r\n" +
            $"Content-Type: application/soap+xml; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, ct);
        using var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(requestHeader), ct);
        await stream.WriteAsync(bodyBytes, ct);

        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        // Some camera firmware uses bare LF line endings, which the strict BCL HTTP
        // client rejects outright, so the raw response is read and parsed leniently here.
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            ms.Write(buffer, 0, read);

        string rawText = Encoding.UTF8.GetString(ms.ToArray());
        // This camera's firmware occasionally prepends a raw debug dump before the real
        // status line on error paths, so the response is parsed starting at "HTTP/1.".
        int httpIdx = rawText.IndexOf("HTTP/1.", StringComparison.Ordinal);
        if (httpIdx > 0)
            rawText = rawText[httpIdx..];
        string sep = rawText.Contains("\r\n\r\n") ? "\r\n\r\n" : "\n\n";
        int splitIdx = rawText.IndexOf(sep, StringComparison.Ordinal);
        if (splitIdx < 0)
            throw new InvalidOperationException("ONVIF response had no header/body separator.");
        string headerPart = rawText[..splitIdx];
        string bodyPart = rawText[(splitIdx + sep.Length)..];

        string statusLine = headerPart.Split('\n')[0].TrimEnd('\r');
        string[] statusParts = statusLine.Split(' ', 3);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out int statusCode))
            throw new InvalidOperationException($"ONVIF returned an unparseable status line: '{statusLine}'.");

        XDocument? doc = null;
        try
        {
            using var bodyReader = XmlReader.Create(new StringReader(bodyPart), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000
            });
            doc = XDocument.Load(bodyReader);
        }
        catch (XmlException) when (statusCode is < 200 or >= 300)
        {
            // Fall through to the HTTP-status error below; the body wasn't valid SOAP.
        }

        if (doc is not null && doc.Descendants().Any(e => e.Name.LocalName == "Fault"))
        {
            string? reason = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Text")?.Value;
            throw new InvalidOperationException($"ONVIF returned a SOAP fault: {reason ?? "unknown reason"}.");
        }

        if (statusCode is < 200 or >= 300)
            throw new InvalidOperationException($"ONVIF returned HTTP {statusCode}. Verify Camera Account and clock synchronization.");

        return doc!;
    }

    public void Dispose() { }
}
