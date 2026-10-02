# ScreenFrame
A moldura em volta de uma tela (monitor) no modo edição: seleção, alças de canto, alça de curvatura, rótulo e guias de encaixe. Fica sempre **fora** do vídeo.

**O consumidor fornece:** o retângulo (ou arco) da tela, o nome e a resolução, se está selecionada, qual parte está sob o ponteiro ou em uso, e as guias de encaixe ativas.

**Regras**
- Tamanhos em graus, não metros: alças de canto `handle-corner` (2,5°), alça de curvatura 7,5° × 2°, moldura afastada 0,5° da borda. Antes eram 6 cm/10 cm fixos e encolhiam com a distância.
- Seleção: moldura `portal` em `stroke-focus` com `halo` por fora. Fora do modo edição, a tela em foco mostra só os quatro cantos (1,5° cada) em `portal`, para o foco nunca ser invisível.
- No modo edição, as alças aparecem em todas as telas a 35 % de opacidade de traço (sempre descobríveis) e em cheio na selecionada ou sob o ponteiro.
- Parte em uso (alça arrastada) em `edit` cheio. Guias de encaixe: linha tracejada `edit` em `stroke-world` com `halo`, cruzando as duas telas alinhadas, e um toast "Encaixado · Shift solta".
- Rótulo acima da tela, alinhado à esquerda: nome em `label` + resolução em `caption`, sobre `panel`. Ao curvar, o rótulo de curvatura ("Média · 60 % · ≈ 1300R") aparece centrado acima da alça.
- Tela curva: moldura e guias seguem o arco; o rótulo fica plano, virado para a cabeça.
