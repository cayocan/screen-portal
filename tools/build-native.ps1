# Compila headset-native (.aar: Kotlin + C++/NDK) com o JDK, SDK, NDK e Gradle embarcados na Unity
# e copia o resultado para Assets/Plugins/Android/.
#   powershell -ExecutionPolicy Bypass -File tools/build-native.ps1 [-UnityEditor "D:\Unity Editor\6000.3.19f1\Editor"]
param([string]$UnityEditor = "D:\Unity Editor\6000.3.19f1\Editor", [string]$Task = "assembleRelease")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$android = Join-Path $UnityEditor "Data\PlaybackEngines\AndroidPlayer"
$sdk = Join-Path $android "SDK"
$ndk = Join-Path $android "NDK"
$env:JAVA_HOME = Join-Path $android "OpenJDK"
$launcher = Get-ChildItem (Join-Path $android "Tools\gradle\lib") -Filter "gradle-launcher-*.jar" | Select-Object -First 1
if (-not $launcher) { throw "Gradle da Unity não encontrado em $android\Tools\gradle" }

$native = Join-Path $root "headset-native"
"sdk.dir=$($sdk.Replace('\', '/'))" | Set-Content (Join-Path $native "local.properties") -Encoding ascii

Push-Location $native
try {
    & "$env:JAVA_HOME\bin\java.exe" -cp $launcher.FullName org.gradle.launcher.GradleMain `
        --no-daemon --console=plain -q "-PspNdkPath=$($ndk.Replace('\', '/'))" ":screenportal:$Task"
    if ($LASTEXITCODE -ne 0) { throw "Gradle falhou ($LASTEXITCODE)" }
} finally { Pop-Location }

$aar = Join-Path $native "screenportal\build\outputs\aar\screenportal-release.aar"
$dest = Join-Path $root "Assets\Plugins\Android"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $aar (Join-Path $dest "screenportal-native.aar") -Force
Write-Host "OK: $dest\screenportal-native.aar"
