# ADR 0008 — Capacidade de decode do Quest 3 e latência mínima do caminho de vídeo

- **Status:** Aceito (Fase 2A, 29/09/2026)
- **Spec:** docs/specs/2026-09-28-fase2a-spikes-design.md · **Medição:** [fase2a-decoder.md](../measurements/fase2a-decoder.md)

## Contexto
A Fase 0 deixou em aberto quantas telas o decoder do Quest 3 sustenta (multi-tela instável). O critério do MVP é o
cenário do brief: 1 tela em foco a 60 fps + 3 a 15 fps, 2560×1080.

## Resultado
- **Capacidade:** 4 telas a 60 fps (80 Mbps) e 6 telas (1×60 + 5×15), todas sem nenhum descarte e com o app a 72 fps.
  Não é preciso o plano B do brief §12 (atlas) para o MVP.
- **Latência:** o decoder segurava 1 quadro por causa do SPS do AMF (`max_dec_frame_buffering = 4`). Com 1 quadro de
  referência no encoder e saída em ordem de decodificação no decoder, a latência captura→decoder caiu para
  **~16 ms a 60 fps** (era 32) e **~20 ms a 15 fps** (era ~88).
- **Critério do MVP:** atendido pelo decoder nas 3 repetições. As únicas amostras abaixo do alvo foram quedas de
  recebimento do Wi-Fi (uma amostra de 1 s em 3 min).

## Decisões
1. **Encoder:** sempre `CODECAPI_AVEncVideoMaxNumRefFrame = 1`, sem B-frames (já era). Qualquer encoder novo (NVENC,
   QSV, HEVC) precisa garantir `max_dec_frame_buffering ≤ 1` no SPS — verificar com `ffmpeg -bsf:v trace_headers`.
2. **Decoder:** `low-latency` + `vendor.qti-ext-dec-low-latency.enable` + `vendor.qti-ext-dec-picture-order.enable`.
3. **Limites de partida para o `BandwidthBudget` (2F):** até **4 telas em Video (60 fps)** e até **6 telas** no total
   com a política de foco (1×60 + 5×15), a 20 Mbps por tela. Acima disso, sem dados: a 2F mede antes de liberar.
4. **Política de descarte (mantida da 2A):** espera de até 16 ms por input buffer; em perda, KFRQ (UDP 9101) e IDR
   imediato; IDR periódico de 5 s como rede de segurança.

## Consequências
- A latência do foco (16 ms até a saída do decoder) deixa folga grande no orçamento de 50 ms do brief.
- O gargalo seguinte é o **Wi-Fi** (rajadas de ~1 s). É assunto da 2F: bitrate por tela proporcional ao fps e medição
  de jitter por rede.
- Não medido: 4K (3840×2160) — o monitor de teste é 2560×1080. Fica para a 2C, que cria monitores virtuais maiores
  com o SudoVDA (ADR 0007). Também não isolamos qual das duas mudanças de latência resolve sozinha; as duas ficam.
