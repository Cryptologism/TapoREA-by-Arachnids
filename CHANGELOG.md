# Changelog

## [0.1.1] - 2026-10-25

### Fixed
- Missing `using System.Net.Http;` in `OnvifPtzClient.cs` that failed the Release build.
- PTZ `ContinuousMove`/`Stop` requests failing with HTTP 500 against this camera's firmware:
  - Replaced the `HttpClient`-based ONVIF transport with a raw TCP socket implementation.
    The camera's embedded HTTP server does not reliably reply in a way the strict BCL
    HTTP/1.1 parser accepts, and on error paths it writes an unstructured debug dump to
    the socket before the real status line. The response is now parsed starting from the
    first `HTTP/1.` token found, instead of assuming it begins at offset 0.
  - Dropped the `SOAPAction` HTTP header and the `action=` parameter on `Content-Type`.
    This camera's SOAP parser returns a malformed response when either is present; a bare
    `Content-Type: application/soap+xml; charset=utf-8` is all it tolerates.
  - Removed the `<Timeout>` element from `ContinuousMove` requests. Sending a fractional-second
    ONVIF duration (e.g. `PT0.35S`) crashes this firmware's request handler and it returns
    HTTP 500. The app already enforces the move duration locally via `Task.Delay` followed by
    an explicit `StopAsync`, so the camera-side timeout was redundant.

### Notes
- These PTZ fixes were diagnosed and verified against a real TP-Link Tapo TC70 unit (firmware
  current as of 2026-10). Other Tapo/ONVIF camera models may not need the same workarounds, or
  may need different ones — ONVIF compliance varies a lot by firmware. If PTZ fails on your
  camera, capture the raw TCP response (see `docs/ARCHITECTURE.md`) before assuming the same fix
  applies.

## [0.1.0] - initial starter

- Initial source-code starter: WPF shell, RTSP video via LibVLCSharp, ONVIF Media1/PTZ client,
  FFmpeg stream-copy recording, Inno Setup installer script. Not compiled, not camera-tested at
  this revision.
