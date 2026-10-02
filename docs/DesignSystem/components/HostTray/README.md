# HostTray
O ícone do Screen Portal na bandeja do Windows e o menu do clique direito: estado de relance e as ações rápidas do host.

**O consumidor fornece:** o estado (conectado, esperando, instável, pausado), número de telas e latência, e se inicia com o Windows.

**Ícone**
- Um monitor em traço `ink` com um ponto de estado no canto: `ok` conectado, `portal` esperando o óculos, `warn` instável, `ink-faint` pausado. O ponto tem contorno da cor da barra de tarefas para não colar no monitor. Gere em 16, 20, 24 e 32 px.
- **Desenho provisório:** não há logo ainda; troque o monitor pela marca quando existir.

**Menu**
- Cabeçalho com ponto de estado, "Quest 3 conectado" em `host-label` e "3 telas · 4 ms" em `host-caption`.
- Itens: Abrir Screen Portal (também o clique simples no ícone) · Parear novo óculos… · Pausar o vídeo · Iniciar com o Windows (✓) · Sair.
- "Pausar o vídeo" é uma proposta (privacidade rápida: as telas no óculos mostram "Vídeo pausado no PC").
