# ADR 0018 — Janela destacada é um espelho (WGC + popups por PrintWindow), não um monitor-palco

- **Status:** Aceito (Fase 3D, 02/10/2026, aprovado pelo Cayo)
- **Spec:** docs/specs/2026-10-02-fase3d-janela-espelhada-design.md · **Spike:** docs/measurements/fase3d-spike-popups.md

## Contexto
O brief (§10.5) previa um monitor virtual "palco" por janela destacada. O Cayo pediu algo mais leve, sem monitor
virtual, mas com menus e diálogos funcionando (um conhecido tem isso funcionando). No Windows 10 o WGC só captura
janelas "de Alt+Tab": menus (`#32768`) e diálogos (`#32770`) são recusados. O Windows 11 24H2 resolve com
`IncludeSecondaryWindows`, mas o PC de desenvolvimento é Windows 10 (19045).

## Decisão
1. Destacar = **espelhar** a janela: WGC da janela + popups do mesmo processo detectados por `SetWinEventHook` e
   capturados por `PrintWindow(PW_RENDERFULLCONTENT)` (spike: menu 393×329 e diálogo 618×195 com imagem, 16 ms
   cada), compostos no host num único quadro do tamanho da janela (popups cortados na borda), um stream.
2. A janela não muda de monitor; entrar no espelho com o cursor traz a janela para a frente.
3. No Windows 11 24H2+ usar `IncludeSecondaryWindows` no lugar do `PrintWindow`.

## Consequências
- Sem monitor virtual por janela: nada de arranjo mexido, criação instantânea, sem limite de monitores do driver.
- Popups custam CPU (pequenos, ~30 fps enquanto visíveis) e aparecem ~0,1 s depois de abrir.
- Popup que vaza a borda da janela é cortado; se a janela estiver numa tela do Quest, aparece duas vezes.
- Cliques dependem de a janela estar na frente no Windows (o host a traz ao entrar no espelho).
