# Fase 2E — Telas curvas — Design

**Data:** 30/09/2026 · **Aprovado por:** Cayo (brainstorming de 30/09/2026)
**Depende de:** 2B (superfície de layout, manipulação, menu de pulso) · 2C (telas com vídeo, layers Underlay —
[ADR 0010](../adr/0010-screens-as-underlay-layers.md)).
**Brief:** §10.2 (tela curva), §15.4 (limites de compositor layers). **Spike:** [fase2e-spike.md](../measurements/fase2e-spike.md).

## 1. Decisões

| # | Decisão |
|---|---|
| 1 | **Toda tela curva é uma layer cilíndrica** (`OVROverlay` Cylinder, Underlay, external surface). O spike mostrou 4 layers cilíndricas com vídeo, nítidas, no Quest 3 (OpenXR): o limite de "1 cilindro por cena" do brief §15.4 não vale. Saem do plano a `CylinderLayerPolicy`, o crossfade e a malha curva no eye buffer para o vídeo (ADR 0011) |
| 2 | Tela plana continua **layer quad**. Ligar ou desligar a curvatura recria a layer e o decoder (medido: 35–100 ms típicos, imperceptível). Mudar o valor de uma tela já curva só muda o raio, sem recriar |
| 3 | Regras do brief §10.2 mantidas: curvatura 0–100 %, curva só horizontal, raio = distância ÷ curvatura, largura e resolução nunca mudam ao curvar, rótulo "≈ NNNNR" |
| 4 | **Arco máximo de 120°:** acima disso a curvatura efetiva é limitada e o rótulo avisa ("arco limitado") |
| 5 | Controles nesta fase: **menu do pulso** (página "Curvatura" com os presets) e **alça de curvatura** no topo da moldura (controle e mãos). Atalhos de teclado e mouse ficam para a Fase 3 (cursor espacial) |
| 6 | Telas novas nascem planas (sem curvatura no seletor de criação) |

## 2. Escopo

**Dentro:** geometria da tela curva (domínio puro); layer cilíndrica para telas de vídeo; furo, quad de estado, tela de
teste e moldura curvos; hit-test do laser e das mãos na tela curva; página "Curvatura" no menu; alça de curvatura com
prévia; verificação no Simulator e no Quest.

**Fora:** atalhos `Ctrl+Alt+C` / `[` / `]` e mouse na moldura (Fase 3); curvatura no seletor "+ Monitor virtual";
ícone de "nitidez reduzida" (não existe mais: toda tela curva é nítida); curvatura vertical/esférica (fora do MVP);
curvatura no perfil (Fase 3b).

## 3. Domínio e Application (código puro)

Já existem desde a 1A: `VirtualScreen.Curvature` (0..1) e `LastNonZeroCurvature`, `SetScreenCurvature`,
`ToggleScreenCurvature`, `ScreenLimits.DefaultCurvature` (0,6).

- **`CurvaturePresets`**: Plana 0 · Suave 0,3 · Média 0,6 · Envolvente 1,0, com nome para o menu.
- **`CurvedGeometry`** (Domain/Layout):
  - distância d = `LayoutSurface.CurvatureDistance(placement)` (raio horizontal no Cilindro, distância na Esfera);
    tela solta: distância do centro da superfície à tela;
  - curvatura efetiva c' = min(c, 120° × d ÷ largura) (arco ≤ 120°), com flag `ArcLimited`;
  - raio R = d ÷ c' (c' = 0 → plana); arco = largura ÷ R;
  - centro do cilindro = posição da tela − (frente da tela × R), mesma rotação da tela;
  - rótulo: `"Média · 60 % · ≈ 1300R"` (R em mm arredondado a 100; preset pelo nome quando coincide, senão só o %),
    com " · arco limitado" quando `ArcLimited`.
- **Hit-test curvo** (`ScreenHitTest`): com c' > 0, o raio de mira é interceptado com o cilindro da tela (a parte
  interna, virada para o usuário). As coordenadas locais (x ao longo do arco, y na altura) são as mesmas da tela plana,
  então `Classify` (conteúdo, moldura, alças) não muda.
- **Menu do pulso:** item "Curvatura ▸" na página principal (apagado sem tela em foco) →
  página com os 4 presets e "← Voltar"; o preset atual fica marcado. Escolher envia `SetScreenCurvature` na tela em foco.
- **Alça de curvatura:** nova região de hit no meio do topo da moldura. Arrastar move a curvatura pela distância da
  mão/raio ao longo da normal da tela: 30 cm para frente = +100 %, para trás = −100 %, limitado a 0..1. Durante o arraste
  a curvatura é prévia local (sem Command) e o rótulo mostra o valor; ao soltar, um único `SetScreenCurvature`. O
  controlador de manipulação segue o padrão da 2B (prévia → um Command ao soltar).

## 4. Renderização (Presentation)

- **`VideoScreenView`:** forma pela curvatura efetiva. c' = 0 → layer Quad (como hoje). c' > 0 → layer Cylinder com o
  transform no centro do cilindro e escala (largura, altura, R). Mudança 0 ↔ > 0 → reconstrói a layer como na troca de
  resolução (o decoder reabre na Surface nova e pede IDR). Mudança de valor com c' > 0 → só transform.
- **`CurvedQuadMesh`:** malha de um arco (N segmentos proporcional ao arco, mínimo 2, ~1 a cada 3°), gerada só quando
  largura, altura ou raio mudam, sem alocação por frame (buffers reaproveitados). Usada pelo furo (ADR 0010), pelo quad
  de estado, pela `TestPatternScreenView` e pela moldura/alças do `ManipulationPresenter`.
- O texto de estado continua plano, no centro da tela, 3 mm à frente do arco.

## 5. Testes e verificação

**Automáticos:**
- Harness `dotnet test`: `CurvedGeometry` (raio, arco, limite de 120°, centro do cilindro, rótulo), presets, hit-test
  curvo (centro, bordas, moldura, alça, raio que passa ao lado), página "Curvatura", alça (prévia sem Command, um
  Command ao soltar, undo volta ao valor anterior), invariante: curvar não muda resolução nem largura.
- Unity EditMode: `CurvedQuadMesh` (vértices no arco, contagem de segmentos, sem alocação ao regenerar com o mesmo
  tamanho).

**Meta XR Simulator** (Operator, como na 2B): página "Curvatura" e presets; alça com controle e com mãos; moldura e
alças acompanham a curva; laser acerta a tela curva nas bordas; undo.

**Quest** (só o que depende dele): nitidez do texto em Suave/Média/Envolvente; troca plana ↔ curva sem piscar; 4 telas
curvas a 60 fps; UI por cima da tela curva (furo curvo).

**Documentação:** ADR 0011 (toda tela curva é layer cilíndrica); brief §10.2 e §15.4 corrigidos (limite de 1 cilindro
não vale no OpenXR do Quest 3; `CylinderLayerPolicy` sai); relatório `docs/measurements/fase2e-*.md`; STATUS.
