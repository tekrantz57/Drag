[CmdletBinding()]
param(
    [ValidateSet("Mega", "Due")]
    [string]$Board = "Mega",
    [string]$ArduinoCliPath = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$isDue = $Board -eq "Due"
$boardSlug = $Board.ToLowerInvariant()
$architecture = if ($isDue) { "sam" } else { "avr" }
$fqbn = if ($isDue) { "arduino:sam:arduino_due_x_dbg" } else { "arduino:avr:mega:cpu=atmega2560" }
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sketchDirectory = Join-Path $repoRoot "dragMC"
$versionHeader = Join-Path $sketchDirectory "FirmwareVersion.h"
$distDirectory = Join-Path $sketchDirectory "dist"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\controller-firmware"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$buildDirectory = Join-Path $OutputRoot "$boardSlug-build"
$packageStage = Join-Path $OutputRoot "$boardSlug-package-stage"

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
$versionMacro = if ($isDue) { "DRAGMC_DUE_FIRMWARE_VERSION" } else { "DRAGMC_FIRMWARE_VERSION" }
$versionMatch = [regex]::Match(
    $versionText,
    ('#define\s+' + $versionMacro + '\s+"([^"]+)"'))
if (-not $versionMatch.Success) {
    throw "Could not read $versionMacro from $versionHeader"
}
$firmwareVersion = $versionMatch.Groups[1].Value

$coreLines = & $ArduinoCliPath core list
if ($LASTEXITCODE -ne 0) {
    throw "Arduino CLI could not list installed cores."
}
$avrLine = $coreLines | Where-Object { $_ -match ('^arduino:' + $architecture + '\s+') } | Select-Object -First 1
if (-not $avrLine) {
    throw "The arduino:$architecture core is not installed."
}
$arduinoCoreVersion = ($avrLine -split '\s+')[1]

foreach ($directory in @($buildDirectory, $packageStage)) {
    $resolved = [System.IO.Path]::GetFullPath($directory)
    if ([System.IO.Path]::GetDirectoryName($resolved) -ne $OutputRoot) { throw "Unsafe build path: $resolved" }
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory | Out-Null
}
New-Item -ItemType Directory -Force -Path $distDirectory | Out-Null

Write-Host "Compiling DragMC $firmwareVersion for $Board..."
& $ArduinoCliPath compile `
    --fqbn $fqbn `
    --output-dir $buildDirectory `
    $sketchDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Arduino CLI compilation failed."
}

$imageName = if ($isDue) { "dragMC.ino.bin" } else { "dragMC.ino.hex" }
$imagePath = Join-Path $buildDirectory $imageName
if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
    throw "The application-only $imageName image was not generated."
}
$bootloaderImage = Join-Path $buildDirectory "dragMC.ino.with_bootloader.hex"
if (Test-Path -LiteralPath $bootloaderImage) {
    Write-Verbose "Ignoring with-bootloader image: $bootloaderImage"
}

$stagedImage = Join-Path $packageStage $imageName
Copy-Item -LiteralPath $imagePath -Destination $stagedImage
$imageItem = Get-Item -LiteralPath $stagedImage
$sha256 = (Get-FileHash -LiteralPath $stagedImage -Algorithm SHA256).Hash.ToUpperInvariant()
$manifest = [ordered]@{
    formatVersion = 1
    product = $(if ($isDue) { "DRAG_MC_DUE" } else { "DRAG_MC" })
    firmwareVersion = $firmwareVersion
    boardProfile = $(if ($isDue) { "ARDUINO_DUE_PROGRAMMING" } else { "ARDUINO_MEGA_2560" })
    boardDisplayName = $(if ($isDue) { "Arduino Due (Programming Port)" } else { "Arduino Mega 2560" })
    mcu = $(if ($isDue) { "sam3x8e" } else { "atmega2560" })
    uploaderBackend = $(if ($isDue) { "bossac" } else { "avrdude" })
    arduinoFqbn = $fqbn
    arduinoCoreVersion = $arduinoCoreVersion
    uploadProtocol = $(if ($isDue) { "sam-ba" } else { "wiring" })
    uploadBaud = $(if ($isDue) { 1200 } else { 115200 })
    imageFile = $imageName
    imageSizeBytes = $imageItem.Length
    sha256 = $sha256
}
$manifest | ConvertTo-Json | Set-Content `
    -LiteralPath (Join-Path $packageStage "manifest.json") `
    -Encoding UTF8

Get-ChildItem -LiteralPath $distDirectory -Filter "DragMC-$boardSlug-*.dragfw" -File |
    Remove-Item -Force
$packagePath = Join-Path $distDirectory "DragMC-$boardSlug-$firmwareVersion.dragfw"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageStage, $packagePath)

Write-Host "Created $packagePath"
Write-Host "Image bytes: $($imageItem.Length)"
Write-Host "SHA-256: $sha256"
