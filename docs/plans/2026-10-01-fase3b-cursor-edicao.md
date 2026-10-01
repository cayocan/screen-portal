# Fase 3B — Plano

> Execução inline, TDD onde é puro, um commit por tarefa. Spec: docs/specs/2026-10-01-fase3b-cursor-edicao-design.md.
> Cada bloco termina verificado (Simulator e/ou Quest) antes do próximo.

## 3B-1 — Cursor da tela
1. **Protocolo:** `StartCursor`, `CursorShape`, `CursorEnter`, `SetEditMode` no `control.fbs` (fim da union) +
   `HotkeyAction.ToggleEditMode`; mensagens, codec, testes. **`MouseState`** UDP: formato binário fixo
   (`Infrastructure/Shared/Protocol/MouseStateCodec.cs`, compartilhado com o host), teste de ida e volta.
2. **Host — forma e posição:** `CursorShapes` (`GetCursorInfo`/`GetIconInfo` → BGRA; monocromático → preto com
   contorno branco, parte pura em Host.Domain com testes); `CursorFeed` (thread ~240 Hz: posição + tela sob o cursor
   via `ScreenHost.ScreenAt` + forma; manda `MouseState` por UDP e `CursorShape` novo pela sessão); `HeadsetSession`
   trata `StartCursor`. Captura sem cursor (`captureCursor: false`).
3. **Headset — recepção:** `UdpMouseStateReceiver` (Infrastructure/Shared/Net, thread, buffer fixo, último pacote);
   `CursorSync` (Application): manda `StartCursor` no Welcome, guarda formas (`CursorShape`) e o estado mais recente.
4. **Headset — desenho:** `CursorPlacement` (Application, puro: pixel → pose 3D na tela plana/curva, tamanho em metros;
   testes) + `CursorView` (Presentation: quad no eye buffer, texturas por `shape_id`). Composição; Unity; APK; Quest.

## 3B-2 — Cursor no mundo
5. **Host `MouseRouter`** (Host.Domain, puro): estados Tela/Mundo/Edição/Fora do Quest, borda → destino, resistência
   40 px/300 ms, deltas acumulados, `CursorEnter`, queda da sessão; bateria de testes.
6. **Host `MouseHook`:** `WH_MOUSE_LL` em thread própria → `MouseRouter`; `SetCursorPos` na entrada; só com headset.
7. **Headset `WorldCursor`** (Application, puro): direção a partir do ponto de saída, delta → graus pela última tela,
   toque → `CursorEnter`, 80 ms; testes. `CursorView` no mundo (tamanho angular, halo). Simulator; Quest.

## 3B-3 — Modo edição
8. **`EditMode`** (Application) + `SetEditMode` ao host; `InteractionRouter` só manipula com a edição ligada; alças
   visíveis em todas as telas; selo "Modo edição"; testes.
9. **`MousePointer`** (`IManipulationInput`): raio cabeça → cursor 3D, botões/roda/`Shift`/`Esc`; testes.
10. **Atalhos e menu:** `Ctrl+Alt+E`, `Esc` só na edição, `Ctrl+Alt` segurado (temporário); item no menu
    (`MaxItems` → 17). Unity; APK; Simulator; Quest; relatório, STATUS, merge.
