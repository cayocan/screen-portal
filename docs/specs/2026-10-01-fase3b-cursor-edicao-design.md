# Fase 3B — Cursor no headset, cursor no mundo e modo edição — Design

**Data:** 01/10/2026 · **Aprovado por:** Cayo (brainstorm de 01/10)
**Brief:** §10.6–10.11 (revisto na v1.3.6) · **ADR:** [0017](../adr/0017-world-cursor-and-edit-mode.md) · Depois de: 3A ([ADR 0016](../adr/0016-windows-arrangement-follows-quest.md))

## 1. Objetivo
| # | Requisito |
|---|---|
| 1 | O cursor do Windows é **desenhado no headset** na posição exata, sem o atraso do vídeo (a captura sai sem cursor) |
| 2 | Ao sair pela borda de uma tela do Quest (sem monitor fora do Quest do outro lado), o cursor **vira um cursor 3D no mundo**, nascendo no ponto de saída |
| 3 | O cursor 3D, ao tocar uma tela, **some do mundo e entra nela no ponto do toque** (entrada e saída respeitadas) |
| 4 | Monitores do PC fora do Quest continuam acessíveis pelo arranjo do Windows (cursor invisível no Quest enquanto lá) |
| 5 | **Modo edição** global, ligado/desligado: só nele as telas são manipuladas, por **mouse, controle ou mão** (mesma máquina, mesmo undo); fora dele os lasers só servem ao menu |
| 6 | Modo edição por: item do menu de pulso, `Ctrl+Alt+E` (alterna), `Esc` (sai), `Ctrl+Alt` segurado (temporário) |

Fora do escopo: clicar no conteúdo das telas com o laser, menu pelo mouse, moldura "mouse-first" que aparece por proximidade fora da edição (3C); janela destacada (3D); cursor animado (só o 1º quadro).

## 2. Estados do mouse (host)
| Estado | Mouse vai para | Cursor no Quest |
|---|---|---|
| **Tela** | Windows | desenhado no headset na posição exata |
| **Mundo** | engolido; deltas acumulados → headset | cursor 3D no espaço |
| **Edição** | engolido; deltas, botões e roda → headset | cursor 3D como ponteiro de manipulação |
| **Fora do Quest** (monitor físico não espelhado) | Windows | invisível |

- Hook `WH_MOUSE_LL` numa thread própria; o callback só lê o evento, decide engolir/passar e grava o estado (sem I/O, sem alocação). Um envio UDP separado lê o estado mais recente.
- **Saída (Tela → Mundo):** o ponto pedido pelo evento fica fora do monitor atual. Se cair num monitor fora do Quest → passa (Windows). Senão → engole e acumula a **resistência**: 40 px empurrando para fora em até 300 ms (zera se parar ou voltar). Vencida, entra em Mundo com o ponto de saída (tela, x, y); o cursor do Windows fica parado na borda.
- **Entrada (Mundo → Tela):** `CursorEnter(tela, x, y)` do headset → `SetCursorPos` no pixel correspondente do monitor da tela (coordenadas do desktop) → estado Tela.
- **Segurança:** sem sessão de headset, o hook não intercepta nada. Sessão caiu em Mundo/Edição → volta a Tela e devolve o cursor ao ponto de saída.
- Movimento normal entre duas telas do Quest coladas no Windows também passa pela saída: a entrada imediata no headset (§4) faz a travessia parecer direta e respeita o ponto que o usuário vê.

## 3. Canais e mensagens
- **UDP host → headset, `MouseState`** (porta UDP do headset, informada em `StartCursor`): estado, `screen_key`, x, y (pixels da tela), `shape_id`, deltas **acumulados** desde a entrada no Mundo/Edição (dx, dy em pixels do desktop, com a aceleração do Windows), botões (esq., dir., meio), roda acumulada, `Shift` seguro, sequência. Enviado a cada mudança (até 240 Hz) e, em Mundo/Edição, ao menos a cada 50 ms. Pacote perdido não estraga nada (estado completo).
- **TCP de controle** (FlatBuffers, fim da union):
  - `StartCursor { udp_port:ushort; }` (headset → host): começa o `MouseState`.
  - `CursorShape { shape_id:uint; width:ushort; height:ushort; hot_x:ushort; hot_y:ushort; bgra:[ubyte]; }` (host → headset): uma vez por forma nova.
  - `CursorEnter { screen_key:uint; x:int; y:int; }` (headset → host).
  - `SetEditMode { on:bool; }` (headset → host).
- **Captura:** WGC com `IsCursorCaptureEnabled = false` em todas as telas (virtuais e espelhos).
- **Formas:** `GetCursorInfo` → `GetIconInfo` → BGRA. Monocromático/XOR (ex.: I-beam): pixels de inversão viram preto com contorno branco de 1 px. `shape_id` = hash do bitmap.

## 4. Headset
- **`WorldCursor`** (Application, puro): direção (azimute/elevação) em volta da cabeça. Nasce no ponto de saída (pixel → ponto 3D da tela → direção). Delta em pixels × graus por pixel da última tela (largura angular ÷ largura em pixels) → nova direção; elevação limitada a ±85°. Raio cabeça → direção contra as telas (`ScreenHitTest`, plana e curva): tocou → `CursorEnter` com o pixel do toque. Depois de uma entrada ou saída, toques na mesma tela são ignorados por 80 ms.
- **`EditMode`** (Application): `Enabled`, `Temporary` (vindo do `Ctrl+Alt` segurado, reportado no `MouseState`), evento `Changed`; ligado → manda `SetEditMode(true)`; ao conectar começa desligado. Desligar: se o cursor 3D está sobre uma tela → `CursorEnter`; arraste em andamento → cancelado sem Command.
- **`MousePointer`** (Application): adapter `IManipulationInput` — raio cabeça → cursor 3D; botão esquerdo = `SelectHeld`, direito = `GripHeld`, roda = `DistanceDeltaM` (5 cm por clique), `Shift`+roda = `ResizeScreen` ±10 % na tela sob o cursor, `Esc` = `CancelPressed` (com arraste) ou sai do modo. Só ativo na Edição.
- **Lasers fora da edição:** `InteractionRouter` só entrega controle/mão à manipulação com a Edição ligada; fora dela, só menu. Tchau continua.
- **Edição ligada:** molduras e alças visíveis em todas as telas (não só no hover) e selo "Modo edição" na visão.
- **`CursorView`** (Presentation): quad no eye buffer (acima das telas, ADR 0010), 2 mm à frente da tela, orientada pela normal; posição = pixel → ponto 3D (geometria plana/cilíndrica já usada nas alças); tamanho = pixels da forma × metros por pixel da tela; hotspot alinhado. Texturas por `shape_id` em cache (aloca só em forma nova). No Mundo: mesma forma, tamanho angular igual ao que tinha na última tela, na distância dela, sempre por cima, com halo. Na Edição, sobre alças: ícones de mover/redimensionar do headset. Fora do Quest: escondido.
- **Recepção UDP** (Infrastructure): thread com buffer fixo; o quadro lê só o pacote mais recente (sem alocação). O cursor não é vídeo: passar pelo C# não fere a regra do vídeo.
- Menu de pulso: item "Modo edição: ligado/desligado" (`MaxItems` → 17).

## 5. Atalhos
`Ctrl+Alt+E` = `ToggleEditMode` (nova `HotkeyAction`, no fim). `Esc` registrado pelo host **só enquanto a edição está ligada**. `Ctrl+Alt` segurado: o hook lê os modificadores (`GetAsyncKeyState`) e marca `Temporary` no `MouseState`.

## 6. Ordem e verificação
1. **3B-1 — cursor da tela:** captura sem cursor, `MouseState` (estado Tela), formas, `CursorView` na tela. **Quest:** atraso do cursor × vídeo, leitura sobre texto, I-beam visível.
2. **3B-2 — cursor no mundo:** saída com resistência, `WorldCursor`, entrada no ponto certo, monitores fora do Quest pelo Windows, queda da sessão. **Simulator** (host real no PC) + Quest.
3. **3B-3 — modo edição:** `EditMode`, `MousePointer`, lasers só na edição, alças visíveis, atalhos/menu/`Esc`/`Ctrl+Alt`. **Simulator** + Quest.

## 7. Testes
Host (xUnit, puro): `MouseRouter` (estados, borda → Quest/fora do Quest/nada, resistência e tempo, `CursorEnter`, queda da sessão, edição engole tudo); conversão de cursor monocromático; codec das mensagens novas. Headset (NUnit, puro): `WorldCursor` (delta → graus, toque → pixel em plana e curva, 80 ms), `MousePointer` → `ManipulationInput`, `EditMode` (lasers só nele, desligar cancela arraste, desligar sobre tela entra nela), pixel → ponto 3D do cursor. Simulator e Quest como em §6.
