# Fase 2F — frame rate por tela + orçamento: verificação (01/10/2026)

**Branch:** `phase2f/frame-rate-budget` · **Spec:** [specs/2026-09-30-fase2f-fps-orcamento-design.md](../specs/2026-09-30-fase2f-fps-orcamento-design.md) ·
**Plano:** [plans/2026-09-30-fase2f-fps-orcamento.md](../plans/2026-09-30-fase2f-fps-orcamento.md)

## Testes automáticos
Harness `dotnet test`: 310 · Unity EditMode: 318 · Host: 94 — 0 falhas.

## Quest (Cayo no óculos, host real, 5 telas)
| # | Ação | Resultado |
|---|---|---|
| F1 | Olhar de uma tela para outra | ✅ a olhada fica fluida, a anterior cai para 15 fps (log: `tela N → 60 fps` / `→ 15 fps`) |
| F2 | Menu → Taxa de quadros ▸ | ✅ modos, atual marcado, status "Auto · 60 fps" |
| F3 | Vídeo fora do foco | ✅ continua a 60 |
| F4 | Eco olhando | ✅ continua a ~5 fps |
| F5 | 5 telas em Vídeo | ✅ aviso "Essa configuração pode causar travadas" |

## Banda (log do host, `TakeStats`)
| Cenário | Por tela | Total (5 telas) |
|---|---|---|
| Antes da 2F (60 fps fixos, 20 Mbps) | 20 Mbps | ~100 Mbps |
| 2F com bitrate mudado ao vivo (bug, corrigido) | 60 fps: 60–90 Mbps · 15 fps: 3,75 Mbps | ~200 Mbps |
| 2F corrigida (2 Vídeo + 3 Auto fora do foco) | Vídeo 60 fps: ~30 Mbps · Auto 15 fps: 5,0 Mbps | ~70 Mbps |

**Achado (corrigido):** mudar o bitrate do encoder AMF ao vivo (`ICodecAPI` `AVEncCommonMeanBitRate`) o desregula: passa a
reservar bits por quadro como se o stream fosse ~20 fps (×3 a 60 fps, ×0,75 a 15 fps). Sem mudar o bitrate, o AMF
reserva `bitrate ÷ 60` por quadro (configurado a 60 fps), então cortar quadros já baixa a banda na proporção certa.
Correção: mudar fps só corta quadros; mudar a base (Vídeo 30 Mbps ↔ 20 Mbps) recria o encoder da tela mantendo os
frameIds (`StreamRateLimits.BaseKbpsAt60` / `NeedsNewEncoder`).

## Também nesta branch
- Host reinicia o dispositivo do SudoVDA (`pnputil /restart-device`) e tenta de novo quando o driver cai (código 43).
  A recuperação ainda não foi exercitada de verdade (o driver estava de pé quando o host novo abriu).
- Desvio da spec: o status do menu mostra o fps **concedido**, não o medido (o `IScreenVideo` não expõe fps por tela).
- Medição de perdas do Wi-Fi por minuto (spec §6) não foi feita à parte; com ~70 Mbps no total contra ~100 antes, o
  risco de rajada cai. Fica para a Fase 5 (bitrate adaptativo à rede).
