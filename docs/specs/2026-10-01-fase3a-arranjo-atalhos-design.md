# Fase 3A — Arranjo do Windows segue o Quest + escala por monitor + atalhos globais — Design

**Data:** 01/10/2026 · **Aprovado por:** Cayo (brainstorming de 01/10/2026)
**Brief:** §10.6–10.11 (cursor espacial; o `DisplayTopologySync` era o plano B), §10.9 (atalhos). Fase 3 dividida em
3A (esta), 3B (cursor espacial + cursor desenhado no headset), 3C (mouse-first: moldura, cursor 3D, menu pelo mouse),
3D (janela destacada + arrastar janela para o vazio).

## 1. Decisões
| # | Decisão |
|---|---|
| 1 | O **arranjo do Windows segue a disposição das telas no Quest**: o mouse atravessa as telas como o usuário as vê, com o movimento nativo do Windows |
| 2 | Menu: **"Arranjo automático: ligado/desligado"** (princípio Liberdade); começa ligado |
| 3 | A **escala de cada monitor virtual** (Windows) é guardada pelo host por headset e tela e reaplicada quando o monitor é recriado |
| 4 | **Atalhos globais** no host (`RegisterHotKey`) → ação → Command no headset (com undo). **Tela focada = a do cursor do mouse** |
| 5 | Atalhos nesta subfase: mover `Ctrl+Alt+setas` (5°) · redimensionar `Ctrl+Alt+Shift+↑/↓` (±10 %) · aproximar/afastar `Ctrl+Alt+PgUp/PgDn` (±10 cm) · curvatura `Ctrl+Alt+C` / `[` / `]` (±10 %) · fps `Ctrl+Alt+F` (Auto → Vídeo → Eco → Fixo 30) · desfazer/refazer `Ctrl+Alt+Z/Y` · recentrar `Ctrl+Alt+R` · Cilindro ↔ Esfera `Ctrl+Alt+L` · alinhar à postura `Ctrl+Alt+T` · grade `Ctrl+Alt+G` |
| 6 | Atalhos em `%APPDATA%\ScreenPortal\hotkeys.json` (gerado com os padrões); atalho ocupado → log com a sugestão de trocar |

## 2. Arranjo (Host.Domain, puro: `DisplayArrangement`)
Entrada: as telas com monitor (chave, tamanho em px, ângulo do centro e meio-tamanho angular na superfície) + os
monitores do Windows (nome, retângulo, se é o principal) + a correspondência chave → monitor.
- O **principal fica em 0,0**. Se ele aparece no Quest (espelho), é a âncora; senão a âncora é a tela mais central,
  posta à direita do principal.
- Cada tela seguinte (a mais perto, em ângulo, de uma já posta) encosta nessa vizinha do lado onde está no Quest
  (horizontal se o desnível horizontal, em meios-tamanhos, é maior que o vertical); no outro eixo alinha pelo desnível
  angular convertido em pixels da vizinha, limitado para continuar encostando.
- Sobreposição com um monitor já posto (inclusive físicos fora do Quest, que não se movem) → empurra para além dele no
  mesmo sentido. Sem vãos: toda tela encosta em outra.
- O host aplica num único `SetDisplayConfig` só se algo mudou (cada monitor movido tem a captura recriada, ~1 s).

## 3. Protocolo
- `LayoutChanged { screens:[ScreenAngles] }` (headset → host), `ScreenAngles { screen_key; azimuth_deg; elevation_deg;
  half_width_deg; half_height_deg }`: só com o arranjo automático ligado, 0,5 s depois da última mudança, e no Welcome.
  Telas soltas (fora da superfície) ficam de fora (o host as mantém onde estão).
- `Hotkey { action:ubyte; screen_key:uint; }` (host → headset); `screen_key` 0 = sem tela sob o cursor.

## 4. Escala
`ScaleKeeper` (host): a cada 3 s lê a escala de cada monitor virtual vivo (`DisplayScale`, API não documentada —
testada com SudoVDA: lê e grava) e guarda em `%APPDATA%\ScreenPortal\scales.json` quando muda; ao criar/recriar o
monitor de uma chave com escala guardada, aplica (se a escala não for válida para a resolução nova, ignora).

## 5. Testes
`DisplayArrangement` (direita, esquerda, cima, baixo, diagonal, três em linha, tamanhos diferentes, principal fora do
Quest, físico fora do Quest como obstáculo, sem sobreposição e sem vão); codec das mensagens; `LayoutSync` (debounce,
toggle, tela solta fora); `HotkeyDispatcher` (cada ação → Command certo, tela 0 → só ações globais); host: sessão
encaminha `LayoutChanged`, `HotkeyMap` (texto ↔ teclas).
**Quest:** mover telas → o mouse atravessa como no Quest; desligar o arranjo; escala volta ao reabrir; atalhos.
