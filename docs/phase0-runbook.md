# Fase 0 — runbook (o que rodar e em que ordem)

## 0. Uma vez
1. Quest 3 em **modo desenvolvedor**, ligado por USB, `adb devices` mostrando o headset.
2. PC **no cabo de rede**, Quest no Wi-Fi 6/6E perto do roteador, mesma sub-rede.
3. Firewall do Windows: liberar o `ScreenPortalHost.exe` (UDP de saída para 9000+ e entrada em 9100).
   ```powershell
   New-NetFirewallRule -DisplayName "Screen Portal host (clock sync)" -Direction Inbound -Protocol UDP -LocalPort 9100 -Action Allow
   ```
4. No Unity: **Screen Portal → Phase 0 → Build All** (já foi rodado; repita se mudar algo nas cenas).

## 1. Validar o host sozinho (sem headset)
```powershell
powershell -ExecutionPolicy Bypass -File tools/host-loopback-test.ps1
```
Esperado: ~60 fps, `cap→enc` < 15 ms de média, ffprobe `h264 Main has_b_frames=0`.

## 2. Vídeo no headset (1 stream)
1. Unity: **Screen Portal → Phase 0 → Boot scene: Video**. Em `Phase0Video` confira
   `videoWidth/videoHeight` = resolução do monitor primário (o host imprime ao iniciar).
2. **File → Build And Run** (Android, headset conectado). Ou `Build` + `adb install -r`.
3. Descubra o IP do Quest: `adb shell ip -f inet addr show wlan0`.
4. No PC:
   ```powershell
   dotnet run --project host/Host.App -c Release -- stream --target <ip-do-quest> --bitrate 20000
   ```
5. Coloque o headset: a tela aparece a ~1,3 m; o HUD abaixo mostra `SPSTAT` por tela.
6. Coleta: `powershell -ExecutionPolicy Bypass -File tools/phase0-collect.ps1 -Label 1stream-2560x1080`
   → preencha a tabela 2 de [measurements/fase0.md](measurements/fase0.md).
   Dica: deixe um vídeo ou um cronômetro rodando no PC — o WGC só entrega frame quando a tela muda.

## 3. Decoders simultâneos (1/2/4/6)
Para cada N: `Phase0Video.streamCount = N` → Build And Run →
`... stream --target <ip> --streams N` → `phase0-collect.ps1 -Label Nstreams`.
Abra o **OVR Metrics Tool** (HUD de fps/GPU) durante o teste. Tabela 3 do fase0.md.

## 4. Zona de passthrough
1. Unity: **Screen Portal → Phase 0 → Boot scene: Passthrough** → Build And Run.
2. Aceite a permissão de **Spatial Data** na primeira abertura.
3. Coloque o teclado na mesa; teste 2–3 teclados. Controles no cabeçalho de
   [measurements/fase0-passthrough.md](measurements/fase0-passthrough.md). Logs: `adb logcat -s Unity | findstr SPKB`.

## Recompilar a lib nativa
```powershell
powershell -ExecutionPolicy Bypass -File tools/build-native.ps1   # copia o .aar para Assets/Plugins/Android
```
