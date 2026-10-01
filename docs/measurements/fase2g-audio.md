# Fase 2G — áudio do PC no Quest + microfone do Quest no PC: verificação (01/10/2026)

**Branch:** `phase2g/pc-audio` · **Spec:** [specs/2026-10-01-fase2g-audio-design.md](../specs/2026-10-01-fase2g-audio-design.md) ·
**Plano:** [plans/2026-10-01-fase2g-audio.md](../plans/2026-10-01-fase2g-audio.md) · **Spike:** [fase2g-spike-audio.md](fase2g-spike-audio.md) · ADR 0015

## Testes automáticos
Harness `dotnet test`: 315 · Unity EditMode: 323 · Host: 101 — 0 falhas.

## PC (`ScreenPortalHost audio-test`, sem headset)
| Caminho | Resultado |
|---|---|
| Saída padrão → "Steam Streaming Speakers" e de volta | ✅ trocou e devolveu a original (TEYUN) |
| Tom na saída → loopback → Opus → udp/9300 → decodificado | ✅ RMS 0,19 |
| Tom Opus → udp/9301 → "Microfone (Steam Streaming Microphone)" | ✅ RMS 0,24 |

## Quest (Cayo no óculos, host real)
| # | Teste | Resultado |
|---|---|---|
| G1 | Vídeo/música no PC | ✅ toca no Quest, não nas caixas do PC |
| G2 | Vídeo com fala | ✅ som junto com a imagem (avaliação no óculos; a medição por claquete com número fica para a Fase 5) |
| G3 | Gravador/Discord com "Microfone (Steam Streaming Microphone)" | ✅ grava a voz pelo Quest |
| G4 | Menu → Áudio ▸ → mudo | ✅ para de chegar voz |
| G5 | Fechar o host | ✅ a saída de som do PC volta à anterior |

## Notas
- Requisito até a Fase 5: Steam instalado (drivers de streaming). Sem eles, som também no PC e sem microfone (ADR 0015).
- Buffer de reprodução no Quest: 40 ms para começar, descarta acima de 120 ms.
