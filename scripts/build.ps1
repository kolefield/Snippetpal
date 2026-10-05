param([switch]$Installer, [switch]$Test, [switch]$TestClipboard, [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$sdkList = & $Dotnet --list-sdks 2>$null
if (!$sdkList -or !($sdkList -match '^10\.')) {
    $local = Join-Path $env:LOCALAPPDATA 'SnippetpalBuildTools\dotnet\dotnet.exe'
    if (Test-Path $local) { $Dotnet = $local; $env:DOTNET_ROOT = Split-Path $local }
    else { throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 or pass -Dotnet with its full path.' }
}
Push-Location $root
try {
    $project = Join-Path $root 'src\Snippetpal\Snippetpal.csproj'
    $projectXml = [xml](Get-Content $project -Raw)
    $version = [string]$projectXml.Project.PropertyGroup.Version
    & $Dotnet build $project -c Release
    if ($LASTEXITCODE) { throw 'Build failed' }
    if ($Test) {
        $testRoot = Join-Path $root 'artifacts\tests'
        New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
        $testArgs = @('--self-test', '--data-dir', $testRoot)
        if (!$TestClipboard) { $testArgs += '--no-clipboard' }
        & $Dotnet (Join-Path $root 'src\Snippetpal\bin\Release\net10.0-windows\Snippetpal.dll') @testArgs
        if ($LASTEXITCODE) { Get-Content (Join-Path $testRoot 'self-test-results.txt'); throw 'Verification failed' }
        Get-Content (Join-Path $testRoot 'self-test-results.txt')
    }
    $publish = Join-Path $root "artifacts\build\$version\win-x64"
    & $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $publish
    if ($LASTEXITCODE) { throw 'Publish failed' }
    $notices = Join-Path $publish 'notices'
    New-Item -ItemType Directory -Force -Path $notices | Out-Null
    $assets = Get-Content (Join-Path $root 'src\Snippetpal\obj\project.assets.json') -Raw | ConvertFrom-Json
    $packageRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $runtime = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object { $_.name -eq $package } | Select-Object -First 1
        if (!$runtime) { throw "Runtime package metadata missing for $package" }
        $runtimeVersion = $runtime.version.Trim('[',']').Split(',')[0].Trim()
        $packageDirectory = Join-Path $packageRoot "$package/$runtimeVersion"
        foreach ($name in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            $source = Join-Path $packageDirectory $name
            if (Test-Path $source) { Copy-Item $source (Join-Path $notices ($package + '-' + $name + $(if ($name -eq 'LICENSE') { '.TXT' } else { '' }))) -Force }
        }
    }
    Copy-Item (Join-Path $root 'README.md') (Join-Path $publish 'README.md') -Force
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $publish 'THIRD-PARTY-NOTICES.md') -Force
    $release = Join-Path $root 'artifacts\release'
    New-Item -ItemType Directory -Force -Path $release | Out-Null
    $releaseFiles = @('Snippetpal.exe', 'Snippetpal-win-x64.zip', 'Third-Party-Notices.zip')
    if ($Installer) {
        $makensis = & (Join-Path $PSScriptRoot 'get-nsis.ps1')
        Copy-Item (Join-Path (Split-Path $makensis) 'COPYING') (Join-Path $notices 'NSIS-COPYING.txt') -Force
        $setup = Join-Path $release 'Snippetpal-Setup-x64.exe'
        & $makensis /V2 "/DSourceRoot=$root" "/DPublishDir=$publish" "/DAppVersion=$version" "/DOutputFile=$setup" (Join-Path $root 'installer\Snippetpal.nsi')
        if ($LASTEXITCODE) { throw 'Installer build failed' }
        $releaseFiles += 'Snippetpal-Setup-x64.exe'
    }
    Copy-Item (Join-Path $publish 'Snippetpal.exe') (Join-Path $release 'Snippetpal.exe') -Force
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $release 'Snippetpal-win-x64.zip') -Force
    Compress-Archive -Path $notices,(Join-Path $publish 'THIRD-PARTY-NOTICES.md') -DestinationPath (Join-Path $release 'Third-Party-Notices.zip') -Force
    $checksums = foreach ($name in ($releaseFiles | Sort-Object)) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
    Get-ChildItem $release -File | Select-Object Name,Length
} finally { Pop-Location }
