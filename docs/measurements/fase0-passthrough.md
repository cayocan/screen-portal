# Fase 0 — zona de passthrough (MRUK Keyboard Tracking + Passthrough Window)

Cena: `Assets/_Project/Spikes/Phase0/Scenes/Phase0_Passthrough.unity`
(menu **Screen Portal → Phase 0 → Boot scene: Passthrough** antes do build).
Linhas de log: `SPKB,detected|lost|removed,<segundos>` (`adb logcat -s Unity | findstr SPKB`).

Controles: thumbstick direito X = margem do lado do mouse, Y = margem da frente · A = voltar à sugestão ·
B = liga/desliga depth test da overlay de teste.

## Teclados testados ⏳
| Teclado (marca/modelo, layout, cor) | Iluminação | Detectou? | Tempo até detectar (s) | Perdas em 5 min | Sugestão aceitável sem ajuste? | Ajuste feito (cm) |
|---|---|---|---|---|---|---|
| | | | | | | |
| | | | | | | |
| | | | | | | |

Meta de aceite (brief §4.2): sugestão aceitável sem ajuste em **≥ 80 %** dos testes.

## Recuperação ⏳
- Ao cobrir/afastar o teclado: a zona congela na última posição? (sim/não)
- Ao reencontrar: volta sem teleporte perceptível? (a transição suave de 200 ms é da Fase 4c)

## Oclusão overlay × Passthrough Window ⏳
A quad xadrez (`OcclusionTestOverlay`) fica ~95 cm de altura, 60 cm à frente, cruzando a zona na visão.

| Modo | Overlay cobre o passthrough? | Observações |
|---|---|---|
| `noDepthBufferTesting = true` (padrão) | | |
| `noDepthBufferTesting = false` (B) | | |

## Conclusão
_Preencher: confirma D7? Margens padrão precisam mudar? Precisa da Fase 4d (câmera para o mouse)?_
