# CoachMark
Dica contextual que aparece ao lado da coisa que explica (uma tela, o menu, o cursor), no momento em que ela é útil.

**O consumidor fornece:** o alvo, o título (`heading`), uma frase com o atalho em `Kbd` e a posição na sequência ("Dica 1 de 3").

**Regras**
- Painel `panel` com contorno `portal` em `stroke-focus`; o alvo ganha a moldura de seleção.
- No máximo 3 dicas, nunca duas ao mesmo tempo, e nunca de novo depois de dispensada. Dispensar com "Entendi", `Esc`, ou fazendo a ação ensinada.
- Fica ao lado do alvo, nunca por cima do vídeo de uma tela.
