# Fase 2D — recriar com outra resolução: verificação (30/09/2026)

**Branch:** `phase2e/curved-screens` (2D e 2E juntas) · **Spec:** [specs/2026-09-30-fase2d-recriar-design.md](../specs/2026-09-30-fase2d-recriar-design.md) ·
**Plano:** [plans/2026-09-30-fase2d-recriar.md](../plans/2026-09-30-fase2d-recriar.md) · ADR 0012

## Testes automáticos
Harness `dotnet test`: 288 · Unity EditMode: 296 · Host (`MonitorRecreator`, `MonitorArrangement`, planos de janela): 84 — 0 falhas.

## Quest + host real (Cayo no óculos)
| # | Ação | Resultado |
|---|---|---|
| D1 | Foco no espelho | ✅ "Recriar ▸" apagado |
| D2–D3 | Monitor 2560×1440 com o Bloco de Notas → Recriar → 1920×1080 | ✅ monitor muda no Windows, janelas vão junto (log: "recriado 2560×1440 → 1920×1080 (… 5 janela(s) movida(s))"); a tela fica no lugar |
| D4 | Desfazer | ✅ volta a 2560×1440 com as janelas |
| D5 | Maximizado → Retrato 9:16 1440×2560 | ✅ continua maximizado no monitor em pé |
| D6 | Monitor que estava em cima do principal, recriado | ❌ → ✅ ia para a direita no arranjo do Windows (o mouse perdia a continuidade). Correção: o novo ocupa o lugar do antigo, grudado no mesmo vizinho (`MonitorArrangement.Reattach` + `SetDisplayConfig`); posições negativas (em cima, à esquerda) testadas no PC |

## Notas
- `ChangeDisplaySettingsEx` e `SetDisplayConfig` aceitam posições negativas; sobreposição com outro monitor é recusada
  (87) ou "corrigida" pelo Windows — por isso o alvo é calculado encostado no vizinho.
- Pendente de medir: barra de tarefas no topo/à esquerda (posição de restauração de janelas maximizadas/minimizadas
  assume barra embaixo — relatório do plano, desvio 7).
