# Teste de loopback do host no próprio PC (sem headset):
# captura o monitor primário → H.264 HW → UDP 127.0.0.1 → receptor de depuração → .h264 → ffprobe.
#   powershell -ExecutionPolicy Bypass -File tools/host-loopback-test.ps1 [-Seconds 6] [-ExtraArgs "--bitrate 30000"]
param([int]$Seconds = 6, [string]$ExtraArgs = "")

$ErrorActionPreference = "Stop"
$env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + $env:PATH
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $outDir | Out-Null

dotnet build "$root/host/Host.App" -c Release -v q -nologo | Out-Null
$exe = Join-Path $root "host/Host.App/bin/Release/net10.0-windows10.0.22621.0/ScreenPortalHost.exe"

$rxFile = Join-Path $outDir "loopback-rx.h264"
$rxLog = Join-Path $outDir "loopback-rx.log"
$rx = Start-Process $exe -ArgumentList "receive --port 9000 --out `"$rxFile`" --seconds $($Seconds + 3)" `
    -NoNewWindow -PassThru -RedirectStandardOutput $rxLog -RedirectStandardError "$rxLog.err"
Start-Sleep -Milliseconds 800

Write-Host "=== host stream ($Seconds s)"
& $exe stream --target 127.0.0.1 --seconds $Seconds @($ExtraArgs -split ' ' | Where-Object { $_ })
$rx.WaitForExit(($Seconds + 5) * 1000) | Out-Null

Write-Host "=== receptor"
Get-Content $rxLog -Tail 3
Get-Content "$rxLog.err" -ErrorAction SilentlyContinue

Write-Host "=== ffprobe"
ffprobe -v error -select_streams v:0 -count_frames `
    -show_entries stream=codec_name,profile,width,height,has_b_frames,nb_read_frames -of compact $rxFile
