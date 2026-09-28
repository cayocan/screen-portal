# ADR 0005 — Superfície de layout Cilindro/Esfera e o princípio "Liberdade"

- **Status:** Aceito (28/09/2026, decisão do Cayo no brainstorming)
- **Design completo:** [docs/specs/2026-09-28-superficie-layout-design.md](../specs/2026-09-28-superficie-layout-design.md)
- **Brief:** v1.3.2 — diferencial 11 (§3), pilar A (§4.1), critério (§4.2), Commands (§8.2), atalhos (§10.9),
  movimento (§10.10), seção nova §10.12, roadmap (§14 Fases 2 e 3), decisão D12.

## Contexto
O brief v1.3 dizia só que "arrastar move a tela sobre uma esfera centrada na sua cabeça" (§10.10). O Cayo propôs
tornar a forma da superfície uma escolha do usuário: um **cilindro** (telas viradas para o eixo, sempre em pé) ou uma
**esfera** (telas viradas para o centro nos dois eixos), com melhorias acordadas no brainstorming.

## Decisão
- Superfície por **perfil de layout** (padrão **Cilindro**), com **centro fixo** capturado ao recentrar e
  **telas soltas** como exceção por tela.
- As telas guardam **coordenadas da superfície** (`Placement`: azimute, elevação, distância, ou pose livre); a pose 3D é
  **calculada** por função pura. Cilindro e Esfera põem a tela no mesmo ponto; muda só a orientação.
  Trocar o modo **não move** nenhuma tela.
- Encaixe nas vizinhas ligado, grade angular opcional, esfera inclinável ("alinhar à postura"), curvatura 100% no
  Cilindro coincidindo com o cilindro.
- **Princípio "Liberdade":** encaixes e regras ajudam, nunca prendem — tudo desgruda e toda tela pode sair da superfície.
- **Ordem do roadmap mantida:** a próxima fase continua sendo a 1B (conexão), que não transporta layout. O domínio da
  superfície entra no início da manipulação da Fase 2; o caminho do mouse e o `SpatialMap` na Fase 3.

## Consequências
- ✅ Layout previsível e ergonômico por padrão (Cilindro), sem tirar a liberdade (Esfera, soltas, desgrudar).
- ✅ Um só espaço angular para arraste, encaixe, grade e cursor espacial: regras testáveis como invariantes puras.
- ⚠️ Na Fase 2, `VirtualScreen.Pose` vira `Placement` e `MoveScreen(Pose)` é substituído
  (`MoveScreenOnSurface`, `SetScreenLoose`, `ReturnScreenToSurface`) — migração pequena, só a Fase 1A usa `Pose`.
- ⚠️ Mais comandos e atalhos (`Ctrl+Alt+L/R/T/G`) para manter o caminho do mouse (mouse-first).
