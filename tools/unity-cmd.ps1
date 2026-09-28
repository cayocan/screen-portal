# Dispara um comando no Unity Editor aberto via gatilho por arquivo (FileCommandWatcher.cs) e espera
# o resultado, com timeout. Não usa MCP (que trava quando o Editor abre diálogos modais).
#   powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk [-TimeoutMinutes 40]
# Comandos: refresh, build-scenes, boot-video, boot-passthrough, build-apk
param([Parameter(Mandatory)][string]$Command, [int]$TimeoutMinutes = 5)

$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root "Temp\sp-commands"
New-Item -ItemType Directory -Force $dir | Out-Null
$done = Join-Path $dir "$Command.done"
if (Test-Path $done) { Remove-Item $done -Force }
New-Item -ItemType File -Force (Join-Path $dir $Command) | Out-Null

$log = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    if (Test-Path $done) { Get-Content $done; exit 0 }
}
Write-Host "TIMEOUT: sem resultado em $TimeoutMinutes min. Log modificado em $((Get-Item $log).LastWriteTime)."
Write-Host "Se o log está parado, o Editor provavelmente tem um diálogo modal aberto (feche-o)."
if (Test-Path (Join-Path $dir $Command)) { Write-Host "O gatilho não foi consumido: o watcher ainda não compilou ou o Editor está bloqueado." }
exit 1
