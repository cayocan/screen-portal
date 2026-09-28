# Canal de controle — v1

Schema: `protocol/control.fbs` (FlatBuffers 25.2.10). C# gerado: `Assets/_Project/Infrastructure/Shared/Protocol/Generated/ControlProtocol.g.cs`
(`tools/gen-protocol.ps1`). Codec único: `ControlMessageCodec.cs` (usado pela Unity e pelo host via `host/Host.Shared`).

- **TCP 9200.** Frame: `[u32 little-endian tamanho][FlatBuffer Envelope]`, máximo 64 KiB. Tamanho inválido encerra a conexão.
- **mDNS:** `_screenportal._tcp.local`, porta 9200, TXT `v=1`; nome da instância = nome do PC.
- **Envelope:** `protocol_version` (1) + `body` (union); `file_identifier` `SPCT` (bytes 4–7 do buffer). Versão diferente → `Bye(IncompatibleVersion)`.
- **Evolução (brief §8.9):** campos novos só no fim das tabelas e opcionais; nunca reordenar nem remover.

| Mensagem | Direção | Campos |
|---|---|---|
| Hello | headset → host | device_id, device_name, pairing_token (vazio na 1ª vez) |
| PairingRequired | host → headset | code (6 dígitos) |
| PairingResult | host → headset | accepted, pairing_token |
| Welcome | host → headset | host_name, monitors[id, width, height, name, is_primary] |
| StartStream | headset → host | monitor_id, udp_port, stream_id |
| StreamStarted / StreamStopped | host → headset | stream_id, width/height ou reason |
| Ping / Pong | ambos | sent_us (ecoado) |
| Bye | ambos | reason: Shutdown, IncompatibleVersion, PairingRejected, Replaced |

**Handshake:** Hello → (token válido) Welcome · (sem token) PairingRequired → diálogo no PC → PairingResult → Welcome.
Depois do Welcome: o headset prepara o decoder e envia StartStream; o host transmite (protocol/video-packet-v1.md) para o
IP de origem da conexão e responde StreamStarted. Ping a cada 500 ms (headset); o host responde Pong.
