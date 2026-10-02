# AspectPicker
Subpáginas do menu para escolher orientação, proporção e resolução de um monitor virtual, em "+ Monitor virtual" e em "Recriar".

**O consumidor fornece:** o modo (criar ou recriar com o nome da tela), a orientação, as proporções disponíveis e, na segunda página, as resoluções da proporção escolhida com a atual marcada.

**Regras**
- Orientação é um par de botões (Paisagem/Retrato), o ligado em estado pressionado.
- Cada proporção é um botão de 3 por linha com o retângulo desenhado na proporção real (traço de 3 px em `currentColor`, maior lado 56 px = 3,5°), girado em retrato.
- Resoluções: uma por linha, com apelido em `caption` ("Full HD", "QHD", "4K · mais pesado"). Em "Recriar", a atual fica desabilitada com "atual" no fim.
- Título `title` diz a ação e o alvo: "+ Monitor virtual" ou "Recriar: DISPLAY3".
- Escolher a resolução fecha o menu e a tela nasce à frente; um toast confirma com "Desfazer".
