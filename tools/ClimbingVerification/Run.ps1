$ErrorActionPreference = 'Stop'
$taskGame = 'E:\SteamLibrary\steamapps\common\Apocalypter'
$taskPlayer = (Resolve-Path "$PSScriptRoot\..\..").Path
$taskRoot = 'V:\Repos\Apocalypter\_staging\apocaplayer-climbing-2.3.0'
$taskRuntime = "$taskRoot\runtime"
$taskOutput = "$taskRoot\results"
New-Item -ItemType Directory -Path "$taskRuntime\BepInEx\plugins\Apocaplayer", "$taskRuntime\BepInEx\config", $taskOutput -Force | Out-Null
if (Test-Path -LiteralPath "$taskOutput\report.txt") { Remove-Item -LiteralPath "$taskOutput\report.txt" }
foreach ($file in @('Apocalypter.exe','UnityPlayer.dll','winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath "$taskGame\$file" -Destination $taskRuntime -Force }
Copy-Item -LiteralPath "$taskGame\BepInEx\core" -Destination "$taskRuntime\BepInEx" -Recurse -Force
foreach ($dir in @('Apocalypter_Data','MonoBleedingEdge')) { if (!(Test-Path -LiteralPath "$taskRuntime\$dir")) { New-Item -ItemType Junction -Path "$taskRuntime\$dir" -Target "$taskGame\$dir" | Out-Null } }
Copy-Item -LiteralPath "$taskPlayer\bin\Release\Apocaplayer.dll" -Destination "$taskRuntime\BepInEx\plugins\Apocaplayer" -Force
Copy-Item -LiteralPath "$taskPlayer\Models", "$taskPlayer\Sounds" -Destination "$taskRuntime\BepInEx\plugins\Apocaplayer" -Recurse -Force
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\ApocaplayerClimbingVerifier.dll" -Destination "$taskRuntime\BepInEx\plugins" -Force
@'
[Logging.Console]
Enabled = false
[Logging.Disk]
Enabled = true
LogLevels = All
'@ | Set-Content -LiteralPath "$taskRuntime\BepInEx\config\BepInEx.cfg"
@'
[General]
IgnitionKey = R
[climbing]
AnimationSpeed = 1.5
[Debug]
VerboseLog = true
'@ | Set-Content -LiteralPath "$taskRuntime\BepInEx\config\com.denis.apocalypter.apocaplayer.cfg"
$taskArgs = @('-screen-width','640','-screen-height','360','-screen-fullscreen','0','-logFile',('"'+$taskRuntime+'\unity.log"'),('-apocaplayer-climbing-report="'+$taskOutput+'"'))
$taskProcess = Start-Process -FilePath "$taskRuntime\Apocalypter.exe" -WorkingDirectory $taskRuntime -WindowStyle Hidden -ArgumentList $taskArgs -PassThru
try {
    if (!$taskProcess.WaitForExit(55000)) { throw 'Native verification exceeded 55 seconds' }
    Get-Content -LiteralPath "$taskOutput\report.txt"
    if (Select-String -LiteralPath "$taskOutput\report.txt" -Pattern '^FAIL') { throw 'Native verification failed' }
    if ($taskProcess.ExitCode -ne 0) { throw "Native process exited with $($taskProcess.ExitCode)" }
    Copy-Item -LiteralPath "$taskOutput\report.txt" -Destination "$PSScriptRoot\latest-report.txt" -Force
} finally { if (!$taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id } }
