# Fase 2E — spike de telas curvas (30/09/2026)

**Pergunta:** o limite de "1 layer cilíndrica por cena" do brief §15.4 (documentação do runtime Oculus) ainda vale no
Quest 3 com OpenXR? E quanto custa trocar uma tela de plana para curva com vídeo?

**Ambiente:** Quest 3, Horizon OS v83, Meta XR Core SDK 207 (OpenXR), APK Release. Host `stream` com 2560×1080 a 60 fps,
20 Mbps por stream, Wi-Fi. Cena de vídeo da Fase 0 com `--ei cylinder 1` (N layers cilíndricas lado a lado, centro na
cabeça, raio = distância) e `--ei toggle 5` (a tela 0 alterna quad ↔ cilindro a cada 5 s, recriando a layer e a sessão
nativa). `tools/phase0-streams-test.ps1`.

## 1. Quatro layers cilíndricas com vídeo
| Tela | Decode | Latência média | Perdas |
|---|---|---|---|
| 0–3 | 59,9–60,0 fps (mín 57,2) | 15–17 ms | 0 rede, 0 decoder |

App a 72,5 fps. **Visto no óculos (Cayo): as 4 telas curvas aparecem, com o texto nítido.** O limite de 1 cilindro
**não vale** no OpenXR do Quest 3. (Os mínimos de 57,2 fps são o jitter do Wi-Fi já conhecido, pendência da 2F.)

## 2. Troca plana ↔ curva recriando a layer e o decoder
A cada troca, a tela 0 perde tipicamente 2–6 quadros (35–100 ms; decode no segundo da troca entre 53 e 58 fps). No
pior caso foram 15 quadros (~250 ms; 44,7 fps), e 13 pacotes se perderam na religação do socket, somando todas as
trocas. **No óculos: imperceptível.** A tela 1 não é afetada.

## Conclusão
- Toda tela curva pode ser uma layer cilíndrica nítida: não precisa de `CylinderLayerPolicy`, crossfade nem malha curva
  no eye buffer (brief §10.2 e §15.4 ficam simplificados; ADR 0011).
- Ligar ou desligar a curvatura pode recriar a layer e o decoder; não precisa de `MediaCodec.setOutputSurface`.
- Mudar o valor da curvatura (tela já curva) só muda o raio da layer (escala do transform), sem recriar nada.
