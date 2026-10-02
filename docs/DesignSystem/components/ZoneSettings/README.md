# ZoneSettings
Subpágina do menu (Espaço → "Zona da mesa ▸") com formato, borda suave, escurecimento do mundo, altura e revelar ao olhar para baixo; e o aviso de âncora perdida.

**O consumidor fornece:** os valores atuais da zona; para o aviso, o estado da âncora.

**Controles**
- Formato: segmentado (`sp-seg`) Retângulo · Arredondada · Elipse (rótulo curto "Arredond." quando não couber).
- Borda suave (0–10 cm, padrão 4) e Escurecer o mundo (50–100 %, padrão 80): sliders com valor à direita do título. Respondem a laser, pinça e roda do mouse (um passo por clique, `Shift` dez).
- Revelar ao olhar para baixo: toggle.
- Altura: ajuste pela roda ("72 cm"), com o plano visível ao vivo.
- Remover zona: destrutivo, desfazível pelo toast.

**Aviso de âncora perdida** (`ConnectionStatus` em `warn`): "Perdemos a posição da mesa" + "Reposicionar ▸" (refaz os dois cantos) ou "Mundo claro" (tira o escurecimento para o usuário não ficar sem ver a mesa).

**Atalho proposto:** `Ctrl+Alt+P` liga/desliga a zona (precisa entrar em `hotkeys.json`).
