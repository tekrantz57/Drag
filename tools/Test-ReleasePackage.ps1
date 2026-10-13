[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Archive,
    [Parameter(Mandatory)] [string]$Checksums,
    [switch]$LaunchDemo
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$name = [IO.Path]::GetFileName($archivePath)
$checksumLine = @(Get-Content -LiteralPath $Checksums | Where-Object {
    ($_ -split '\s+', 2)[1] -eq $name
})
if ($checksumLine.Count -ne 1) { throw "Missing or ambiguous checksum for $name" }
$expected = ($checksumLine[0] -split '\s+', 2)[0]
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expected) {
    throw "Checksum mismatch: $name"
}
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $root = [IO.Path]::GetFileNameWithoutExtension($name)
    foreach ($required in @('dragWin.exe', 'hostfxr.dll', 'Start-Demo.cmd', 'START_HERE.txt',
                            'TRY_DEMO.md', 'BETA_FEEDBACK.md', 'COMMISSIONING_CHECKLIST.md',
                            'LICENSE', 'THIRD_PARTY_NOTICES.md', 'BUILD_INFO.json')) {
        if (!$zip.GetEntry("$root/$required")) { throw "Missing $required" }
    }
    $privateFiles = @($zip.Entries | Where-Object {
        [IO.Path]::GetExtension($_.FullName) -in @('.db', '.sqlite', '.sqlite3', '.log', '.onnx', '.pdb') -or
        $_.Name -in @('settings.json', 'controller-port.txt')
    })
    if ($privateFiles.Count) { throw 'Archive contains private or development files.' }
    $reader = [IO.StreamReader]::new($zip.GetEntry("$root/BUILD_INFO.json").Open())
    try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    $stream = $zip.GetEntry("$root/dragWin.exe").Open()
    $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $expectedMachine = if ($metadata.runtime -eq 'win-x64') { 0x8664 }
                       elseif ($metadata.runtime -eq 'win-arm64') { 0xaa64 }
                       else { throw "Unexpected runtime: $($metadata.runtime)" }
    if ($machine -ne $expectedMachine) { throw 'Executable architecture does not match metadata.' }
    Write-Output "$name verified: checksum, archive layout, runtime, licenses, and no private files."
}
finally { $zip.Dispose() }

if ($LaunchDemo) {
    if ($metadata.runtime -ne 'win-x64' -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw 'This launch check requires an x64 package on an x64 Windows host.'
    }
    $destination = Join-Path ([IO.Path]::GetDirectoryName($archivePath)) "Smoke Test With Spaces $([Guid]::NewGuid().ToString('N'))"
    Expand-Archive -LiteralPath $archivePath -DestinationPath $destination
    $folder = Join-Path $destination $root
    $executable = Join-Path $folder 'dragWin.exe'
    $launcher = Join-Path $folder 'Start-Demo.cmd'
    $process = $null
    try {
        $command = Start-Process -FilePath $env:ComSpec -ArgumentList "/d /c `"`"$launcher`"`"" -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            $process = Get-Process -Name dragWin -ErrorAction SilentlyContinue | Where-Object {
                $_.Path -eq $executable
            } | Select-Object -First 1
            if (!$process) { Start-Sleep -Milliseconds 200 }
        } while (!$process -and [DateTime]::UtcNow -lt $deadline)
        if (!$process) { throw 'Demo launcher did not start the extracted application.' }
        if (!$process.WaitForInputIdle(20000)) { throw 'Demo did not reach an idle UI.' }
        Start-Sleep -Seconds 3
        $process.Refresh()
        if ($process.HasExited) { throw 'Demo exited during startup.' }
        Write-Output 'Downloaded demo launcher reached an idle UI from a folder with spaces.'
    }
    finally {
        if ($process -and !$process.HasExited) {
            $null = $process.CloseMainWindow()
            if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
        }
    }
}
