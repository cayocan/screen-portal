# Video packet wire format — v1 (Fase 0)

Formato binário fixo dos pacotes UDP de vídeo. Não usa FlatBuffers de propósito: é o caminho
quente (até ~2 000 pacotes/s por tela) e o header precisa ser lido sem alocação no host (C#) e
no headset (C++). O canal de **controle** (Fase 1) usa FlatBuffers (`protocol/*.fbs`).

Implementações que DEVEM ficar em sincronia com este documento:
- `host/Host.Domain/Video/VideoPacketHeader.cs`
- `headset-native/screenportal/src/main/cpp/packet.h`

## Transporte
- UDP, um fluxo por tela. Porta base configurável (padrão **9000**); a tela `n` usa `9000 + n`.
- Payload máximo por pacote: **1 400 bytes** (+ 24 de header = 1 424, cabe no MTU 1500 com IP/UDP).
- Sem retransmissão na v1. Frame com fragmento faltando é descartado; o receptor pede
  keyframe (a partir da Fase 1, pelo canal de controle). No spike o host força IDR a cada 1 s.

## Header (24 bytes, little-endian)

| Offset | Tipo | Campo | Descrição |
|---|---|---|---|
| 0 | u16 | `magic` | `0x5053` ("SP") |
| 2 | u8 | `version` | `1` |
| 3 | u8 | `flags` | bit0 = keyframe (IDR), bit1 = config (SPS/PPS incluídos) |
| 4 | u16 | `streamId` | Índice da tela |
| 6 | u16 | `fragIndex` | 0..fragCount-1 |
| 8 | u16 | `fragCount` | Nº de fragmentos do frame (≥ 1) |
| 10 | u16 | `payloadSize` | Bytes de payload neste pacote |
| 12 | u32 | `frameId` | Sequencial por stream, com wrap |
| 16 | u64 | `captureTimeUs` | Momento da captura no relógio do host (QPC, µs) |

Payload: fatia do access unit H.264 em **Annex-B** (start codes `00 00 00 01`). Cada fragmento,
exceto o último, tem exatamente 1 400 bytes. O receptor remonta por `(streamId, frameId)`.

## Sincronização de relógio (harness de latência)
Porta UDP **9100** no host. Request/response de 24 bytes, little-endian:

| Offset | Tipo | Campo |
|---|---|---|
| 0 | u32 | `magic` = `0x434B4C43` ("CLKC") |
| 4 | u32 | `seq` |
| 8 | u64 | `clientSendUs` (relógio do headset) |
| 16 | u64 | `hostUs` (preenchido pelo host na resposta; 0 no request) |

O headset calcula `offset = hostUs - (clientSend + clientRecv)/2` e escolhe a amostra de menor
RTT de uma janela. Latência captura→decodificado = `(agoraHeadset + offset) - captureTimeUs`.
