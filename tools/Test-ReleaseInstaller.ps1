param(
    [Alias('InstallerPath')][string]$SetupPath,
    [Alias('InstallDir')][string]$Target
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$targetPath = [IO.Path]::GetFullPath($Target)
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Amos錄影與截圖.lnk'
$menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Amos錄影與截圖\Amos錄影與截圖.lnk'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B38929C2-823A-4AB2-9B5A-191483A8AD65}_is1'
foreach ($path in @($targetPath,$desktopLink,$key)) {
    if (Test-Path -LiteralPath $path) { throw "Requires clean Windows test machine: $path" }
}
function Read-TestShortcut([string]$Path) {
    $aliasPath = Join-Path $env:RUNNER_TEMP 'Amos-shortcut-inspection.lnk'
    Copy-Item -LiteralPath $Path -Destination $aliasPath -Force
    try { return (New-Object -ComObject WScript.Shell).CreateShortcut($aliasPath) }
    finally { Remove-Item -LiteralPath $aliasPath -Force }
}
function Install-TestSetup([string]$Path) {
    $process = Start-Process -FilePath $Path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/MERGETASKS=desktopicon',('/DIR="'+$targetPath+'"')) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" }
}
$oldSetup = Join-Path $env:RUNNER_TEMP 'Amos-previous-setup.exe'
$previousAvailable = $true
try {
    Invoke-WebRequest -Uri 'https://github.com/stu310101-arch/screen_recoder/releases/download/v1.3.1/AmosRecorder-Setup.exe' -OutFile $oldSetup
    if ((Get-FileHash -LiteralPath $oldSetup -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'ea87288ba27d7a50dbfba144e87d49e8a38766fceacf83f5e30938e2170527fb') { throw 'Previous installer checksum mismatch' }
} catch {
    if ($_.Exception.Response.StatusCode.value__ -eq 404) {
        $previousAvailable = $false
        Write-Output 'Historical download removed; checking clean installation.'
    } else { throw }
}
$uninstaller = Join-Path $targetPath 'unins000.exe'
$icon = Join-Path $targetPath 'icons\AmosRecorder-v1.3.2.ico'
try {
    $oldIcon = $null
    if ($previousAvailable) {
        Install-TestSetup $oldSetup
        $oldIcon = (Read-TestShortcut $desktopLink).IconLocation
        if ([string]::IsNullOrEmpty($oldIcon)) { throw 'Previous shortcut icon could not be inspected' }
    }
    Install-TestSetup (Resolve-Path -LiteralPath $SetupPath).Path
    $exe = Join-Path $targetPath 'AureoRecorder.exe'
    foreach ($path in @($exe,$desktopLink,$menuLink,$key,$uninstaller,$icon)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing upgraded item: $path" }
    }
    if ((Get-FileHash -LiteralPath $icon -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'fb49157a98e163199a713a1059fd9da9798a0d1230c0e9b25f51ce898fc53be5') { throw 'Installed ICO differs from approved artwork' }
    foreach ($path in @($desktopLink,$menuLink)) {
        $shortcut = Read-TestShortcut $path
        if ($shortcut.TargetPath -ne $exe) { throw 'Shortcut target differs from installed executable' }
        $iconPath = ($shortcut.IconLocation -replace ',\s*0$','').Trim('"')
        if ($iconPath -ne $icon) { throw "Shortcut icon mismatch: actual='$iconPath'; expected='$icon'" }
        if ($oldIcon -and $shortcut.IconLocation -eq $oldIcon) { throw 'Upgrade reused old icon cache key' }
    }
    $registration = Get-ItemProperty -LiteralPath $key
    if ($registration.DisplayVersion -ne '1.3.2') { throw 'Installer version mismatch' }
    if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -notlike '1.3.2*') { throw 'Executable version mismatch' }
    Write-Output ('Previous version installed in test: ' + $previousAvailable)
    Write-Output 'PASS: upgrade from 1.3.1; correct new desktop and Start menu ICO paths; artwork SHA-256; version'
    $preview = Join-Path $env:RUNNER_TEMP 'Amos-recorder-preview.png'
    $app = Start-Process -FilePath $exe -ArgumentList @('--preview-export',('"'+$preview+'"')) -Wait -PassThru -WindowStyle Hidden
    if ($app.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preview)) { throw 'Installed WPF GUI failed' }
} finally {
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw 'Uninstall failed' }
    }
}
foreach ($path in @($desktopLink,$menuLink,$key,$icon,(Join-Path $targetPath 'AureoRecorder.exe'))) {
    if (Test-Path -LiteralPath $path) { throw "Uninstall left item: $path" }
}
Write-Output 'PASS: uninstall removes program, versioned icon, shortcuts and registration'
