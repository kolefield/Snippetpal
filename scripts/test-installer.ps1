# This changes the runner's per-user installation registry and Start menu.
# Never run it against a personal Windows account.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installer lifecycle tests are restricted to disposable GitHub-hosted runners.'
}
$root = Split-Path $PSScriptRoot -Parent
$setup = Join-Path $root 'artifacts\release\Snippetpal-Setup-x64.exe'
$installDirectory = Join-Path $env:RUNNER_TEMP 'Snippetpal installer test'
$app = Join-Path $installDirectory 'Snippetpal.exe'
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Snippetpal'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'Snippetpal'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Snippetpal.lnk'
if ((Test-Path -LiteralPath $installDirectory) -or (Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $dataDirectory)) {
    throw 'Expected a clean disposable runner; refusing to overwrite an existing installation or library.'
}
New-Item -ItemType Directory -Path $dataDirectory | Out-Null
$sentinel = Join-Path $dataDirectory 'preserve-library.txt'
'Keep this library' | Set-Content -LiteralPath $sentinel
function Check($condition, [string]$message) {
    if (!$condition) { throw $message }
    Write-Host "PASS $message"
}
function Install {
    # NSIS requires /D to be last, with its path unquoted even when it contains spaces.
    $process = Start-Process -FilePath $setup -ArgumentList "/S /D=$installDirectory" -WindowStyle Hidden -PassThru -Wait
    return $process.ExitCode
}
Check ((Install) -eq 0) 'Silent per-user installation'
Check ((Test-Path -LiteralPath $app) -and (Test-Path -LiteralPath $shortcut)) 'Executable and Start menu shortcut installed'
$installed = Get-ItemProperty -LiteralPath $registry
Check ($installed.InstallLocation -eq $installDirectory) 'Windows uninstall registration points to the selected folder'
Check ((Get-FileHash -LiteralPath $app).Hash -eq (Get-FileHash -LiteralPath (Join-Path $root 'artifacts\release\Snippetpal.exe')).Hash) 'Installer contains the published executable'
Check ((Test-Path -LiteralPath (Join-Path $installDirectory 'notices\NSIS-COPYING.txt')) -and (Test-Path -LiteralPath (Join-Path $installDirectory 'notices\microsoft.netcore.app.runtime.win-x64-LICENSE.TXT'))) 'Runtime and installer notices installed'
$qaDirectory = Join-Path $env:RUNNER_TEMP 'snippetpal-installed-app-tests'
$process = Start-Process -FilePath $app -ArgumentList "--self-test --no-clipboard --data-dir `"$qaDirectory`"" -WindowStyle Hidden -PassThru -Wait
Get-Content (Join-Path $qaDirectory 'self-test-results.txt')
Check ($process.ExitCode -eq 0) 'Installed app passes its isolated self-tests'
Check ((Install) -eq 0 -and (Test-Path -LiteralPath $sentinel)) 'Reinstall preserves the user library'
$locked = [IO.File]::Open($app, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Check ((Install) -ne 0) 'Locked application is rejected without forced termination' }
finally { $locked.Dispose() }
Set-ItemProperty -LiteralPath $registry -Name DisplayVersion -Value '99.0.0'
try { Check ((Install) -ne 0) 'Downgrading a newer installed version is rejected' }
finally { Set-ItemProperty -LiteralPath $registry -Name DisplayVersion -Value $installed.DisplayVersion }
# A previous-version registration exercises the upgrade path without requiring an older public release.
Set-ItemProperty -LiteralPath $registry -Name DisplayVersion -Value '0.0.1'
Check ((Install) -eq 0 -and (Get-ItemProperty -LiteralPath $registry).DisplayVersion -eq $installed.DisplayVersion) 'Upgrade updates the existing installation registration'
$uninstaller = Join-Path $installDirectory 'Uninstall.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -PassThru -Wait
Check ($process.ExitCode -eq 0) 'Silent uninstall completed'
Check (!(Test-Path -LiteralPath $app) -and !(Test-Path -LiteralPath $registry) -and !(Test-Path -LiteralPath $shortcut)) 'Uninstall removes app, registration, and shortcut'
Check ((Get-Content -LiteralPath $sentinel -Raw).Trim() -eq 'Keep this library') 'Uninstall preserves the user library'
