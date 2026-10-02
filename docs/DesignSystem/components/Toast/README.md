# Toast
Mensagem curta no HUD que confirma uma ação, um atalho ou um gesto, ou avisa de desempenho; some sozinha.

**O consumidor fornece:** o texto (até ~40 caracteres), um ícone opcional, o tom (`neutro`, `ok`, `warn`, `danger`) e, opcionalmente, uma ação ("Desfazer", "Refazer").

**Quando usar**
- Desfazer/refazer: "Desfeito: <ação>" com "Refazer".
- Atalho de teclado acionado: nome da ação + o `Kbd` do atalho (ensina o atalho).
- Gesto de tchau: "Mãos desligadas · acene para ligar".
- Remoção: "Tela removida" com "Desfazer".
- Desempenho: "Taxa alta: pode travar" em `warn`.

**Regras**
- `panel` com contorno `edge` (ou `warn`/`danger` no tom), altura `target`, `radius-md`. Ícone colorido + palavra; o texto fica `ink`.
- A `dist-hud`, logo acima do selo do modo edição; no máximo 2 empilhados, o mais novo embaixo.
- 2 s (3 s com ação), saída em 180 ms. A ação é clicável com cursor e laser.
- Nunca para erros que pedem decisão: esses vão para o painel de status.
