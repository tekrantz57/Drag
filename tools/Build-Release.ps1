[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Runtimes = @('win-x64', 'win-arm64')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'dragWin\dragWin.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.DisplayVersion | Where-Object { $_ }
if ($version -notmatch '^v\d+\.\d+\.\d+-beta\.\d+$') { throw "Unexpected release version: $version" }
$firmwareHeader = Get-Content -LiteralPath (Join-Path $root 'dragMC\FirmwareVersion.h') -Raw
$firmwareVersion = [regex]::Match($firmwareHeader, 'DRAGMC_FIRMWARE_VERSION\s+"([^"]+)"').Groups[1].Value
if (!$firmwareVersion) { throw 'Cannot read controller firmware version.' }

Push-Location $root
try {
    $changes = git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit all source changes before packaging a release.' }
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read source commit.' }
    $tagCommit = git rev-list -n 1 $version
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $commit) { throw "Tag $version must identify HEAD before packaging." }

    $output = Join-Path $root "artifacts\release\$version-$(Get-Date -Format yyyyMMdd-HHmmss)"
    if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
    New-Item -ItemType Directory -Path $output | Out-Null
    $archives = @()
    foreach ($runtime in ($Runtimes | Select-Object -Unique)) {
        $name = "DragWin-$version-$runtime"
        $folder = Join-Path $output $name
        dotnet publish $project -c Release -r $runtime --self-contained true `
            -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $folder
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime" }

        Copy-Item -LiteralPath (Join-Path $root 'tools\Start-Demo.cmd') -Destination $folder
        Copy-Item -LiteralPath (Join-Path $root 'docs\START_HERE.txt') -Destination $folder
        Copy-Item -LiteralPath (Join-Path $root 'docs\TRY_DEMO.md') -Destination $folder
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $folder
        Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $folder
        Copy-Item -LiteralPath (Join-Path $root "docs\RELEASE_$version.md") `
            -Destination (Join-Path $folder 'RELEASE_NOTES.md')

        # Preserve license files shipped by the exact restored NuGet packages.
        $assets = Get-Content -LiteralPath (Join-Path $root 'dragWin\obj\project.assets.json') -Raw | ConvertFrom-Json
        foreach ($library in $assets.libraries.PSObject.Properties) {
            if ($library.Value.type -ne 'package') { continue }
            foreach ($packageRoot in $assets.packageFolders.PSObject.Properties.Name) {
                $package = Join-Path $packageRoot $library.Value.path
                if (!(Test-Path -LiteralPath $package)) { continue }
                $licenses = Get-ChildItem -LiteralPath $package -File | Where-Object {
                    $_.Name -match '^(LICENSE|NOTICE|THIRD[-_]PARTY)' -and $_.Extension -in @('', '.txt', '.md')
                }
                if ($licenses) {
                    $licenseFolder = Join-Path $folder ('Licenses\' + ($library.Name -replace '/', '-'))
                    New-Item -ItemType Directory -Path $licenseFolder -Force | Out-Null
                    $licenses | Copy-Item -Destination $licenseFolder
                }
            }
        }

        $privateFiles = Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object {
            $_.Extension -in @('.db', '.sqlite', '.sqlite3', '.log', '.onnx', '.pdb') -or
            $_.Name -in @('settings.json', 'controller-port.txt')
        }
        if ($privateFiles) { throw "Unexpected private or development files in $folder" }
        if (!(Test-Path -LiteralPath (Join-Path $folder 'dragWin.exe')) -or
            !(Test-Path -LiteralPath (Join-Path $folder 'hostfxr.dll'))) {
            throw "Missing self-contained application files in $folder"
        }
        @{
            version = $version
            sourceCommit = $commit
            runtime = $runtime
            packagedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            controllerFirmware = $firmwareVersion
            physicalFourLaneValidation = 'pending'
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'BUILD_INFO.json') -Encoding UTF8

        $archive = Join-Path $output "$name.zip"
        Compress-Archive -LiteralPath $folder -DestinationPath $archive -CompressionLevel Optimal
        $archives += $archive
    }
    $archives | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf)
    } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
    Write-Output "Release packages: $output"
}
finally { Pop-Location }
