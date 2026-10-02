# Spike 3D — popups de uma janela no Windows 10 (02/10/2026)

`ScreenPortalHost popup-spike` (Bloco de Notas; menu de contexto por `WM_CONTEXTMENU`, diálogo Localizar por
`WM_COMMAND`, sem mexer no mouse). PC: Windows 10 Pro 19045, RX 5500 XT.

| Pergunta | Resultado |
|---|---|
| Detectar os popups do app (`SetWinEventHook`, show/menu-start, por processo) | ✅ < 50 ms, classe `#32768` (menu) e `#32770` (diálogo) |
| WGC da janela principal | ✅ 1101×826 |
| WGC de menu / diálogo (`CreateForWindow`) | ❌ "Parâmetro incorreto" em 48 tentativas por 1,5 s: o Windows 10 só captura janelas de Alt+Tab |
| `PrintWindow(PW_RENDERFULLCONTENT)` do menu | ✅ 393×329, 16,0 ms por captura, 437/441 amostras com imagem |
| `PrintWindow` do diálogo | ✅ 618×195, 16,1 ms por captura, 420/462 amostras com imagem |

Conclusão: espelho com popups é viável no Windows 10 (WGC para a janela, `PrintWindow` para os popups). → ADR 0018.
