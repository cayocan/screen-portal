# Fase 0 — medições do caminho de vídeo

Como rodar: [docs/phase0-runbook.md](../phase0-runbook.md). CSV bruto: `docs/measurements/fase0-*.csv`
(gerado por `tools/phase0-collect.ps1` a partir das linhas `SPSTAT` do logcat).

## Ambiente
| | |
|---|---|
| PC | Windows 10 Pro 19045 · AMD Radeon RX 5500 XT (AMF) · monitor 2560×1080 |
| Rede | PC no cabo (192.168.0.64), Quest no Wi-Fi (192.168.0.171) · _roteador/banda: preencher_ |
| Headset | Quest 3 · build UP1A.231005.007.A1 (Android 14) · APK Release, IL2CPP ARM64 |
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
| 2560×1080 | 20 Mbps | 60,1 | 60,0 | **~33 ms** | ~34 ms | 44–50 ms | 2,5–3,1 ms | 0 / 1 (só no 1º segundo) |
| 1920×1080 | 15 Mbps | | | | | | | |

Medido em 28/09/2026 04:52 (CSV: `fase0-1stream-2560x1080.csv`). Host no mesmo período:
captura→bitstream 7,5–7,8 ms (máx 13 ms). Ou seja, ~25 ms são rede + remontagem + decode no Quest.
A média geral do CSV (34,6 ms) inclui o 1º segundo de aquecimento do decoder.

\* p95 calculado do CSV por segundo (aprox.).

## 3. Decoders / layers simultâneos ⏳
Host: `ScreenPortalHost stream --target <ip-quest> --streams N`. Unity: `streamCount = N`.

Medido em 28/09/2026 ~05:05, todas as telas 2560×1080, 20 Mbps cada (exceto onde indicado).
Estatísticas descartam os 5 primeiros segundos. App fps = linha `VrApi FPS` do logcat (display a 72 Hz).
Decoder configurado com `MAX_WIDTH/HEIGHT = 4096×4096` nesta rodada.

| N streams | App fps | dec fps por tela (média / mín) | Latência média | Latência máx | Perdas rede / decoder | Observações |
|---|---|---|---|---|---|---|
| 1 | 72 | 60,0 / — | ~33 ms | 44–50 ms | 0 / 0 | |
| 2 | 72,5 (mín 72) | 59,7 / 49 | 33–34 ms | 59–63 ms | 0 / 0 | ✅ |
| 4 | 72,6 (mín 71) | 59,6 / 45 | 35–37 ms | 85–96 ms | 0 / 0 | ✅ 80 Mbps no Wi-Fi sem perda |
| 6 @ 60 | 72,6 (mín 71) | 11–38 / 1–2 | 125–333 ms | ~1 s | 0 / 13–29 | ❌ decoder satura; rede ok (0 perdas) |
| 6 @ 30 (12 Mbps) | 72,5 | 18–28 / 2–6 | 81–152 ms | ~1 s | 0 / 3–15 | ❌ também satura → não é só taxa de pixels |

**Leituras:**
- As compositor layers isolam o app: o fps do app não caiu nem com o decoder saturado (princípio do ADR 0001 confirmado).
- A rede não é o gargalo até ~120 Mbps agregados (0 perdas de rede em todos os casos).
- O limite está no decoder. Hipótese testada e **refutada**: usar `MAX_WIDTH/HEIGHT = resolução real`
  (2560×1080) piorou tudo — 4 e 5 telas caíram para 4–29 fps (CSVs `*-maxfix`), 1 tela seguiu perfeita.
  Provável causa: 1080 linhas codificadas como 1088 → reconfiguração do decoder a cada IDR. Revertido para 4096.
- Efeito cascata identificado: quando falta input buffer, o descarte espera o próximo IDR (1 s). Perto do
  limite isso derruba o fps para ~4. Na Fase 2 (spike do decoder): esperar até ~1 frame por input buffer,
  IDR sob demanda em vez de periódico, e medir de novo 5–6 telas e resoluções menores.
- **Rodada 2 (05:15–05:35), instável — resultados NÃO reproduzem a rodada 1:** com o valor revertido
  (4096) e até após reboot do Quest: 4@60 → 4–22 fps; 3@60 → 14–56 fps; 4@30 → ~29 fps mas com
  latência ~65 ms (vs 33 ms na rodada 1). 1 tela segue perfeita. Rede sempre 0 perdas; app sempre 72 fps.
  Térmico sem throttling (SoC ~45 °C), sem host perdido, sem codec vazado visível no resource manager.
  Variáveis não controladas: conteúdo da tela do PC (afeta o custo de decode), bateria (51 % → 39 %),
  nível de CPU/GPU do headset.
- **Conclusão da Fase 0 sobre capacidade:** o limite estável de decode ainda NÃO está determinado.
  Confirmado: 1–2 telas 2560×1080@60 com folga na rodada 1; 4@60 é o limite superior (passou uma vez).
  Para medir de verdade (spike do decoder, Fase 2): conteúdo fixo no PC (`tools/text-test.html`),
  headset carregado e com nível de desempenho fixo, política de descarte sem cascata (esperar até
  1 frame por input buffer; pedir IDR em vez de esperar o periódico) e 3 repetições por cenário.
- Mesmo sem a correção, o design do brief se sustenta: 4 telas a 60 fps (meta do MVP) passam, e a
  FocusPolicy (fora de foco a 10–15 fps) reduz a carga das demais.

## 4. Glass-to-glass (opcional, câmera) ⏳
Filmar a tela do PC e o headset juntos a 240 fps com um cronômetro em ms na tela do PC.

## Conclusão
_Preencher: confirma ou revisa o ADR 0001? Limite prático de telas? Próximos ajustes (bitrate, intra-refresh, atlas)?_
