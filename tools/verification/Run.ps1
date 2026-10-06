param([switch]$Equipment, [switch]$Camera, [switch]$Bus, [switch]$StepPain)
$ErrorActionPreference = 'Stop'
$taskGame = 'E:\SteamLibrary\steamapps\common\Apocalypter'
$taskRepo = (Resolve-Path "$PSScriptRoot\..\..").Path
$taskVersion = if ($StepPain) { '1.4.0' } elseif ($Bus) { 'bus-probe' } elseif ($Camera) { '1.3.0' } elseif ($Equipment) { '1.2.0' } else { '1.1.0' }
$taskRuntime = "V:\Repos\Apocalypter\_staging\apocaplayer-$taskVersion-native\runtime"
$taskOutput = "V:\Repos\Apocalypter\_staging\apocaplayer-$taskVersion-native\results"
New-Item -ItemType Directory -Path "$taskRuntime\BepInEx\plugins\Apocaplayer", "$taskRuntime\BepInEx\config", $taskOutput -Force | Out-Null
foreach ($file in @('Apocalypter.exe','UnityPlayer.dll','winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath "$taskGame\$file" -Destination $taskRuntime -Force }
Copy-Item -LiteralPath "$taskGame\BepInEx\core" -Destination "$taskRuntime\BepInEx" -Recurse -Force
foreach ($dir in @('Apocalypter_Data','MonoBleedingEdge')) { if (!(Test-Path -LiteralPath "$taskRuntime\$dir")) { New-Item -ItemType Junction -Path "$taskRuntime\$dir" -Target "$taskGame\$dir" | Out-Null } }
Copy-Item -LiteralPath "$taskRepo\bin\Release\Apocaplayer.dll" -Destination "$taskRuntime\BepInEx\plugins\Apocaplayer" -Force
Copy-Item -LiteralPath "$taskRepo\Models" -Destination "$taskRuntime\BepInEx\plugins\Apocaplayer" -Recurse -Force
if ($StepPain) { Copy-Item -LiteralPath "$taskRepo\Sounds" -Destination "$taskRuntime\BepInEx\plugins\Apocaplayer" -Recurse -Force }
@'
[General]
IgnitionKey = R
[Debug]
VerboseLog = true
'@ | Set-Content -LiteralPath "$taskRuntime\BepInEx\config\com.denis.apocalypter.apocaplayer.cfg"
@'
[Logging.Console]
Enabled = false
[Logging.Disk]
Enabled = true
LogLevels = All
'@ | Set-Content -LiteralPath "$taskRuntime\BepInEx\config\BepInEx.cfg"
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\ApocaplayerNativeVerifier.dll" -Destination "$taskRuntime\BepInEx\plugins" -Force
$taskArgs = @('-screen-width','1200','-screen-height','680','-screen-fullscreen','0','-logFile',('"'+$taskRuntime+'\unity.log"'),('-apocaplayer-report="'+$taskOutput+'"'))
if ($Equipment) { $taskArgs += '-equipment-only' }
if ($Camera) { $taskArgs += '-camera-only' }
if ($Bus) { $taskArgs += '-bus-probe' }
if ($StepPain) { $taskArgs += '-step-pain' }
$taskProcess = Start-Process -FilePath "$taskRuntime\Apocalypter.exe" -WorkingDirectory $taskRuntime -WindowStyle Hidden -ArgumentList $taskArgs -PassThru
try {
    if (!$taskProcess.WaitForExit(60000)) { throw 'Native verification exceeded 60 seconds' }
    Get-Content -LiteralPath "$taskOutput\report.txt"
    if (Select-String -LiteralPath "$taskOutput\report.txt" -Pattern '^FAIL') { throw 'Native verification failed' }
} finally { if (!$taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id } }
