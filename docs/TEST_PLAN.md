# TC70 MVP acceptance tests (to run on a real Windows 10 PC)

No TC70 device or Windows WPF build runtime was accessible while preparing this archive. **All tests below are pending.**

- [ ] .NET SDK restore and `dotnet publish` complete without compiler errors.
- [ ] EXE starts on a freshly provisioned Windows 10 x64 machine with bundled LibVLC binaries.
- [ ] RTSP `stream1` opens using Tapo Camera Account.
- [ ] RTSP `stream2` opens as lower-bandwidth alternative.
- [ ] Wrong password and unreachable IP fail gracefully without displaying secret values.
- [ ] GetCapabilities resolves only same-IP:2020 Media/PTZ endpoints.
- [ ] Media1 GetProfiles returns a token containing `PTZConfiguration`.
- [ ] Up, down, left and right cause brief movement in expected directions.
- [ ] Each directional movement stops within one second, including when operator clicks Stop.
- [ ] Record runs concurrently with live view (check TC70 firmware for session limits).
- [ ] Stopped `.mkv` plays in VLC and contains video and optional audio.
- [ ] Closing while recording finalizes or safely terminates FFmpeg without leaving orphan process.
- [ ] Inno Setup creates an installer with shortcut and functioning uninstall.
- [ ] Installer never saves Camera Account credentials to disk.
- [ ] All license texts and FFmpeg/native libraries are included as needed for distribution.

## Engineering limitations still to solve

- Do not assume the physical TC70 accepts the SOAP WS-Security flow without verification.
- Camera firmware may require different ONVIF Media service behavior or move-velocity-space attributes.
- The capture prototype does not inspect recorder exit codes or perform auto-retry.
- OnvifPtzClient initialization and disposal could race if the operator changes camera during async PTZ setup; disable controls while connecting in a production version.
- Audio and talkback features vary by model/firmware; only receiving RTSP audio is in scope.
