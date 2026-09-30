# ADR 0010 — Telas como compositor layers Underlay, reveladas por um furo no eye buffer

- **Status:** Aceito (Fase 2C, 30/09/2026, pedido do Cayo no teste do Quest)
- **Spec:** docs/specs/2026-09-30-fase2c-varias-telas-design.md §5 · **Relacionado:** ADR 0001 (vídeo nativo em layer)

## Contexto
Desde a Fase 0 cada tela é uma `OVROverlay` do tipo **Overlay**: o compositor a desenha por cima do eye buffer. No
Quest, a moldura, as alças, o laser do controle e o menu do pulso ficavam por baixo das telas sempre que se cruzavam.
Isso quebra a manipulação: o laser some ao apontar para a tela, e o menu some quando a mão passa na frente dela. O
Cayo pediu: toda interface fica por cima das telas.

## Decisão
1. **A layer de cada tela é Underlay:** o compositor a desenha atrás do eye buffer.
2. **Um furo no eye buffer revela a tela:** um quad do tamanho da tela, no mesmo plano, com o shader
   `ScreenPortal/ScreenHole` (fila Geometry, sem blend), escreve cor e alpha 0 e profundidade. O compositor mostra a
   layer onde o alpha do eye buffer é 0.
3. **A interface cobre a tela pela profundidade:** o que é desenhado na frente do furo tem alpha 1 e fica por cima. O
   que estiver atrás da tela falha no teste de profundidade e continua escondido, como numa tela opaca.
4. **O furo só existe com a layer visível.** Enquanto a tela mostra um estado ("Criando monitor…", falha), a layer fica
   escondida e o quad de estado ocupa o lugar dela no eye buffer.

## Consequências
- O eye buffer precisa chegar ao compositor com alpha. No Android a URP está sem HDR e sem pós-processamento; se um dia
  entrar pós-processamento, ele precisa preservar o alpha.
- **Telas sobrepostas:** entre underlays o compositor ordena pela `compositionDepth`, não pela distância. Com duas telas
  sobrepostas, a de trás pode aparecer na área da sobreposição. Isso é aceito na 2C, porque sobrepor telas é raro; se
  incomodar, a view ordena a `compositionDepth` pela distância à cabeça.
- As bordas da moldura ficam no plano da tela e podem ter z-fighting com o furo. Isso é conferido no Quest; se
  aparecer, a moldura sai 1 mm para a frente, como as alças.
- O furo é um draw call opaco a mais por tela, desprezível perto do vídeo.
