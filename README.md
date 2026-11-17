# RFloor

Draw a floor plan, measure Wi-Fi at known locations, and inspect estimated coverage between measurements.

[![Build](https://github.com/BlackF1re/RFloor/actions/workflows/build.yml/badge.svg)](https://github.com/BlackF1re/RFloor/actions/workflows/build.yml)

RFloor uses **C# / .NET 10 / Avalonia** on Windows, Linux and Android. `app/RFloor` contains the shared editor, documents, measurements, localization and coverage model; `app/RFloor.Desktop` and `app/RFloor.Android` provide platform hosts.

Desktop versions share the same interface. On phones, side panels become drawers, tools scroll horizontally, and the canvas supports pinch zoom and two-finger panning. The bottom controls provide one-finger panning and solid/outline primitive drawing. Tap an existing measurement to inspect its results; use the object panel for precise values, deletion and renaming.

Windows ZIP/installers, Linux tarballs and Android APKs include .NET and iperf3. Extract the entire desktop archive. Start `RFloor.Desktop.exe` on Windows or `./AppRun` on Linux. Android requires Android 8.0 or later and a 64-bit ARM or x64 device. Release APKs use a persistent signing key for updates; previous development APKs must be uninstalled before the first release installation. Back up your `.rfplan` files first.

Linux requires a graphical desktop, fontconfig, X11 libraries (or XWayland), and `iw` for Wi-Fi information. On Ubuntu/Debian: `sudo apt install iw iputils-ping libfontconfig1 libice6 libsm6 libx11-6 libxrandr2 libxi6`. On Android, grant precise location / nearby Wi-Fi permissions and enable system Location so Android exposes SSID, BSSID and RSSI. Measurements report the currently connected access point; RFloor does not scan other networks. Throughput requires a reachable iperf3 server, default `192.168.2.1:5201`.

Create or import a `.rfplan` project. **Plan** mode edits rooms, walls, openings, labels, furniture primitives, routers, and tape measurements. Room dimensions describe the interior. **Survey** mode places measurement points and displays results and heatmaps. Both modes share the same file; each document has its own tab.

Choose the interface language (**English** or **Русский**), enabled measurements, thresholds, and keyboard shortcuts in **Settings → Preferences**. Adding a point runs all enabled measurements. Throughput testing requires a reachable iperf3 server; the default target is `192.168.2.1:5201`. Wi-Fi information depends on system permissions and adapter support. The application does not infer a Wi-Fi generation or invent a router client count.

## Coverage estimates

Heatmaps interpolate measurements for the selected access point, frequency band, and metric. At least three non-collinear locations in a room are required. Local Delaunay triangles use linear interpolation, preserve measured values, and avoid extrapolation outside the measured boundary. Repeated coordinates use the latest reading. Predictions do not cross walls; supporting samples must be within 4.5 metres of the predicted location. This distance is a conservative application heuristic, not a radio propagation constant.

Uncovered locations and insufficiently surveyed areas remain transparent. Missing measurements do not imply absent coverage. RSSI and link-quality percentages are not mixed. Opacity indicates distance-based support, not statistical confidence. Ping and throughput maps describe those measured metrics rather than RF signal strength.

Distribute samples throughout each room, around obstacles, and near coverage transitions. Adding samples in previously unsurveyed areas can improve predictions; repeated or noisy readings cannot guarantee improvement. Check predicted locations with independent measurements. RFloor uses survey interpolation, without inferring an access point's position from a client measurement or modelling wall materials.

This approach follows the survey practices of [CWNP](https://www.cwnp.com/seven-rules-for-accurate-site-surveys/): accurate measurement locations and bounded interpolation distance. [Esri's interpolation guidance](https://doc.esri.com/en/arcgis-pro/latest/help/analysis/geostatistical-analyst/how-inverse-distance-weighted-interpolation-works.html) describes sensitivity to sampling density and clustering. [SciPy documents the Delaunay/linear interpolation method](https://docs.scipy.org/doc/scipy/reference/generated/scipy.interpolate.LinearNDInterpolator.html). [ITU-R P.1238](https://www.itu.int/rec/R-REC-P.1238-13-202509-I/en) covers indoor propagation models; RFloor does not claim to implement or conform to those models.

## Development

Install the .NET 10 SDK. The shared solution is `RFloor.slnx`; Android builds also require the .NET Android workload, JDK 21 and Android SDK 36.

```sh
dotnet run --project app/RFloor.Desktop
dotnet run --project tests/RFloor.Checks -c Release
```

Prepare iperf3 before publishing: `./build/Prepare-Iperf.ps1` on Windows, `bash build/iperf/build.sh linux` on Linux. Publish the desktop host with `dotnet publish app/RFloor.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish` (or `win-arm64`, `linux-x64`, `linux-arm64`). Build Linux dependencies on the target architecture.

For Android, run `dotnet workload install android`, set `ANDROID_NDK_HOME` to NDK 28.2.13676358, run `bash build/iperf/build.sh android` on Linux, then `dotnet publish app/RFloor.Android -c Release`. The native client is linked into the APK for ARM64 and x64; it does not execute a downloaded binary. Publishing fails if bundled iperf3 is missing. The upstream source archive is version-pinned and SHA-256 verified.

GitHub **Build** runs shared editor, touch, layout, document, localization and coverage checks, Linux startup and live ICMP/iperf3 checks, and Android emulator editor/native network checks. It produces Windows x64/ARM64 installers and ZIPs, Linux x64/ARM64 tarballs, and an Android APK. Verification code is excluded from release APKs. **Build and Release** publishes these artifacts and SHA-256 checksums after all builds pass. `Directory.Build.props` defines the version. Android signing uses repository secrets `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD` (PKCS12, alias `rfloor`); pull requests without secrets use development signing. Windows installers are unsigned until a publisher certificate is configured.

See the [document schema](docs/FORMAT.md). Sample plans are in `examples`.
