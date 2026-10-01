# ADR 0013 — Texto da interface em TextMeshPro (SDF) e eye buffer em escala 1,0

- **Status:** Aceito (30/09/2026, pedido do Cayo no teste do Quest da 2E/2D)

## Contexto
No Quest, o texto dentro das telas (vídeo em compositor layer, resolução nativa) ficou muito legível, mas o da
interface (menu do pulso, rótulos, estados) ficou ruim de ler. Duas causas no eye buffer da Unity:
1. a URP do Android (`Mobile_RPAsset`) desenhava com **render scale 0,8** e esticava;
2. o texto era `TextMesh` legado: fonte bitmap rasterizada a 48–96 px e encolhida sem mipmaps (serrilhado e tremido).

## Decisão
1. `Mobile_RPAsset.m_RenderScale` = **1,0**. O eye buffer só desenha interface (o vídeo vai nas layers), então o custo
   de GPU é baixo; conferir 72 fps no Quest.
2. Todo texto da interface passa a ser **TextMeshPro 3D (SDF)** via `Presentation/UiText`. Vem no `com.unity.ugui` 2.0
   que o projeto já tem (sem pacote novo); os "TMP Essential Resources" foram importados em `Assets/TextMesh Pro`. O
   shader "TextMeshPro/Distance Field" tem stereo instancing, então o `StereoText` (shader próprio para o `TextMesh`)
   foi removido.
3. Tamanhos mantidos: `fontSize` do TMP = `fontSize × characterSize` do `TextMesh` (medido no Editor: 0,1117 m por
   unidade nos dois).

## Consequências
- Texto nítido em qualquer tamanho e distância; a mesma fonte (LiberationSans SDF) em toda a interface.
- Se ainda não bastar, o próximo passo é desenhar o menu numa compositor layer (como as telas).
