# HostWindow
A janela do Screen Portal no Windows, aberta pela bandeja: status da conexão, telas ativas, óculos pareados e atalhos. Substitui o console e a edição manual de `hotkeys.json`, `scales.json` e `paired.json`.

**O consumidor fornece:** o estado da conexão (óculos, rede, latência), a lista de telas (nome, tipo, resolução, taxa, estado), os óculos pareados e os atalhos.

**Escala:** o host vive em pixels de tela, não em graus: use os estilos `host-*` (corpo de 14 px) e as mesmas cores do óculos. Janela em `host-surface`, cartões em `panel`, contornos `edge` de 1 px, abas com sublinhado `portal`.

**Abas**
- **Telas:** uma linha por tela com ponto de estado (`ok` ao vivo, `warn` limitada, `ink-faint` parada), nome em `host-label`, "tipo · resolução · taxa" em `host-caption` e "Ajustar" (escala do monitor, hoje em `scales.json`). Criar e posicionar continua sendo feito no óculos, e a janela diz isso.
- **Óculos:** óculos pareados (hoje em `paired.json`) com "Esquecer"; "Parear novo óculos".
- **Atalhos:** cada ação com seu `Kbd`; clicar grava um novo atalho (hoje em `hotkeys.json`).
- **Sobre:** versão do host e do app; "Versões incompatíveis" em `warn` quando for o caso.

**Regras**
- Fechar a janela esconde na bandeja; sair é pelo menu da bandeja.
- Os textos espelham os do óculos (no PC, "Procurando seu PC…" vira "Esperando o óculos…").
- Só tema escuro por enquanto; um tema claro para o Windows em modo claro não está definido.
