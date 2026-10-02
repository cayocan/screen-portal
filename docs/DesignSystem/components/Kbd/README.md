# Kbd
Tecla ou combinação de teclas em `mono`, usada em toasts, no selo, nos menus e na folha de atalhos.

**O consumidor fornece:** a combinação como texto ("Ctrl+Alt+E"); para mouse, junte texto comum ("Shift + roda").

**Regras**
- `kbd` (`mono` 500, mesmo tamanho angular de `label`) sobre `panel-sunken`, contorno `edge`, `radius-sm`.
- Uma combinação = um `Kbd` ("Ctrl+Alt+E"), não uma tecla por caixa: menos ruído no eye buffer.
- Use os rótulos impressos nas teclas (Ctrl, Alt, Shift, Esc, Home, PgUp), não traduções.
- A folha de atalhos (Ajustes → Atalhos) é um painel `panel` com ação à esquerda em `body` e `Kbd` à direita, linhas de 3,5°.
