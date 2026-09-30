# Fase 2A — spike do driver: VDD

Data: 28/09/2026 16:52 · GPU: AMD Radeon RX 5500 XT · Windows 10.0.19045.0

| Teste | Resultado | Detalhes |
|---|---|---|
| 1. Resolução exata | ❌ | InvalidOperationException: Sequence contains no matching element |
| 2. Monitores simultâneos | ❌ | TimeoutException: The operation has timed out. |
| 3. Retrato nativo 1440×2560 | ❌ | TimeoutException: The operation has timed out. |
| 4. Recriar movendo janelas | N/A | o driver só remove o último monitor; recriar exigiria troca de modo ao vivo (evitada pelo brief §10.1) |
| 5. Monitor-palco com recorte | ❌ | TimeoutException: The operation has timed out. |
| 6. Limite do H.264 da GPU | ✅ | 4096×1152 ok; 5120×1440 FALHOU (SharpGenException); 7680×2160 FALHOU (SharpGenException); 1440×5120 FALHOU (SharpGenException) |

## O que aconteceu
O driver (WUDFHost do VDD) caiu 6 vezes entre 16:52:43 e 16:52:49 durante a sequência `SETDISPLAYCOUNT n` +
`RELOAD_DRIVER` pelo pipe `MTTVirtualDisplayPipe` (Visualizador de Eventos → Sistema, DriverFrameworks-UserMode
10110/10111). O Windows desistiu de reiniciar o dispositivo (`CM_PROB_FAILED_POST_START`); depois disso o pipe deixa de
aceitar conexões e os testes 2, 3 e 5 dão timeout. Versão: pacote winget 25.7.26 (MttVDD.inf 11.30.4.434), instalado
com `tools/install-vdd.ps1`. Análise e decisão: [ADR 0007](../adr/0007-virtual-display-driver.md).