# Fase 2E — telas curvas: verificação (30/09/2026)

**Branch:** `phase2e/curved-screens` · **Spec:** [specs/2026-09-30-fase2e-telas-curvas-design.md](../specs/2026-09-30-fase2e-telas-curvas-design.md) ·
**Plano:** [plans/2026-09-30-fase2e-telas-curvas.md](../plans/2026-09-30-fase2e-telas-curvas.md) · **Spike:** [fase2e-spike.md](fase2e-spike.md) ·
ADR 0010, 0011

## Testes automáticos
Harness `dotnet test`: 288 · Unity EditMode: 296 · Host: 84 — 0 falhas.

## Meta XR Simulator
Pulado: na 2C o Simulator esgotou a memória do PC só de entrar em Play Mode (vazamento do `synth_env_server`,
[fase2c-simulator.md](fase2c-simulator.md) §3). O roteiro foi feito direto no Quest, que cobre também nitidez, troca de
layer e desempenho.

## Quest (Cayo no óculos, host real)
| # | Ação | Resultado |
|---|---|---|
| E1 | Menu → Curvatura ▸ → Suave, Média, Envolvente, Plana | ✅ curva e volta; preset marcado; texto nítido |
| E2 | Alça de curvatura (meio do topo da moldura): empurrar e soltar | ✅ prévia ao vivo com rótulo; um undo desfaz |
| E3 | Troca plana ↔ curva | ✅ sem piscar perceptível |
| E4 | Laser nas bordas de uma tela Envolvente | ✅ acerta o arco; moldura e alças acompanham |
| E5 | Menu e laser na frente da tela curva (furo curvo, ADR 0010) | ✅ por cima, sem recorte |

Spike (antes da implementação): 4 layers cilíndricas a 60 fps, 15–17 ms, 0 perdas; troca plana ↔ curva 35–100 ms.

## Ajustes pedidos no teste (mesma branch)
- Texto da interface em TextMeshPro (SDF) e eye buffer em escala 1,0 (ADR 0013). ✅ confirmado no Quest: mais nítido.
- Laser do controle some com o controle parado (3 s); tchau liga/desliga as mãos (laser, interação, modelo); modelos
  de mão e de controle do Meta SDK. ✅ confirmados no Quest.
- Última sessão salva no headset e restaurada na abertura, recentrada na cabeça (ADR 0014). ✅ verificado: app
  fechado à força duas vezes, as 5 telas voltaram (`reatado` no host, nenhuma expirou) e centralizadas.
