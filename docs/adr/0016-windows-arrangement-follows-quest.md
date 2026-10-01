# ADR 0016 — O arranjo do Windows segue o Quest; escala por tela; atalhos globais

- **Status:** Aceito (Fase 3A, 01/10/2026, aprovado pelo Cayo)
- **Spec:** docs/specs/2026-10-01-fase3a-arranjo-atalhos-design.md · **Verificação:** docs/measurements/fase3a-arranjo-atalhos.md

## Contexto
O brief previa o cursor espacial (`SpatialMap`) como caminho principal e o `DisplayTopologySync` como plano B. Na
prática, o Cayo quer que o mouse atravesse as bordas das telas como elas estão no Quest **também** no Windows, e que o
arranjo e a escala ("Alterar o tamanho do texto…") de cada monitor virtual sobrevivam a reabrir o app e reiniciar o PC.

## Decisão
1. **O arranjo do Windows segue o Quest** por padrão ("Arranjo automático", desligável no menu — princípio Liberdade).
   O headset manda os ângulos das telas com vídeo na superfície (`LayoutChanged`, 0,5 s após a última mudança e a cada
   Welcome); o host calcula um plano puro (`DisplayArrangement`: principal fixo, vizinho mais próximo em ângulo, colado
   na horizontal ou vertical, sem sobreposição) e aplica num único `SetDisplayConfig`. Telas soltas e monitores fora do
   Quest ficam onde estão.
2. **Escala por tela** via API **não documentada** (`DisplayConfigGetDeviceInfo/SetDeviceInfo`, tipos −3/−4, passos
   relativos à escala recomendada) — a mesma do painel Configurações; funciona com SudoVDA. Lida a cada 3 s, guardada
   em `%APPDATA%\ScreenPortal\scales.json` por headset e tela, reaplicada ao criar/recriar (com até 3 novas tentativas).
3. **Atalhos globais** no host (`RegisterHotKey`, `%APPDATA%\ScreenPortal\hotkeys.json`) → `Hotkey(action, screen_key)`
   para o headset dono da tela sob o cursor; o headset converte em Commands (undo/redo valem).
4. O cursor espacial (`SpatialMap`) continua no plano: Fase 3B.

## Consequências
- Uma API não documentada pode mudar numa atualização do Windows: falha vira log e a escala fica a do Windows.
- `Ctrl+Alt+tecla` equivale a AltGr em teclados ABNT2; atalhos que roubem caracteres se trocam no `hotkeys.json`.
- Protocolo de controle ganhou `LayoutChanged` e `Hotkey` (adicionados ao fim da union, compatível).
