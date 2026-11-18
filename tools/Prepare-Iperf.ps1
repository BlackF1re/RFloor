$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot '../build/native/windows/iperf3'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('rfloor-iperf-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary 'iperf.zip'
    Invoke-WebRequest -Uri 'https://iperf.fr/download/windows/iperf-3.1.3-win64.zip' -OutFile $archive
    $expected = '3c3db693c1bdcc902ca9198fc716339373658233b3392ffe3d467f7695762cd1'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        throw 'iperf3 SHA256 mismatch'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $temporary 'contents')
    $executable = Get-ChildItem (Join-Path $temporary 'contents') -Filter iperf3.exe -Recurse | Select-Object -First 1
    if (-not $executable) { throw 'iperf3.exe missing from archive' }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $executable.DirectoryName | Copy-Item -Destination $destination -Recurse -Force
    & (Join-Path $destination 'iperf3.exe') --version
    if ($LASTEXITCODE -ne 0) { throw 'iperf3 could not start' }
} finally {
    # The absolute target was created above and is restricted to the OS temp folder.
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
