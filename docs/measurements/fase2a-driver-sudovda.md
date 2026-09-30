# Fase 2A — spike do driver: SudoVDA

Data: 28/09/2026 15:56 · GPU: AMD Radeon RX 5500 XT · Windows 10.0.19045.0

| Teste | Resultado | Detalhes |
|---|---|---|
| 1. Resolução exata | ✅ | 2560×1440: exato, cria 250 ms, remove 247 ms; 3440×1440: exato, cria 565 ms, remove 207 ms; 1920×1080: exato, cria 494 ms, remove 208 ms |
| 2. Monitores simultâneos | ✅ | 8 simultâneos (chegou a 8) |
| 3. Retrato nativo 1440×2560 | ✅ | captura 1440×2560; stream stream/codec_name=h264/width=1440/height=2560 |
| 4. Recriar movendo janelas | ✅ | janela no monitor novo: True; centro relativo esperado (0,50, 0,50), real (0,50, 0,50); tamanho 960×540 → 960×540 |
| 5. Monitor-palco com recorte | ✅ | recorte da janela 780×590 → 1180×890 após redimensionar |
| 6. Limite do H.264 da GPU | ✅ | 4096×1152 ok; 5120×1440 FALHOU (SharpGenException); 7680×2160 FALHOU (SharpGenException); 1440×5120 FALHOU (SharpGenException) |
