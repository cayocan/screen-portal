# Fase 2B — Superfície de layout + manipulação por controle/mãos + undo — Design

- **Status:** aprovado pelo Cayo em 29/09/2026 (brainstorming).
- **Brief:** §14 Fase 2 (superfície de layout, mover/resize, undo), §10.10, §10.12, D10, D12.
- **Base:** domínio da superfície já especificado em
  [2026-09-28-superficie-layout-design.md](2026-09-28-superficie-layout-design.md) (ADR 0005) — esta spec não o
  redesenha, só define a manipulação, o menu, as telas de teste e a verificação.

## 1. Decisões

| # | Decisão |
|---|---|
| 1 | Várias telas antes da 2C via **telas de teste sem vídeo** (textura estática), telas normais do domínio |
| 2 | Pegar **pela moldura** (mover) e **pelas alças** dos cantos (redimensionar); meio da tela reservado ao Windows (Fase 3) |
| 3 | Ações por **menu de pulso + atalhos** no controle |
| 4 | **Controlador próprio** (`ScreenManipulationController`), o Interaction SDK só fornece raio e selecionar/soltar |

## 2. Escopo

**Entra:** (1) domínio completo da superfície conforme a spec de 28/09 (`LayoutSurface`, `Placement`, `ToPose`,
`CylinderMath`, `SurfaceSnapper`, os 10 comandos, migração de `VirtualScreen`/`CreateScreen`/views, `MoveScreen(Pose)`
sai); (2) manipulação por controle/mãos com pré-visualização e guias; (3) menu de pulso + atalhos; (4) telas de teste;
(5) recentrar pelo botão e pelo evento do sistema; (6) `CompositeCommand`.

**Fora:** mouse e atalhos `Ctrl+Alt` (Fase 3), várias telas com vídeo (2C), seletor "Novo monitor" (2D), curvatura
pela moldura (2E), persistência do layout (perfis, Fase 3b — na 2B o layout volta ao padrão ao reabrir).

## 3. Arquitetura

| Camada | Conteúdo | Testado com |
|---|---|---|
| Domain | superfície, placement, snapper, comandos, `CompositeCommand` | NUnit (harness `dotnet test` + Unity) |
| Application | `ScreenManipulationController` (State Machine), `IManipulationInput`, `ManipulationPreview` | NUnit com entrada simulada |
| Presentation | moldura, alças, guias, menu de pulso, aplicação da pré-visualização na view | Meta XR Simulator |
| Infrastructure/Unity | `InteractionSdkManipulationAdapter` (raio/seleção ISDK → `IManipulationInput`), evento de recentrar do `OVRManager` | Simulator |

Fluxo: adapter → eventos de entrada → controlador calcula a pré-visualização com as funções puras (sem alocação) →
a view desenha (nunca muda estado) → ao soltar, **um** Command no `CommandBus` → undo pelo `WorkspaceStore`.
O mouse da Fase 3 é outro adapter do mesmo `IManipulationInput`.

## 4. Manipulação

**Alvos** (hit-test por collider do ISDK na view): **moldura** (faixa de ~2 cm em volta, aparece no hover) → mover;
**alças** (4 cantos) → redimensionar proporcional (largura limitada pelo `ScreenLimits`; resolução nunca muda);
**meio da tela** → nada.

**Estados:** `Idle → Hover(alvo) → Dragging(Move | Resize) → Commit | Cancel`.

| Gesto | Controle | Mãos | Pré-visualização | Command ao soltar |
|---|---|---|---|---|
| Mover | gatilho na moldura + arrastar o raio | pinçar a moldura | segue o ponto onde o raio cruza a superfície (az/el); no Cilindro a vertical sobe reta | `MoveScreenOnSurface` |
| Afastar/aproximar | analógico Y durante o arraste | puxar/empurrar a mão durante a pinça | distância (raio horizontal no Cilindro) | `SetScreenDistance` (com o mover: um `CompositeCommand`) |
| Soltar da superfície | segurar grip ao arrastar | pinça com a outra mão fechada | segue o raio num plano | `SetScreenLoose` |
| Redimensionar | gatilho na alça + arrastar | pinçar a alça | largura segue a distância do raio ao centro da tela | `ResizeScreen` |
| Sem encaixe | segurar o gatilho da outra mão | pinçar com a outra mão | `SurfaceSnapper` desligado | — |
| Cancelar | B/Y durante o arraste | — (soltar a pinça confirma; para voltar, Desfazer no menu) | volta à posição original | nenhum |

**Regras:** um passo de undo por gesto (`CompositeCommand` aplica tudo ou nada); encaixe a ≤ 1,5° das vizinhas/grade,
desgruda ao continuar arrastando, guias só durante o arraste; uma manipulação por vez (a outra mão é ignorada durante
um arraste); tela solta mostra "Voltar à superfície" na moldura (`ReturnScreenToSurface`).

## 5. Menu de pulso, atalhos, telas de teste, recentrar

- **Menu de pulso:** painel no pulso esquerdo (mão, ao virar a palma para você) ou no controle esquerdo (segurando
  Menu ≡); acionado por poke ou pelo raio da outra mão. Botões: Desfazer, Refazer (apagados quando não há o que
  desfazer/refazer), Cilindro/Esfera, Recentrar, Alinhar à postura, Grade, + Tela de teste, Remover tela (a em foco);
  linha de status (modo, nº de telas).
- **Atalhos no controle** (fora de arraste): X = desfazer, Y = refazer. Botões de sistema não são usados.
- **Telas de teste:** `CreateScreen` normal com textura estática (grade + número + resolução), alternando
  1920×1080, 2560×1440 e 1440×2560; nascem pela regra da spec de 28/09 (az 0°, el −5°, 1,3 m; se ocupado, o primeiro
  espaço livre à direita). A view usa textura no lugar da Surface do decoder; na 2C troca só a origem da imagem.
  A tela com vídeo do PC continua existindo, igual às outras.
- **Recentrar:** botão (pose atual da cabeça) e evento de recentrar do sistema → `RecenterLayout`.

## 6. Testes e verificação

**Automáticos (NUnit):**
- Domínio: os 9 invariantes da spec de 28/09; `CompositeCommand` (tudo ou nada; undo restaura o estado exato);
  migração (telas existentes ganham `Placement` equivalente à pose antiga); `CreateScreen` no primeiro espaço livre.
- Controlador: cada gesto gera exatamente um Command com os valores certos; cancelar não gera nenhum; uma
  manipulação por vez; sem encaixe não gruda; resize nunca muda a resolução.
- Composição: o VContainer resolve o grafo novo (teste de regressão da 1A).

**Meta XR Simulator (automatizado com o Meta XR Operator):** roteiro — apontar e ver a moldura; mover no Cilindro e na
Esfera; subir reto no Cilindro; afastar; redimensionar; soltar e voltar à superfície; encaixar numa vizinha e
desgrudar; trocar o modo sem nenhuma tela sair do lugar; recentrar; undo/redo de cada passo; cancelar; menu de pulso
com mãos e com controle. O Cayo confere o visual no fim (~5 min).

**Quest (curto, no fim):** conforto da pinça a ~1,3 m e do controle real — o Simulator não reproduz tremor da mão nem
a sensação do gesto.

**Risco:** a troca de `Pose` por `Placement` em `VirtualScreen` atinge arquivos compartilhados com o host
(`Host.Shared`) e os testes da 1A: migrar junto e rodar as duas suítes a cada passo.
