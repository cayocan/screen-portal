# Fase 2A — capacidade e latência do decoder do Quest 3

**Data:** 29/09/2026 · **Commits:** medição base em `1a1c0e5`…`c5232a0`, correção em `ab4f539`
**Decisão:** [ADR 0008](../adr/0008-decode-capacity.md) · **Dados brutos:** [fase2a-decoder/](fase2a-decoder/)

## Condições
- Quest 3 com bateria 82–87 %, no carregador de tomada, adb por Wi-Fi; app do spike de vídeo (cena Phase0, 72 Hz).
- PC: RX 5500 XT (AMF H.264), Ethernet; Quest no Wi-Fi da mesma rede. Conteúdo fixo: `tools/text-test.html` em tela cheia
  no monitor principal 2560×1080 (todas as telas transmitem esse monitor).
- 20 Mbps por tela (inclusive as de 15 fps — pior caso para o decoder: quadros 4× maiores).
- `tools/phase0-streams-test.ps1`: descarta 5 s de aquecimento e as 2 últimas amostras. Latência = captura no host →
  saída do decoder no Quest, com o relógio sincronizado (não inclui o compositor nem o display: ~1 quadro a mais).
- Um monitor virtual ocioso de 800×600 (sobra do VDD, ver ADR 0007) estava ativo; não é transmitido.

## Capacidade (antes da correção de latência)
| Cenário | fps decodificados | Perdas (rede/decoder) | App |
|---|---|---|---|
| 4 × 60 fps (80 Mbps) | todas ≥ 58,9 | 0 / 0 | 72,4 fps |
| 3 × 60 fps | todas ≥ 59,0 | 0 / 0 | 72,4 |
| 2 × 60 fps | todas ≥ 59,0 | 0 / 0 | 72,4 |
| 1 × 60 + 5 × 15 (6 telas) | foco ≥ 58,2; demais ≥ 14 | 0 / 0 | 72,4 |
| 1 × 60 + 3 × 15, ×3 | foco ≥ 58 (r1, r3) | 0 / 0 | 72,2–72,5 |

A instabilidade da Fase 0 (fase0.md §3) não se repetiu: com a espera de 16 ms por input buffer e o pedido de keyframe
(KFRQ), nenhum cenário teve descarte.

## Latência: o decoder segurava 1 quadro
| 1 tela a | intervalo | latência medida | base + 1 quadro |
|---|---|---|---|
| 60 fps | 16,7 ms | 32,5 ms | 33 ms |
| 30 fps | 33,3 ms | 54,9 ms | 49 ms |
| 15 fps | 66,7 ms | 89,8 ms | 83 ms |

Causa: o SPS do AMF saía com `max_num_ref_frames = 4` e `max_dec_frame_buffering = 4`; o decoder da Qualcomm só
liberava o quadro N ao receber o N+1. Correção (`ab4f539`): `CODECAPI_AVEncVideoMaxNumRefFrame = 1` no host (SPS passa a
`max_dec_frame_buffering = 1`, conferido com `ffmpeg -bsf:v trace_headers`) e `vendor.qti-ext-dec-picture-order.enable`
no decoder. (As duas foram aplicadas juntas; não isolamos qual delas resolve.)

## Critério do MVP — 1 × 60 + 3 × 15, 2560×1080, 3 × 60 s
| | Foco (60) lat. | 15 fps lat. | fps mín. foco / demais | Perdas | Critério estrito |
|---|---|---|---|---|---|
| Antes | 32,3–32,5 ms | 85,7–90,5 ms | 58 / 14 | 0 | ❌ 0/3 (latência) |
| **Depois** | **15,9–16,1 ms** | **18,0–22,5 ms** | 57,2 (r1) / 14 | **0** | 2/3 |

A repetição 1 "falhou" por **uma** amostra de 1 s com 57,2 fps no foco. Nessa amostra caiu a taxa de **recebimento**
(`rx` 58,2 → 58,0 → 57,2), e o decoder decodificou tudo o que chegou: é rajada do Wi-Fi, não capacidade do decoder.
O mesmo aconteceu no teste de 1 tela a 15 fps (`rx` 13 e depois 16,8). **Critério do decoder: ✅ nas 3 repetições**
(0 descartes, latência média < 50 ms, decodifica 100 % do recebido). O jitter do Wi-Fi fica para a 2F (orçamento de banda).
