# Fase 0 — medições do caminho de vídeo

Como rodar: [docs/phase0-runbook.md](../phase0-runbook.md). CSV bruto: `docs/measurements/fase0-*.csv`
(gerado por `tools/phase0-collect.ps1` a partir das linhas `SPSTAT` do logcat).

## Ambiente
| | |
|---|---|
| PC | Windows 10 Pro 19045 · AMD Radeon RX 5500 XT (AMF) · monitor 2560×1080 |
| Rede | _preencher: roteador, Wi-Fi 6/6E, canal, PC no cabo?_ |
| Headset | Quest 3 · Horizon OS _vXX_ · build Development/Release |
| Commit | _hash_ |

## 1. Host (PC) — medido em 28/09/2026 ✅
`tools/host-loopback-test.ps1` (captura → encode → UDP 127.0.0.1 → remontagem):

| Resolução | fps | Bitrate | Captura→bitstream (média) | Máx | Perdas | Stream |
|---|---|---|---|---|---|---|
| 2560×1080 | 60 | 20 Mbps CBR | **~8 ms** | 24–32 ms | 0 | H.264 Main, `has_b_frames=0`, validado com ffprobe |

## 2. Latência captura → decodificado no Quest 3 (1 stream) ⏳
Relógio: clock sync UDP/9100, amostra de menor RTT. Não inclui o scan-out do display do headset.

| Resolução | Bitrate | rx fps | dec fps | Latência média | p95* | Máx | RTT | Perdas rede/decoder |
|---|---|---|---|---|---|---|---|---|
| 2560×1080 | 20 Mbps | | | | | | | |
| 1920×1080 | 15 Mbps | | | | | | | |

\* p95 calculado do CSV por segundo (aprox.).

## 3. Decoders / layers simultâneos ⏳
Host: `ScreenPortalHost stream --target <ip-quest> --streams N`. Unity: `streamCount = N`.

| N streams | App fps (OVR Metrics) | dec fps por tela (mín) | Latência média | Perdas decoder | Observações |
|---|---|---|---|---|---|
| 1 | | | | | |
| 2 | | | | | |
| 4 | | | | | |
| 6 | | | | | |

## 4. Glass-to-glass (opcional, câmera) ⏳
Filmar a tela do PC e o headset juntos a 240 fps com um cronômetro em ms na tela do PC.

## Conclusão
_Preencher: confirma ou revisa o ADR 0001? Limite prático de telas? Próximos ajustes (bitrate, intra-refresh, atlas)?_
