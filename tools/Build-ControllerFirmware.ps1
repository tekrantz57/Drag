[CmdletBinding()]
param(
    [string]$ArduinoCliPath = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sketchDirectory = Join-Path $repoRoot "dragMC"
$versionHeader = Join-Path $sketchDirectory "FirmwareVersion.h"
$distDirectory = Join-Path $sketchDirectory "dist"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\controller-firmware"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$buildDirectory = Join-Path $OutputRoot "mega-build"
$packageStage = Join-Path $OutputRoot "package-stage"

if ([string]::IsNullOrWhiteSpace($ArduinoCliPath)) {
    $command = Get-Command arduino-cli -ErrorAction SilentlyContinue
    if ($command) {
        $ArduinoCliPath = $command.Source
    }
    else {
        $programFilesCli = Join-Path $env:ProgramFiles "Arduino CLI\arduino-cli.exe"
        if (Test-Path -LiteralPath $programFilesCli) {
            $ArduinoCliPath = $programFilesCli
        }
    }
}
if ([string]::IsNullOrWhiteSpace($ArduinoCliPath) -or
    -not (Test-Path -LiteralPath $ArduinoCliPath -PathType Leaf)) {
    throw "Arduino CLI was not found. Pass -ArduinoCliPath with the full executable path."
}

$versionText = Get-Content -LiteralPath $versionHeader -Raw
$versionMatch = [regex]::Match(
    $versionText,
    '#define\s+DRAGMC_FIRMWARE_VERSION\s+"([^"]+)"')
if (-not $versionMatch.Success) {
    throw "Could not read DRAGMC_FIRMWARE_VERSION from $versionHeader"
}
$firmwareVersion = $versionMatch.Groups[1].Value

$coreLines = & $ArduinoCliPath core list
if ($LASTEXITCODE -ne 0) {
    throw "Arduino CLI could not list installed cores."
}
$avrLine = $coreLines | Where-Object { $_ -match '^arduino:avr\s+' } | Select-Object -First 1
if (-not $avrLine) {
    throw "The arduino:avr core is not installed."
}
$arduinoCoreVersion = ($avrLine -split '\s+')[1]

foreach ($directory in @($buildDirectory, $packageStage)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory | Out-Null
}
New-Item -ItemType Directory -Force -Path $distDirectory | Out-Null

Write-Host "Compiling DragMC $firmwareVersion for Arduino Mega 2560..."
& $ArduinoCliPath compile `
    --fqbn "arduino:avr:mega:cpu=atmega2560" `
    --output-dir $buildDirectory `
    $sketchDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Arduino CLI compilation failed."
}

$imagePath = Join-Path $buildDirectory "dragMC.ino.hex"
if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
    throw "The application-only dragMC.ino.hex image was not generated."
}
$bootloaderImage = Join-Path $buildDirectory "dragMC.ino.with_bootloader.hex"
if (Test-Path -LiteralPath $bootloaderImage) {
    Write-Verbose "Ignoring with-bootloader image: $bootloaderImage"
}

$imageName = "dragMC.ino.hex"
$stagedImage = Join-Path $packageStage $imageName
Copy-Item -LiteralPath $imagePath -Destination $stagedImage
$imageItem = Get-Item -LiteralPath $stagedImage
$sha256 = (Get-FileHash -LiteralPath $stagedImage -Algorithm SHA256).Hash.ToUpperInvariant()
$manifest = [ordered]@{
    formatVersion = 1
    product = "DRAG_MC"
    firmwareVersion = $firmwareVersion
    boardProfile = "ARDUINO_MEGA_2560"
    boardDisplayName = "Arduino Mega 2560"
    mcu = "atmega2560"
    uploaderBackend = "avrdude"
    arduinoFqbn = "arduino:avr:mega:cpu=atmega2560"
    arduinoCoreVersion = $arduinoCoreVersion
    uploadProtocol = "wiring"
    uploadBaud = 115200
    imageFile = $imageName
    imageSizeBytes = $imageItem.Length
    sha256 = $sha256
}
$manifest | ConvertTo-Json | Set-Content `
    -LiteralPath (Join-Path $packageStage "manifest.json") `
    -Encoding UTF8

Get-ChildItem -LiteralPath $distDirectory -Filter "DragMC-mega-*.dragfw" -File |
    Remove-Item -Force
$packagePath = Join-Path $distDirectory "DragMC-mega-$firmwareVersion.dragfw"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageStage, $packagePath)

Write-Host "Created $packagePath"
Write-Host "Image bytes: $($imageItem.Length)"
Write-Host "SHA-256: $sha256"
