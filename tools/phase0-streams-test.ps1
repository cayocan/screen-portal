# Teste de N telas simultâneas de ponta a ponta (sem rebuild):
# reinicia o app no Quest com --ei streams N, sobe o host, coleta SPSTAT + fps do app.
#   powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Streams 4 [-Seconds 40] [-Bitrate 20000]
#   Fase 2A — taxas mistas e repetições: -Mix "60x1,15x3" -Runs 3 -Seconds 60 -Label mvp
#     (tela 0 = o primeiro grupo; critério por tela: dec fps mín >= esperado - max(1, esperado/30),
#      latência média < 50 ms, 0 descartes do decoder — spec 2A §3)
#   Parâmetros extras do app (teste de texto): -AppExtras "--ei widthcm 200 --ei distcm 120 --ei sharpen 1"
param([int]$Streams = 1, [int]$Seconds = 40, [int]$Bitrate = 20000, [int]$Fps = 60, [string]$Mix = "", [int]$Runs = 1,
      [string]$Label = "", [string]$AppExtras = "", [string]$QuestIp = "192.168.0.171", [string]$Serial = "",
      [switch]$KeepRunning)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "quest.ps1") -QuestIp $QuestIp   # USB se no cabo, senão Wi-Fi
$adb = $QuestAdb
if (-not $Serial) { $Serial = Get-QuestSerial }
Write-Host "Quest: $Serial"
$exe = Join-Path $root "host\Host.App\bin\Release\net10.0-windows10.0.22621.0\ScreenPortalHost.exe"
$activity = "com.screenportal.app/com.unity3d.player.UnityPlayerGameActivity"
$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $outDir | Out-Null

# fps esperado de cada tela, na ordem dos stream ids
$expected = @()
if ($Mix) {
    foreach ($group in $Mix -split ',') {
        $parts = $group.Trim() -split 'x'
        for ($i = 0; $i -lt [int]$parts[1]; $i++) { $expected += [int]$parts[0] }
    }
    $Streams = $expected.Count
} else {
    for ($i = 0; $i -lt $Streams; $i++) { $expected += $Fps }
}
if (-not $Label) { $Label = if ($Mix) { "mix-" + ($Mix -replace '[ ,]', '_') } else { "${Streams}streams" } }
$hostSeconds = if ($KeepRunning) { 0 } else { $Seconds + 5 }
$hostArgs = $(if ($Mix) { "stream --target $QuestIp --mix `"$Mix`" --bitrate $Bitrate" } else { "stream --target $QuestIp --streams $Streams --bitrate $Bitrate --fps $Fps" }) +
            $(if ($hostSeconds -gt 0) { " --seconds $hostSeconds" } else { "" })

$passes = 0
for ($run = 1; $run -le $Runs; $run++) {
    $runLabel = if ($Runs -gt 1) { "$Label-r$run" } else { $Label }

    & $adb -s $Serial shell am force-stop com.screenportal.app
    & $adb -s $Serial shell am broadcast -a com.oculus.vrpowermanager.prox_close | Out-Null
    & $adb -s $Serial logcat -c
    & $adb -s $Serial shell "am start -n $activity --ei streams $Streams $AppExtras" | Out-Null
    Start-Sleep -Seconds 8   # layers + surfaces + sessões nativas

    $hostLog = Join-Path $outDir "host-$runLabel.log"
    $hostProc = Start-Process $exe -ArgumentList $hostArgs -NoNewWindow -PassThru -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"
    if ($KeepRunning) { Write-Host "Host rodando (PID $($hostProc.Id)). Pare com: Stop-Process $($hostProc.Id)"; exit 0 }

    Start-Sleep -Seconds ($Seconds + 6)

    # Estatísticas: descarta os 5 primeiros segundos (aquecimento do decoder).
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "phase0-collect.ps1") -Label $runLabel -FromBuffer -Serial $Serial | Out-Null
    $csv = Join-Path $root "docs\measurements\fase0-$runLabel.csv"
    $rows = Import-Csv $csv | Where-Object { $_.lat_avg_ms -ne "-1.0" }
    Write-Host "=== $runLabel ($Streams tela(s), $(if ($Mix) { $Mix } else { "$Fps fps" }), $Bitrate kbps)"
    $runPass = $true
    $seen = 0
    foreach ($g in $rows | Group-Object stream | Sort-Object { [int]$_.Name }) {
        $steady = $g.Group | Select-Object -Skip 5
        if (-not $steady) { continue }
        $seen++
        $lat = ($steady | ForEach-Object { [double]$_.lat_avg_ms } | Measure-Object -Average).Average
        $max = ($steady | ForEach-Object { [double]$_.lat_max_ms } | Measure-Object -Maximum).Maximum
        $dec = $steady | ForEach-Object { [double]$_.dec_fps } | Measure-Object -Average -Minimum
        $dd = ($steady | ForEach-Object { [int]$_.drop_dec } | Measure-Object -Sum).Sum
        $dn = ($steady | ForEach-Object { [int]$_.drop_net } | Measure-Object -Sum).Sum
        $want = $expected[[int]$g.Name]
        $minFps = $want - [Math]::Max(1, $want / 30)
        $ok = $dec.Minimum -ge $minFps -and $lat -lt 50 -and $dd -eq 0
        if (-not $ok) { $runPass = $false }
        "  tela {0} ({1} fps): lat {2:F1} ms (máx {3:F1}) | dec {4:F1} fps (mín {5:F1}, alvo >= {6}) | perdas rede {7}, decoder {8} | {9}" -f `
            $g.Name, $want, $lat, $max, $dec.Average, $dec.Minimum, $minFps, $dn, $dd, $(if ($ok) { "ok" } else { "FALHOU" })
    }
    if ($seen -lt $Streams) { $runPass = $false; "  faltaram telas: $seen de $Streams com dados" }

    # FPS do app (VrApi imprime 1x/s): "FPS=72/72"
    $appFps = & $adb -s $Serial logcat -d -s VrApi:I | Select-String "FPS=(\d+)/(\d+)" | ForEach-Object { [int]$_.Matches[0].Groups[1].Value } | Select-Object -Skip 5
    if ($appFps) { $m = $appFps | Measure-Object -Average -Minimum; "  app: {0:F1} fps médio (mín {1})" -f $m.Average, $m.Minimum }
    "  critério: $(if ($runPass) { 'PASSOU' } else { 'FALHOU' })"
    if ($runPass) { $passes++ }
}
if ($Runs -gt 1) { "=== $Label`: $passes de $Runs repetições passaram" }
