# Gizmo
O manipulador da tela selecionada no modo edição: setas para mover em X/Y, anéis para girar, alça de profundidade para aproximar/afastar e quadrado central para escalar.

**O consumidor fornece:** a tela selecionada (centro e orientação) e qual parte está sob o ponteiro ou em uso.

**Partes**
| parte | forma | cor | ação |
|---|---|---|---|
| Mover X | haste + ponta triangular à direita | `axis-x` | desliza na horizontal da superfície |
| Mover Y | haste + ponta para cima | `axis-y` | desliza na vertical |
| Perto/longe | haste para baixo + pega com chevron | `axis-z` | arrastar para cima aproxima |
| Girar X/Y/Z | três anéis, X e Y desenhados como elipses (30 % de achatamento) | `axis-x`/`-y`/`-z` | gira em passos de 5° (`Shift` livre); girar solta da superfície |
| Escalar | quadrado central | `ink` | cima/direita cresce |
| sob o ponteiro / em uso | a parte inteira | `edit` | — |

**Regras**
- Tamanho constante em graus: setas de 8,5° do centro até a ponta, anéis com raio 9,4°. Traço `stroke-world`, sempre com `halo` (13 px de largura total na prévia) por baixo.
- Os anéis X e Y são desenhados como elipses voltadas um pouco para o usuário, nunca de perfil: a versão anterior ficava invisível de frente.
- Cada eixo tem forma própria além da cor (seta à direita, seta para cima, pega para baixo).
- Ao arrastar, mostre um rótulo `label` perto do cursor com o valor ("+15°", "1,40 m", "120 %").
