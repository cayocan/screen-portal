# Fase 3D — Janela espelhada ("destacar" sem monitor virtual) — Design

**Data:** 02/10/2026 · **Aprovado por:** Cayo · **ADR:** [0018](../adr/0018-window-mirror-instead-of-stage-monitor.md)
**Brief:** §10.5 (revisto: espelho no lugar do monitor-palco) · **Spike:** [fase3d-spike-popups](../measurements/fase3d-spike-popups.md)

## 1. Objetivo
Qualquer janela do Windows vira uma tela própria no Quest **sem criar monitor virtual**, com menus, botão direito,
dropdowns, tooltips e diálogos funcionando.

| # | Requisito |
|---|---|
| 1 | `Ctrl+Alt+D` destaca a janela ativa: uma tela nova no Quest, do tamanho da janela, à frente da visão |
| 2 | Menus/dropdowns/tooltips/diálogos do app aparecem na tela, na posição certa |
| 3 | Clique, digitação, scroll e arrastar dentro do espelho funcionam como na janela real |
| 4 | `Ctrl+Alt+Shift+D` (cursor sobre o espelho) ou "Reanexar janela" no menu desfaz: a janela volta ao lugar de antes |
| 5 | Arrastar uma janela pela barra de título para fora de uma tela, para o vazio, a destaca ali (2ª etapa) |

## 2. Captura (host)
- **Janela:** WGC por janela (GPU; funciona coberta). Retângulo = `DWMWA_EXTENDED_FRAME_BOUNDS`.
- **Popups do mesmo processo** (menus `#32768`, dropdowns, tooltips, diálogos — janelas possuídas que o WGC do
  Windows 10 recusa): detectados por `SetWinEventHook` (mostrar/esconder/mover, < 50 ms) e capturados com
  `PrintWindow(PW_RENDERFULLCONTENT)` (~16 ms cada, CPU; são pequenos) a ~30 fps enquanto visíveis.
- **Composição:** um quadro (canvas) do tamanho da janela no destaque: a janela e, por cima, cada popup na posição
  relativa à janela, **cortado na borda** da janela. Um stream como o de qualquer tela (o vídeo continua sem passar
  pelo C# do headset). Com popup aberto, o quadro é refeito mesmo sem quadro novo da janela.
- **Windows 11 24H2+:** `IncludeSecondaryWindows` do WGC faz o mesmo nativamente; o host usa quando existir.
- O tamanho do canvas é o da janela no destaque; se a janela mudar de tamanho depois, o conteúdo é cortado/sobra
  borda até reanexar e destacar de novo (redimensionar no headset é zoom, regra 10.1).

## 3. Onde a janela mora no Windows
Não se move de monitor: fica onde está (precisa estar num monitor para receber cliques). Ao entrar com o cursor no
espelho, o host **traz a janela para a frente** e põe o cursor real no ponto correspondente. Se a janela estiver numa
tela do Quest, ela aparece lá e no espelho (o usuário escolhe onde deixá-la). Minimizada = sem imagem (o host a
restaura no destaque).

## 4. Cursor e roteador
O espelho é uma região própria para o roteador do mouse (antes dos monitores): dentro do retângulo da janela =
`Tela` daquele espelho; sair do retângulo = **Mundo** (mesmo que o Windows tenha monitor ali), exceto para outra
tela do Quest. `CursorEnter` num espelho: traz a janela para a frente + `SetCursorPos` no ponto. A forma do cursor e
a posição vêm como hoje (posição relativa à janela).

## 5. Protocolo e headset
- `ScreenKind.Window = 2`; `AcquireScreen` ganha `window_id:uint` (fim da tabela).
- `DetachWindow { window_id:uint; width:uint; height:uint; title:string; }` (host → headset).
- `ScreenSource.Window(windowId)`; a tela nasce à frente da visão; menu "Reanexar janela" no lugar de "Remover tela".
- `HotkeyAction.DetachWindow` (só host) e `ReattachWindow` (headset remove a tela-espelho sob o cursor).
- Sessão salva: espelhos **não** voltam ao reabrir (a janela pode não existir); perfis por app na Fase 3b.
- Janela fechada pelo app → o host avisa (`ScreenFailed`) e o headset remove a tela.

## 6. Ordem e testes
1. Protocolo + `ScreenSource.Window`; 2. `PopupTracker` + `WindowMirrorCapture` (composição) no host; 3. destacar e
reanexar (atalhos, sessão, menu); 4. roteador/cursor com espelhos; 5. arrastar para o vazio.
Testes puros: composição (posição/corte dos popups), roteador com espelho, `HostSync` com `Window`, codec. Quest: menus
do Bloco de Notas/navegador, clique e digitação, reanexar.
