# Fase 3A — verificação (01/10/2026)

Spec: [fase3a](../specs/2026-10-01-fase3a-arranjo-atalhos-design.md) · Plano: [fase3a](../plans/2026-10-01-fase3a-arranjo-atalhos.md) ·
ADR: [0016](../adr/0016-windows-arrangement-follows-quest.md)

## Testes automatizados
| Suíte | Resultado |
|---|---|
| `dotnet test tests-dotnet/UnityPureCode.Tests` | 329 ✅ (`LayoutSync` 6, `HotkeyDispatcher` 8) |
| EditMode (Unity) | 337 ✅ |
| `dotnet test host/Host.Tests` | 132 ✅ (`DisplayArrangement` 7, `ScaleMemory` 7, `HotkeyMap`/router 16, sessão) |

## Spike
`DisplayScale` em monitor SudoVDA: leu 150 %, aplicou 100 % e voltou a 150 % (API não documentada, tipos −3/−4).

## Quest (Cayo, host Release como admin)
| Teste | Resultado |
|---|---|
| Mover telas no Quest (cima, baixo, esquerda, direita) → o arranjo do Windows acompanha; o mouse atravessa como no Quest | ✅ |
| "Arranjo automático: desligado" → o Windows não muda; religar → realinha | ✅ |
| Escala por tela guardada e reaplicada ao recriar/reabrir | ✅ |
| Atalhos globais (mover, tamanho, distância, curvatura, fps, desfazer/refazer, recentrar) na tela sob o cursor | ✅ |

Resumo do Cayo: "tudo funcionando".
