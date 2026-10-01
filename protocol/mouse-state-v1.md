# MouseState v1 (UDP, host → headset) — Fase 3B

Um datagrama de **44 bytes**, little-endian, com o estado **completo** do mouse (perder um pacote não estraga nada).
Destino: a porta pedida pelo headset em `StartCursor` (controle). Enviado a cada mudança (até 240 Hz) e, nos modos
Mundo/Edição, ao menos a cada 50 ms. Código: `Infrastructure/Shared/Protocol/MouseStateCodec.cs` (host e headset).

| Offset | Tipo | Campo | |
|---|---|---|---|
| 0 | 4 bytes | magic | `SPMS` |
| 4 | u32 | seq | cresce a cada pacote; o headset descarta seq ≤ último |
| 8 | u8 | mode | 0 Tela · 1 Mundo · 2 Edição · 3 Fora do Quest |
| 9 | u8 | buttons | bit 0 esquerdo · bit 1 direito · bit 2 meio |
| 10 | u8 | flags | bit 0 `Shift` · bit 1 `Ctrl+Alt` segurados |
| 11 | u8 | reservado | 0 |
| 12 | u32 | epoch | muda a cada troca de modo (uma saída nova é uma epoch nova) |
| 16 | u32 | screen_key | Tela: tela do cursor · Mundo/Edição: tela de onde saiu (0 = nenhuma) |
| 20 | i32 | x | pixels da tela (Tela: posição; Mundo/Edição: ponto de saída) |
| 24 | i32 | y | |
| 28 | u32 | shape_id | forma atual (`CursorShape` no controle) |
| 32 | i32 | dx | Mundo/Edição: soma dos deltas desde o início da epoch (pixels do desktop, com a aceleração do Windows) |
| 36 | i32 | dy | |
| 40 | i32 | wheel | soma da roda desde o início da epoch (unidades de 120 = um clique) |
