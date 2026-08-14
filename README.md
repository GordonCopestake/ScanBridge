# CS3 Scan Bridge

CS3 Scan Bridge is a Windows tray application. It accepts a scan request from an allowed CS3 web origin, uses Windows Image Acquisition (WIA) or TWAIN, creates one PDF in memory, and returns that PDF to the browser. It does not upload the PDF and it does not need NAPS2 to be installed.

## Requirements

- 64-bit Windows 10 or Windows 11. The application process is 64-bit. TWAIN runs in the bundled isolated 32-bit NAPS2 worker for compatibility with 32-bit scanner drivers.
- A WIA or TWAIN scanner driver. The installed Brother DSmobile DS-740D driver uses 32-bit TWAIN.
- A CS3 site that uses HTTPS
- No administrator rights

## First setup

1. Start `CS3.ScanBridge.exe`.
2. The settings window opens if the scanner or allowed origins are not configured.
3. Select `[TWAIN] TW-Brother DS-740D` from the scanner-source list.
4. Add each exact CS3 origin on a separate line. An example is `https://cs3.example.com`.
5. Select **Save**.
6. Restart the application if you changed the listener port.

Settings are in `%LOCALAPPDATA%\CS3 Scan Bridge\settings.json`. The application writes the file atomically. Logs are in `%LOCALAPPDATA%\CS3 Scan Bridge\Logs`. Daily logs are kept for 14 days. Scanned images and PDF bytes are not logged.

The optional start entry is in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The application does not use HKLM and does not need a Windows service.

## Tray commands

- **Open CS3 Scan Bridge** opens the status and settings window.
- **Test scanner** asks for confirmation before it starts a physical scan.
- **Open log folder** opens the local log folder.
- **Start with Windows** changes the current-user startup entry.
- **Exit** stops Kestrel, closes WIA and TWAIN resources, and exits.

Closing the settings window hides it. It does not stop the bridge.

## HTTP API

Kestrel listens only on the literal IPv4 loopback address. The default address is `http://127.0.0.1:9175`. It does not listen on `localhost`, IPv6, a LAN interface, or all interfaces.

### `GET /health`

Returns service, version, busy state, and configured-scanner state.

### `GET /scanners`

Returns scanner provider, ID, and display name for available WIA and TWAIN sources.

### `POST /scan`

The request must have:

- `Content-Type: application/json`
- `X-CS3-Scan-Request: 1`
- an exact allowed `Origin`

The JSON can contain only `correlationId` and `suggestedFilename`. The bridge rejects scan settings, paths, callback URLs, upload URLs, and all unknown fields. The local settings always control the scan.

One scan can run at a time. A second request gets HTTP 409. If a scanner call exceeds the timeout, the request gets HTTP 504. The bridge stays busy until the driver operation ends because WIA and some TWAIN drivers cannot cancel an active transfer safely.

The response has `Content-Type: application/pdf`, an attachment filename, `Cache-Control: no-store`, and a content length.

### Browser example

```javascript
const response = await fetch("http://127.0.0.1:9175/scan", {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
    "X-CS3-Scan-Request": "1"
  },
  body: JSON.stringify({
    correlationId: "example",
    suggestedFilename: "delivery-note.pdf"
  })
});

if (!response.ok) {
  const error = await response.json();
  throw new Error(error.message);
}

const pdfBlob = await response.blob();
const scannedFile = new File(
  [pdfBlob],
  "delivery-note.pdf",
  { type: "application/pdf" }
);

// CS3 can pass scannedFile to its existing file upload function.
```

Current Chromium browsers can show a one-time local-network permission prompt. CS3 must use HTTPS for reliable access from a browser page to the loopback service. The bridge supports the private-network preflight header for an allowed origin only. It never enables credentials or a wildcard CORS origin.

## Scan behavior

The default settings are 300 DPI, greyscale, automatic paper size with an A4 fallback, duplex, JPEG quality 85, a 90 second timeout, and 10 maximum pages. The default filename is `delivery-note-yyyyMMdd-HHmmss.pdf`.

WIA work runs on one dedicated STA thread. TWAIN work runs in the isolated 32-bit NAPS2 SDK worker. The worker owns the TWAIN message loop, data source manager, driver session, and memory transfer. Device ID, exact name, and provider are stored. A WIA device cannot silently match a TWAIN source or the reverse.

The NAPS2 acquisition engines convert each returned side to JPEG in memory. ScanBridge embeds those JPEG bytes directly in the PDF, so it does not apply a second lossy JPEG encoding step. Duplex sides stay in the order that the selected driver returns them.

The bridge limits acquired image data to 128 MiB and the generated PDF to 160 MiB. It stops with HTTP 413 if a document exceeds a limit. The request body is limited to 4 KiB.

## Security design

- Kestrel binds to `127.0.0.1` only.
- Origins use exact ordinal matching. Wildcards and partial host matches are not accepted.
- A present browser origin is checked before every endpoint, including health and scanner enumeration.
- The required custom header prevents a simple HTML form from starting a scan.
- CORS headers are returned only for an allowed origin.
- Browser credentials are not enabled.
- The request cannot choose local paths or network destinations.
- Scan images and PDFs stay in memory and are not written to temporary files.
- Unexpected errors return a generic message and an error ID. Technical details stay in the local log.
- A named mutex permits one application instance per Windows user.
- The application manifest uses `asInvoker`.

Loopback and CORS checks reduce risk, but they do not authenticate a local Windows process. Any process that runs as the same user can call the loopback address and can set HTTP headers. This is a normal limit of a local HTTP bridge.

## Build and test

Use the .NET 10 SDK:

```powershell
dotnet restore CS3.ScanBridge.slnx --locked-mode
dotnet build CS3.ScanBridge.slnx -c Release --no-restore
dotnet test CS3.ScanBridge.slnx -c Release --no-build
```

Automated tests use fake scanner services. They never start a physical scanner.

## Publish

```powershell
dotnet publish src/CS3.ScanBridge/CS3.ScanBridge.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:DebugType=None `
  -o publish/win-x64
```

Trimming is disabled because WIA COM, TWAIN, and Windows Forms depend on native integration, COM metadata, and reflection. The normal-use publish folder contains one self-contained EXE.

## Signed releases

The CI workflow builds and tests each update to `main` and each pull request with locked NuGet dependencies. A tag such as `v1.4.0` starts the release workflow. The workflow will publish a release only after it signs the EXE with Authenticode and verifies the signature.

Configure these GitHub Actions secrets before you push a release tag:

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`: the Base64 text of the code-signing PFX file
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: the PFX password

The workflow fails if either secret is absent. It does not create an unsigned release.

## Troubleshooting

- If the Brother source does not appear, install the full Brother scanner package and confirm that `C:\Windows\twain_32` contains its data source.
- A TWAIN source can remain registered when its scanner is disconnected. If the Brother source appears but cannot open, connect its USB cable and confirm that Windows detects the scanner. Also close NAPS2 and Brother scanning software because a TWAIN source can reject a second connection.
- The Brother DS-740D source requires the isolated NAPS2 32-bit worker and its TWAIN 2 DSM on this computer. Direct in-process TWAIN can list the source but the Brother driver returns `NoDS` when it tries to open it.
- If `/health` reports `scannerAvailable: false`, open settings and select the scanner again. The device ID can change after a driver update.
- If a browser request gets HTTP 403, add the exact scheme, host, and port of the CS3 origin. Do not add a path or wildcard.
- If the browser blocks the request, confirm that CS3 uses HTTPS and accept its local-network permission prompt.
- If a scan times out, wait until the bridge returns to **Ready** before another scan. Then inspect the local log.
- If the port changes, restart the bridge.

## Dependencies and licences

- [PDFsharp](https://github.com/empira/PDFsharp), version 6.2.2, MIT licence. This is the PDF library used at run time.
- [NAPS2 SDK](https://www.nuget.org/packages/NAPS2.Sdk/), version 1.3.0, LGPL 2.1-or-later. ScanBridge uses the matched `NAPS2.Images.Gdi` and `NAPS2.Sdk.Worker.Win32` packages.
- [NAPS2.NTwain](https://www.nuget.org/packages/NAPS2.NTwain/), version 1.0.1, MIT licence. This is a transitive NAPS2 SDK dependency.
- [TWAIN Data Source Manager](https://github.com/twain/twain-dsm), LGPL licence. The NAPS2 32-bit worker includes the modern DSM used to open the Brother source.
- [Serilog.AspNetCore](https://github.com/serilog/serilog-aspnetcore), Apache-2.0 licence.
- [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file), Apache-2.0 licence.
- xUnit.net and Microsoft ASP.NET Core TestHost are test-only dependencies under permissive licences.

No commercial run-time licence is required.

## Implementation review

WIA and TWAIN are driver-defined APIs. A driver can omit a setting, reject a documented value, return more than one page in one TIFF, or ignore cancellation. The bridge applies advertised settings only, releases WIA COM objects, closes TWAIN sessions on success and error paths, and keeps the scan lock until a late driver call ends. Duplex order depends on the selected driver. The code keeps the returned order and expands multi-frame TIFFs without sorting.

The automated checks cover exact origins, wildcard and partial-origin rejection, required headers, request limits, preflight, private-network preflight, health, busy and unavailable scanner responses, timeout, filename sanitizing, settings persistence, scan memory limits, scanner option mapping, device-name fallback, PDF headers, one-page and two-page PDFs, and safe error responses.

A physical document is still needed to confirm DS-740D duplex order, feeder end-of-paper behavior, supported property values, and real scan quality. Do not use **Test scanner** until a document is loaded.

### Verification on 11 August 2026

- The Release solution build passed with no warnings or errors.
- The earlier WIA-only build passed 19 automated tests. The current WIA/TWAIN build has additional provider-routing tests.
- The current self-contained target is `win-x64`. The installed Brother source runs only in the bundled NAPS2 32-bit worker.
- The publish folder contains only `CS3.ScanBridge.exe`.
- The EXE started without a console window and served `/health` with status `ready`.
- `netstat` showed one listener at `127.0.0.1:9175`. It showed no listener on a LAN address or all interfaces.
- A live request from a disallowed origin returned HTTP 403 without an allow-origin header.
- Earlier live WIA enumeration found `Kyocera ECOSYS M8124cidn`. The new build also enumerates the installed `TW-Brother DS-740D` TWAIN source.
- A visual tray-menu check and a clean **Exit** check remain manual. The automated run started the tray process in a hidden desktop state and stopped that verification process after the HTTP checks.
- No live scan was started because no loaded document was confirmed.

### WIA and TWAIN update on 12 August 2026

- The first TWAIN build used an `win-x86` application process. Version 1.2.3 changes the application to `win-x64` and isolates the installed 32-bit Brother data source in the NAPS2 worker, which matches the supported NAPS2 process model.
- NTwain 3.7.6 was added under the MIT licence.
- Live discovery returned `[WIA] Kyocera ECOSYS M8124cidn`, `[TWAIN] TW-Brother DS-740D`, and the Kyocera TWAIN compatibility source.
- The Brother source was discovered without starting a scan.
- The Release build passed with no warnings, and all 22 automated tests passed.
- The final `win-x64` publish folder contains one self-contained EXE.
- Version 1.2.1 uses the NAPS2 SDK 32-bit worker because the Brother driver returns `NoDS` when direct in-process TWAIN tries to open it. A connection-only capability test through the worker opened the source successfully.
- Version 1.2.2 matches the proven NAPS2 DS-740D profile for right-aligned A4 layout and post-scan brightness and contrast processing. This avoids sending controls that make the Brother driver return its generic `Bummer` error.
- Version 1.2.4 keeps the selected WIA device object alive until acquisition completes and refreshes the Windows startup entry after an application update.
- Version 1.2.5 leaves page-count control to the WIA driver and ignores rejected optional WIA settings. Scan Bridge still enforces its configured page limit during transfer.
- Version 1.2.6 selects only WIA document-handling flags that the scanner advertises. ADF selection is required, so a rejected ADF mode cannot fall back to the flatbed.
- Version 1.2.7 checks WIA feeder-ready status before each scan. It uses the ADF when paper is loaded and falls back to a single flatbed page when the ADF is empty.
- Version 1.2.8 lets WIA use the device's preferred image format and applies paper size at device level. This supports eSCL WIA drivers that reject an explicit transfer format.
- Version 1.2.9 sets the WIA acquisition page count before transfer. Duplex ADF requests use an even page count, while flatbed requests remain limited to one page.
- Version 1.2.10 sets the WIA ADF page count to zero, which requests all currently loaded pages. Scan Bridge still limits returned pages to its configured maximum; flatbed scans remain one page.
- Version 1.3.0 replaces raw WIA Automation transfer with the NAPS2 SDK native WIA 2 acquisition engine. The bridge retains its live ADF-to-flatbed source selection and configured page limit.
- Version 1.3.1 enables safe single-file compression while keeping the application self-contained and untrimmed.
- Version 1.4.0 keeps the WinForms loop on its STA thread, caches device discovery, uses live TWAIN device records, limits scan memory, embeds JPEG pages without re-encoding, locks NuGet dependencies, and adds CI plus signed releases.
- The updated process listened only on `127.0.0.1:9175`; a disallowed origin returned HTTP 403 before scanner access.
- A physical document is still required to verify Brother capability negotiation, duplex side order, and scan output.
