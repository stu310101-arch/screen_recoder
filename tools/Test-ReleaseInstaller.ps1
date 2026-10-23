param([string]$SetupPath,[string]$Target)
$ErrorActionPreference = 'Stop'
$targetPath = [IO.Path]::GetFullPath($Target)
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Amos錄影與截圖.lnk'
$menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Amos錄影與截圖\Amos錄影與截圖.lnk'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B38929C2-823A-4AB2-9B5A-191483A8AD65}_is1'
foreach ($path in @($targetPath,$desktopLink,$key)) {
    if (Test-Path -LiteralPath $path) { throw "Requires clean test machine: $path" }
}
$uninstaller = Join-Path $targetPath 'unins000.exe'
try {
    $process = Start-Process -FilePath (Resolve-Path -LiteralPath $SetupPath).Path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$targetPath+'"')) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Setup failed: $($process.ExitCode)" }
    $exe = Join-Path $targetPath 'AureoRecorder.exe'
    foreach ($path in @($exe,$desktopLink,$menuLink,$key,$uninstaller)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing installed item: $path" }
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in @($desktopLink,$menuLink)) {
        $shortcut = $shell.CreateShortcut($path)
        if ($shortcut.TargetPath -ne $exe) { throw 'Wrong shortcut target' }
        if ($shortcut.IconLocation -notlike ($exe + '*')) { throw 'Wrong shortcut icon' }
    }
    $registration = Get-ItemProperty -LiteralPath $key
    if ($registration.DisplayName -notlike 'Amos錄影與截圖*' -or $registration.DisplayVersion -ne '1.3.1') { throw 'Wrong uninstall display metadata' }
    Write-Output 'PASS: setup, version, desktop shortcut, Start menu shortcut and icon target'
    $preview = Join-Path $env:RUNNER_TEMP 'recorder-preview.png'
    $appProcess = Start-Process -FilePath $exe -ArgumentList @('--preview-export',('"'+$preview+'"')) -Wait -PassThru -WindowStyle Hidden
    if ($appProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preview)) { throw 'Installed WPF GUI check failed' }
    Write-Output 'PASS: installed WPF GUI renders with embedded icon'
} finally {
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw 'Uninstall failed' }
    }
}
foreach ($path in @($desktopLink,$menuLink,$key,(Join-Path $targetPath 'AureoRecorder.exe'))) {
    if (Test-Path -LiteralPath $path) { throw "Uninstall left installed item: $path" }
}
Write-Output 'PASS: uninstall removes program, shortcuts and registration'
