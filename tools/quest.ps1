# Conexão adb com o Quest: USB se estiver no cabo, senão Wi-Fi (padrão do projeto; o óculos pode ficar no carregador).
#
# Direto:
#   powershell -ExecutionPolicy Bypass -File tools/quest.ps1                 # mostra qual conexão será usada + bateria
#   powershell -ExecutionPolicy Bypass -File tools/quest.ps1 -Install Builds/Phase0.apk [-Launch]
#   powershell -ExecutionPolicy Bypass -File tools/quest.ps1 -EnableWifi     # com o cabo: liga o adb por Wi-Fi (após reiniciar o Quest)
# Em outros scripts (dot-source):
#   . (Join-Path $PSScriptRoot "quest.ps1"); $serial = Get-QuestSerial; & $QuestAdb -s $serial shell ...
#
# Após reiniciar o Quest o adb por Wi-Fi desliga: ligue o cabo uma vez e rode -EnableWifi.
# O IP precisa ser fixo (reserva DHCP no roteador); mude com -QuestIp ou a variável de ambiente SP_QUEST_IP.
param([string]$Install = "", [switch]$Launch, [switch]$EnableWifi,
      [string]$QuestIp = $(if ($env:SP_QUEST_IP) { $env:SP_QUEST_IP } else { "192.168.0.171" }))

$QuestAdb = "D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$QuestUsbSerial = "2G0YC1ZG410B1H"
$QuestPackage = "com.screenportal.app"
$QuestActivity = "com.screenportal.app/com.unity3d.player.UnityPlayerGameActivity"

function Get-QuestSerial([string]$Ip = $QuestIp) {
    $devices = & $QuestAdb devices | Select-String "^(\S+)\s+device$" | ForEach-Object { $_.Matches[0].Groups[1].Value }
    if ($devices -contains $QuestUsbSerial) { return $QuestUsbSerial }
    $wifi = "${Ip}:5555"
    if ($devices -notcontains $wifi) {
        $out = & $QuestAdb connect $wifi 2>&1 | Out-String
        $devices = & $QuestAdb devices | Select-String "^(\S+)\s+device$" | ForEach-Object { $_.Matches[0].Groups[1].Value }
        if ($devices -notcontains $wifi) {
            throw "Quest não encontrado (nem USB, nem $wifi): $($out.Trim()). Óculos ligado e na mesma rede? " +
                  "Se ele reiniciou, ligue o cabo e rode tools/quest.ps1 -EnableWifi."
        }
    }
    return $wifi
}

# Só executa as ações quando chamado direto (não quando incluído com dot-source).
if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = "Stop"
    if ($EnableWifi) {
        & $QuestAdb -s $QuestUsbSerial tcpip 5555
        Start-Sleep -Seconds 2
        & $QuestAdb connect "${QuestIp}:5555"
        $state = & $QuestAdb devices | Select-String "^${QuestIp}:5555\s+(\S+)" | ForEach-Object { $_.Matches[0].Groups[1].Value }
        if ($state -eq "device") { "Wi-Fi ok. Pode desligar o cabo."; exit 0 }
        if ($state -eq "unauthorized") {
            throw "Quest não autorizou este PC: coloque o óculos, aceite 'Permitir depuração USB' (marque 'Sempre permitir') e rode de novo."
        }
        throw "Wi-Fi não conectou (estado: '$state'). Mantenha o cabo e rode de novo."
    }
    $serial = Get-QuestSerial
    $battery = & $QuestAdb -s $serial shell dumpsys battery | Select-String "level: (\d+)" | ForEach-Object { $_.Matches[0].Groups[1].Value }
    $how = if ($serial -eq $QuestUsbSerial) { "USB" } else { "Wi-Fi" }
    "Quest: $serial ($how), bateria $battery %"
    if ($Install) {
        & $QuestAdb -s $serial install -r -g $Install
        if ($LASTEXITCODE -ne 0) { throw "install falhou." }
    }
    if ($Launch) {
        & $QuestAdb -s $serial shell am force-stop $QuestPackage
        & $QuestAdb -s $serial shell am broadcast -a com.oculus.vrpowermanager.prox_close | Out-Null
        & $QuestAdb -s $serial shell am start -n $QuestActivity | Out-Null
        "App aberto."
    }
}
