# Canal de controle — v2 (Fase 2C)

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

## v2 (Fase 2C) — telas sob comando do headset

`protocol_version` = 2 (v1 e v2 não conversam: `Bye(IncompatibleVersion)`). A chave de uma tela é o `ScreenId` da
workspace (`screen_key`), nunca reutilizado; undo restaura o mesmo id e, portanto, o mesmo monitor.

| Mensagem | Sentido | Campos | Resposta |
|---|---|---|---|
| `AcquireScreen` | headset → host | `screen_key, kind (Virtual/Mirror), width, height, physical_monitor_id` | `ScreenReady{screen_key,width,height}` ou `ScreenFailed{screen_key,reason}` |
| `StartStream` | headset → host | `screen_key, udp_port, stream_id` (`monitor_id` = 0, legado) | `StreamStarted` / `StreamStopped` |
| `ReleaseScreen` | headset → host | `screen_key, grace_ms` | — (para o stream; remove o monitor após o prazo; `AcquireScreen` da mesma chave antes disso reata) |
| `Welcome.leases` | host → headset | `[ScreenLease{screen_key, kind, width, height}]` | aluguéis ainda vivos deste headset |

Desconexão: o host aplica 5 min (padrão) a todos os aluguéis do headset. Espelho nunca remove nada no Windows: expirar só
encerra o stream. Portas de vídeo do headset: 9000 + `stream_id`. Detalhes: ADR 0009.

## Verificação

O codec (`ControlMessageCodec.Decode`) verifica o envelope e depois despacha o verificador do corpo pelo `BodyType`, como
workaround de um bug do runtime Google.FlatBuffers 25.2.10 (`Verifier.VerifyUnion` passa o type id errado). Reavaliar ao
atualizar o pacote Google.FlatBuffers.

## SetStreamRate (Fase 2F)
Headset → host: `SetStreamRate { screen_key, fps, kbps }` muda fps e bitrate de uma tela com stream, ao vivo (sem
recriar o pipeline nem pedir IDR). O host limita fps a 1–60 e kbps a 1 000–50 000. O headset manda só quando o valor
muda e de novo depois que o stream da tela recomeça (um pipeline novo começa no padrão: 60 fps, 20 Mbps).
