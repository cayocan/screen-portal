# ADR 0011 — Toda tela curva é uma layer cilíndrica

- **Status:** Aceito (Fase 2E, 30/09/2026, aprovado pelo Cayo no brainstorming)
- **Spec:** docs/specs/2026-09-30-fase2e-telas-curvas-design.md · **Medição:** docs/measurements/fase2e-spike.md

## Contexto
O brief (§10.2, §15.4) partia de "no máximo 1 compositor layer cilíndrica por cena", da documentação do runtime Oculus.
Daí vinha um desenho caro: a layer cilíndrica ia para a tela curva em foco (`CylinderLayerPolicy`), trocava de dono com
crossfade, e as outras telas curvas precisavam de malha curva no eye buffer. Isso exigia um caminho de vídeo novo
(decoder → textura na Unity) e deixava essas telas menos nítidas.

## Decisão
1. **Toda tela curva é uma layer cilíndrica** (`OVROverlay` Cylinder, Underlay, external surface); tela plana
   continua layer quad.
2. **Ligar ou desligar a curvatura recria a layer e o decoder.** Mudar o valor de uma tela já curva só muda o raio.
3. Saem a `CylinderLayerPolicy` (e o plano B "tela curva principal"), o crossfade e o modo "nitidez reduzida".

## Evidência
Spike no Quest 3 (Horizon OS v83, SDK 207, OpenXR): 4 layers cilíndricas com vídeo 2560×1080 a 60 fps, as 4 visíveis
e nítidas; a troca plana ↔ curva recriando a layer perde 35–100 ms típicos (250 ms no pior caso) e ficou
imperceptível.

## Consequências
- Toda tela curva tem nitidez de layer; nenhum vídeo passa pelo eye buffer (o quadro continua sem atravessar o C#).
- Cada tela curva ocupa uma das 15 layers, como as planas.
- Risco: uma atualização do Horizon OS pode voltar a limitar cilindros. O `VideoScreenView` escolhe a forma num ponto
  só; se isso acontecer, o fallback é mostrar a tela plana com aviso, sem mudar o domínio.
