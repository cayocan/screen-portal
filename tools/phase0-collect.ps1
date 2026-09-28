# Coleta as linhas SPSTAT/SPKB do logcat do Quest e grava CSV em docs/measurements/.
#   powershell -ExecutionPolicy Bypass -File tools/phase0-collect.ps1 -Label "1stream-2560x1080" [-Seconds 60]
param([Parameter(Mandatory)][string]$Label, [int]$Seconds = 60,
      [string]$Adb = "D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$csv = Join-Path $root "docs\measurements\fase0-$Label.csv"

& $Adb logcat -c
Write-Host "Coletando $Seconds s de logcat... (deixe o app rodando no headset)"
$job = Start-Job { param($adb) & $adb logcat -v time -s ScreenPortal:I Unity:I } -ArgumentList $Adb
Start-Sleep -Seconds $Seconds
Stop-Job $job
$lines = Receive-Job $job | Where-Object { $_ -match "SPSTAT,|SPKB," }
Remove-Job $job

"time,stream,rx_fps,dec_fps,mbps,drop_net,drop_dec,lat_avg_ms,lat_min_ms,lat_max_ms,rtt_ms" | Set-Content $csv -Encoding utf8
foreach ($l in $lines) {
    if ($l -match "^(\S+ \S+).*(SPSTAT,[^\s]+)") { "$($Matches[1]),$($Matches[2].Substring(7))" | Add-Content $csv -Encoding utf8 }
}
$kb = $lines | Where-Object { $_ -match "SPKB," }
if ($kb) { $kb | Set-Content (Join-Path $root "docs\measurements\fase0-$Label-keyboard.log") -Encoding utf8 }

$rows = Import-Csv $csv | Where-Object { $_.lat_avg_ms -ne "-1.0" }
if ($rows) {
    foreach ($g in $rows | Group-Object stream) {
        $lat = $g.Group | ForEach-Object { [double]$_.lat_avg_ms } | Sort-Object
        $p95 = $lat[[math]::Min($lat.Count - 1, [math]::Floor($lat.Count * 0.95))]
        $dec = ($g.Group | ForEach-Object { [double]$_.dec_fps } | Measure-Object -Average -Minimum)
        $dd = ($g.Group | ForEach-Object { [int]$_.drop_dec } | Measure-Object -Sum).Sum
        $dn = ($g.Group | ForEach-Object { [int]$_.drop_net } | Measure-Object -Sum).Sum
        "stream {0}: lat média {1:F1} ms, p95 {2:F1} ms, dec fps média {3:F1} (mín {4:F1}), perdas rede {5}, decoder {6}" -f `
            $g.Name, ($lat | Measure-Object -Average).Average, $p95, $dec.Average, $dec.Minimum, $dn, $dd
    }
}
Write-Host "CSV: $csv"
