# Teste de N telas simultâneas de ponta a ponta (sem rebuild):
# reinicia o app no Quest com --ei streams N, sobe o host com --streams N, coleta SPSTAT + fps do app.
#   powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Streams 4 [-Seconds 40] [-Bitrate 20000]
#   Parâmetros extras do app (teste de texto): -AppExtras "--ei widthcm 200 --ei distcm 120 --ei sharpen 1"
param([int]$Streams = 1, [int]$Seconds = 40, [int]$Bitrate = 20000, [int]$Fps = 60, [string]$Label = "",
      [string]$AppExtras = "", [string]$QuestIp = "192.168.0.171", [string]$Serial = "2G0YC1ZG410B1H",
      [switch]$KeepRunning)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$adb = "D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$exe = Join-Path $root "host\Host.App\bin\Release\net10.0-windows10.0.22621.0\ScreenPortalHost.exe"
$activity = "com.screenportal.app/com.unity3d.player.UnityPlayerGameActivity"
if (-not $Label) { $Label = "${Streams}streams" }
$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $outDir | Out-Null

& $adb -s $Serial shell am force-stop com.screenportal.app
& $adb -s $Serial shell am broadcast -a com.oculus.vrpowermanager.prox_close | Out-Null
& $adb -s $Serial logcat -c
& $adb -s $Serial shell "am start -n $activity --ei streams $Streams $AppExtras" | Out-Null
Start-Sleep -Seconds 8   # layers + surfaces + sessões nativas

$hostLog = Join-Path $outDir "host-$Label.log"
$hostSeconds = if ($KeepRunning) { 0 } else { $Seconds + 5 }
$hostArgs = "stream --target $QuestIp --streams $Streams --bitrate $Bitrate --fps $Fps" + $(if ($hostSeconds -gt 0) { " --seconds $hostSeconds" } else { "" })
$hostProc = Start-Process $exe -ArgumentList $hostArgs -NoNewWindow -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"
if ($KeepRunning) { Write-Host "Host rodando (PID $($hostProc.Id)). Pare com: Stop-Process $($hostProc.Id)"; exit 0 }

Start-Sleep -Seconds ($Seconds + 6)

# Estatísticas: descarta os 5 primeiros segundos (aquecimento do decoder).
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "phase0-collect.ps1") -Label $Label -FromBuffer -Serial $Serial | Out-Null
$csv = Join-Path $root "docs\measurements\fase0-$Label.csv"
$rows = Import-Csv $csv | Where-Object { $_.lat_avg_ms -ne "-1.0" }
Write-Host "=== $Label ($Streams stream(s), $Bitrate kbps)"
foreach ($g in $rows | Group-Object stream) {
    $steady = $g.Group | Select-Object -Skip 5
    if (-not $steady) { continue }
    $lat = $steady | ForEach-Object { [double]$_.lat_avg_ms } | Sort-Object
    $max = ($steady | ForEach-Object { [double]$_.lat_max_ms } | Measure-Object -Maximum).Maximum
    $dec = $steady | ForEach-Object { [double]$_.dec_fps } | Measure-Object -Average -Minimum
    $dd = ($steady | ForEach-Object { [int]$_.drop_dec } | Measure-Object -Sum).Sum
    $dn = ($steady | ForEach-Object { [int]$_.drop_net } | Measure-Object -Sum).Sum
    "  tela {0}: lat {1:F1} ms (máx {2:F1}) | dec {3:F1} fps (mín {4:F1}) | perdas rede {5}, decoder {6}" -f `
        $g.Name, ($lat | Measure-Object -Average).Average, $max, $dec.Average, $dec.Minimum, $dn, $dd
}

# FPS do app (VrApi imprime 1x/s): "FPS=72/72"
$appFps = & $adb -s $Serial logcat -d -s VrApi:I | Select-String "FPS=(\d+)/(\d+)" | ForEach-Object { [int]$_.Matches[0].Groups[1].Value } | Select-Object -Skip 5
if ($appFps) { $m = $appFps | Measure-Object -Average -Minimum; "  app: {0:F1} fps médio (mín {1}) — alvo do display: {2}" -f $m.Average, $m.Minimum, ((& $adb -s $Serial logcat -d -s VrApi:I | Select-String "FPS=\d+/(\d+)" | Select-Object -Last 1).Matches[0].Groups[1].Value) }
"  host: " + ((Get-Content $hostLog -Tail 3 | Select-Object -First 1) -replace '^\[\S+\] ', '')
