[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',

    [switch]$BuildInstaller
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'W1sleyEdition.csproj'
$artifactsPath = Join-Path $projectRoot 'artifacts'
$publishPath = Join-Path $artifactsPath 'publish'
$portableZip = Join-Path $artifactsPath "W1SLEY-Edition-$Version-win-x64.zip"

if (Test-Path $artifactsPath) {
    Remove-Item -LiteralPath $artifactsPath -Recurse -Force
}
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --output $publishPath
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish başarısız oldu.' }

Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $portableZip -CompressionLevel Optimal

if ($BuildInstaller) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $defaultIscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path $defaultIscc) { $iscc = $defaultIscc }
    }
    if (-not $iscc) {
        $userIscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
        if (Test-Path $userIscc) { $iscc = $userIscc }
    }
    if (-not $iscc) { throw 'Inno Setup 6 bulunamadı. ISCC.exe kurun veya -BuildInstaller kullanmayın.' }
    & $iscc "/DMyAppVersion=$Version" (Join-Path $projectRoot 'installer\W1SLEYEdition.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup derlemesi başarısız oldu.' }
}

$releaseFiles = @($portableZip | Get-Item)
$installerPath = Join-Path $artifactsPath 'installer'
if (Test-Path $installerPath) {
    $releaseFiles += Get-ChildItem -Path $installerPath -File -Filter '*.exe'
}
$checksums = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    "$($hash.Hash)  $($file.Name)"
}
$checksums | Set-Content -LiteralPath (Join-Path $artifactsPath 'SHA256SUMS.txt') -Encoding ascii

Write-Host "Release çıktıları: $artifactsPath" -ForegroundColor Green
