# Build & test

Install the .NET 10 SDK and run the commands from the repository root.

## Desktop

Prepare the bundled iperf3 client. On Windows, use PowerShell 7.

```powershell
./tools/Prepare-Iperf.ps1
```

On Linux, install a C compiler, `make`, `binutils`, and `curl`. Build on the architecture you intend to target.

```sh
bash tools/iperf/build.sh linux
```

Run RFloor and the shared checks.

```sh
dotnet run --project app/RFloor.Desktop
dotnet run --project tests/RFloor.Checks -c Release
```

Publish a self-contained build. Choose the runtime identifier for your platform from `win-x64`, `win-arm64`, `linux-x64`, and `linux-arm64`.

```sh
dotnet publish app/RFloor.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Android

On x64 Linux, install JDK 21, Android SDK 36, Build Tools 36.0.0, and NDK 28.2.13676358. Set `JAVA_HOME` and `ANDROID_HOME` to the JDK and SDK directories.

```sh
dotnet workload install android
ANDROID_NDK_HOME="$ANDROID_HOME/ndk/28.2.13676358" bash tools/iperf/build.sh android
dotnet publish app/RFloor.Android -c Release -o artifacts/android
```

## CI & releases

[Build](../.github/workflows/build.yml) runs the shared checks, verifies startup and networking on Linux and Android, and packages all three platforms.

[Build and Release](../.github/workflows/build-and-release.yml) publishes the packages and SHA-256 checksums after the build passes. Enter the release version when running the workflow. Regular CI builds take their version from [Directory.Build.props](../Directory.Build.props). Android releases use a PKCS#12 keystore with the alias `rfloor` and the repository secrets `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD`.
