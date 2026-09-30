# Instala o Virtual Display Driver (VirtualDrivers/MikeTheTech) a partir do pacote que o winget só extrai.
# Rodar num PowerShell COMO ADMINISTRADOR:
#   winget install --id=VirtualDrivers.Virtual-Display-Driver -e      (baixa e extrai; não instala o driver)
#   powershell -ExecutionPolicy Bypass -File tools/install-vdd.ps1
# Desinstalar: tools/install-vdd.ps1 -Uninstall  (o README do VDD pede desinstalar antes de atualizar o driver da GPU)
param([switch]$Uninstall)

$ErrorActionPreference = "Stop"
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Rode como administrador." }

$pkg = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Directory -Filter "VirtualDrivers.Virtual-Display-Driver*" | Select-Object -First 1
if (-not $pkg) { throw "Pacote não encontrado. Rode antes: winget install --id=VirtualDrivers.Virtual-Display-Driver -e" }
$devcon = Join-Path $pkg.FullName "Dependencies\devcon.exe"
$driverDir = Join-Path $pkg.FullName "SignedDrivers\x86\VDD"   # "x86" no pacote = x64 (a .inf é NTamd64)

if ($Uninstall) {
    & $devcon remove "Root\MttVDD"
    exit $LASTEXITCODE
}

# O driver lê a configuração de C:\VirtualDisplayDriver\vdd_settings.xml (caminho padrão do VDD).
New-Item -ItemType Directory -Force "C:\VirtualDisplayDriver" | Out-Null
if (-not (Test-Path "C:\VirtualDisplayDriver\vdd_settings.xml")) {
    Copy-Item (Join-Path $driverDir "vdd_settings.xml") "C:\VirtualDisplayDriver\vdd_settings.xml"
}
# O host (usuário comum) edita a lista de resoluções no spike: dá escrita ao grupo Usuários só nesse arquivo.
icacls "C:\VirtualDisplayDriver\vdd_settings.xml" /grant "*S-1-5-32-545:M" | Out-Null

& $devcon install (Join-Path $driverDir "MttVDD.inf") "Root\MttVDD"
if ($LASTEXITCODE -ne 0) { throw "devcon falhou ($LASTEXITCODE)." }
Get-PnpDevice -Class Display | Where-Object FriendlyName -match "Virtual Display Driver|MttVDD" | Format-Table Status, FriendlyName, InstanceId
