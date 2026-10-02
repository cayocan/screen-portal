# Fase 3B–3D — verificação (02/10/2026)

Specs: [3B](../specs/2026-10-01-fase3b-cursor-edicao-design.md) · [3D](../specs/2026-10-02-fase3d-janela-espelhada-design.md) ·
Plano: [3B](../plans/2026-10-01-fase3b-cursor-edicao.md) · ADRs: [0017](../adr/0017-world-cursor-and-edit-mode.md),
[0018](../adr/0018-window-mirror-instead-of-stage-monitor.md) (desligada)

## Testes automatizados
| Suíte | Resultado |
|---|---|
| `dotnet test tests-dotnet/UnityPureCode.Tests` | 380 ✅ |
| EditMode (Unity) | 388 ✅ |
| `dotnet test host/Host.Tests` | 163 ✅ |

## Medições no PC
- Extração de cursores do sistema (`cursor-probe`): seta, I-beam (preto com contorno branco), espera, mão, ↔, ↕ — 32×32, hotspot certo.
- Espelho de janela (`popup-spike`, `mirror-spike`): ver [fase3d-spike-popups](fase3d-spike-popups.md).

## Quest (Cayo)
| Bloco | Teste | Resultado |
|---|---|---|
| 3B-1 cursor na tela | Cursor desenhado pelo óculos, sem atraso do vídeo, formas certas | ✅ (borrado no eye buffer → virou compositor layer → nítido) |
| 3B-2 cursor no mundo | Sai pela borda na hora (sem resistência, a pedido), anda no mundo, entra na tela que toca no ponto certo | ✅ |
| 3B-3 modo edição | `Ctrl+Alt+E`, menu, `Ctrl+Alt` segurado, `Esc`; lasers só manipulam na edição; mouse arrasta, roda, `Shift`+roda | ✅ |
| Gizmo | Setas X/Y, alça de profundidade, anéis (5°, `Shift` livre, solta da superfície), cubo; roda segurando a parte (curto/longo); desfazer | ✅ |
| 3C menu pelo mouse | `Ctrl+Alt+M` abre onde está o cursor; clique escolhe; `Esc`/clique fora fecha | ✅ |
| Cursor perdido | `Ctrl+Alt+Home` | ✅ |
| 3D janela espelhada | Implementada e medida no PC; **desligada** a pedido antes do teste no óculos | — |

Resumo do Cayo: "Todos os testes passaram".
