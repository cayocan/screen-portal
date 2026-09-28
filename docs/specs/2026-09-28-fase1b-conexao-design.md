# Fase 1B — Conexão PC ↔ headset (protocolo, pareamento, estados, mDNS) — Design

- **Status:** aprovado pelo Cayo em 28/09/2026 (brainstorming).
- **Brief:** §7 (arquitetura), §8.5 (state machine), §8.9 (versionamento), §4.2 (reconexão < 3 s), §1.1 ("Procurando seu PC…").
- **Documentação a produzir:** ADR 0006, `protocol/control-v1.md`, brief (§8.5 tempos, §12 risco), STATUS.

## 1. Objetivo e decisões

Ao final da 1B, abrir o app no Quest com o host rodando no PC **mostra a tela do PC** na tela Demo, sem IP fixo,
com status de conexão visível, pareamento na primeira vez e reconexão automática em < 3 s.

| # | Decisão |
|---|---|
| 1 | **Pareamento na primeira vez**: o headset mostra um código de 6 dígitos; o PC pergunta "Permitir/Negar" com o mesmo código; o par é lembrado. Sem criptografia no MVP (v1.1) |
| 2 | **Entrega visível**: status da conexão no headset + vídeo do monitor principal na tela Demo via o caminho da Fase 0 |
| 3 | **FlatBuffers**: `flatc` oficial com versão fixa em `tools/flatc/`; **código gerado commitado**; runtime via NuGet (host) e código-fonte num asmdef puro (Unity) |
| 4 | **mDNS**: host usa a API nativa `DnsServiceRegister` (Win10 1809+); headset usa `NsdManager` no `.aar` (com multicast lock) |
| 5 | **Lógica de conexão no headset em C#**: máquina de estados pura (Application) + `TcpControlChannel` (Infrastructure); `.aar` só com mDNS e vídeo |

## 2. Protocolo de controle v1

- **TCP porta 9200.** Frame: `[u32 little-endian: tamanho do payload][FlatBuffer]`. Tamanho máximo do frame: 64 KiB.
- **Serviço mDNS:** `_screenportal._tcp.local`, porta 9200, TXT `v=1` (versão maior do protocolo), `name=<nome do PC>`.
- **Mensagem raiz:** `Envelope { protocol_version: ushort; body: Message (union) }`.
- **Regra de evolução (§8.9):** campos novos sempre opcionais; versão **maior** diferente = incompatível.

| Mensagem | Direção | Campos |
|---|---|---|
| `Hello` | headset → host | `device_id: string` (GUID persistente), `device_name: string`, `pairing_token: string` (vazio na 1ª vez) |
| `PairingRequired` | host → headset | `code: string` (6 dígitos) |
| `PairingResult` | host → headset | `accepted: bool`, `pairing_token: string` |
| `Welcome` | host → headset | `host_name: string`, `monitors: [MonitorInfo]` (`id: uint`, `width: uint`, `height: uint`, `name: string`, `is_primary: bool`) |
| `StartStream` | headset → host | `monitor_id: uint`, `udp_port: ushort`, `stream_id: ushort` |
| `StreamStarted` | host → headset | `stream_id: ushort`, `width: uint`, `height: uint` |
| `StreamStopped` | host → headset | `stream_id: ushort`, `reason: string` |
| `Ping` / `Pong` | ambos | `sent_us: ulong` (ecoado no `Pong`) |
| `Bye` | ambos | `reason: ByeReason` (`Shutdown`, `IncompatibleVersion`, `PairingRejected`, `Replaced`) |

**Handshake:**
1. Headset conecta e envia `Hello`.
2. Token válido para o `device_id` → host envia `Welcome`.
3. Sem token ou token inválido → host envia `PairingRequired(code)` e abre o diálogo
   "Quest (código 482 913) quer ver sua tela — Permitir / Negar" (`MessageBox` no topo; ícone na bandeja fica na Fase 5).
   Permitir → `PairingResult(true, token)` + `Welcome`. Negar → `PairingResult(false)` + `Bye(PairingRejected)`.
4. Headset cria a tela Demo com a resolução real do monitor primário (do `Welcome`), prepara o decoder e envia
   `StartStream(monitor, porta, stream_id)`. Host inicia o `StreamPipeline` da Fase 0 para o **IP de origem da
   conexão TCP** e responde `StreamStarted`.
5. Versão maior diferente → `Bye(IncompatibleVersion)`; o headset mostra "Atualize o app ou o host".

**Persistência do pareamento:** host em `%APPDATA%\ScreenPortal\paired.json` (`device_id → token`, nome, data);
headset no armazenamento do app (`device_id` + token por host). Token: 32 bytes aleatórios em base64.

**Segurança (registrada como risco):** impede conexões casuais na rede; sem criptografia, `device_id`/token podem ser
capturados por alguém na mesma rede. Criptografia (TLS/DTLS) fica para a v1.1.

## 3. Máquina de estados (headset, Application, C# puro)

| Estado | Comportamento | Transições |
|---|---|---|
| `Idle` | início | → `Discovering` |
| `Discovering` | aguarda o `IHostDiscovery` (o último host conectado tem prioridade se reaparecer) | host encontrado → `Connecting` |
| `Connecting` | abre o TCP, timeout **2 s** | ok → `Negotiating`; falha → `Discovering` |
| `Negotiating` | envia `Hello`; aguarda `Welcome` ou `PairingRequired` (**sem timeout durante o pareamento**; timeout de 5 s fora dele) | `Welcome` + `StreamStarted` → `Streaming`; recusado/incompatível → `Discovering` com a mensagem |
| `Streaming` | `Ping` a cada **500 ms** | sem `Pong` há **1,5 s** → `Degraded`; socket caiu → `Reconnecting` |
| `Degraded` | "Conexão instável…" | `Pong` → `Streaming`; **3 s** sem resposta → `Reconnecting` |
| `Reconnecting` | reconecta **direto ao último IP** (sem mDNS), tentativa a cada **250 ms** | ok → `Negotiating`; **10 s** sem sucesso → `Discovering` |

- **Tempo injetado** (`IClock`) e eventos externos (`OnHostFound`, `OnConnected`, `OnMessage`, `OnDisconnected`, `Tick`).
  Saídas por portas (`IControlChannel.Connect/Send/Close`, `IVideoStreamSink.Start/Stop`).
- **Reconexão < 3 s:** pula mDNS; token já existe; host reaproveita o pipeline do mesmo `device_id` e força IDR.
- **Layout preservado:** a `Workspace` vive no headset. A conexão **só cria** a tela do monitor primário quando ela
  ainda não existe (1º `Welcome` da sessão do app); em reconexões e novos `Welcome` a tela existente é reutilizada
  (identificada pelo `monitor_id`, guardado no `Label` até existir um campo próprio na Fase 2). Se a resolução do
  monitor mudou entre sessões, a tela existente é mantida e o vídeo é escalado pelo decoder (recriar é ação explícita,
  §10.4). Nenhum outro campo da `Workspace` é tocado pela conexão. Durante a reconexão a tela mantém a última imagem.
- **`Bye(Shutdown)`** → `Discovering` com "Seu PC encerrou o Screen Portal".

**Status exibido (`ConnectionStatus` → `ConnectionStatusView`):** "Procurando seu PC…" · "Conectando a <PC>…" ·
"Confirme no PC: código 482 913" · "Conectado" (some após 2 s) · "Conexão instável…" · "Reconectando…" ·
"Recusado no PC" · "Atualize o app ou o host" · após 10 s em `Discovering` na 1ª abertura: "Instale o host no seu PC"
+ link (QR code na Fase 5).

## 4. Componentes

**Protocolo:** `protocol/control.fbs`; `protocol/control-v1.md`; `tools/flatc/` (binário fixo); `tools/gen-protocol.ps1`;
gerado em `host/Host.Protocol/Generated/` e `Assets/_Project/Infrastructure/Protocol/Generated/`.

**Host:**
- `Host.Protocol`: código gerado + `FrameCodec` (encode/decode de frames, remontagem de fluxo TCP).
- `Host.Domain`: `PairingStore` (tokens, pares; puro).
- `Host.Transport`: `ControlServer` (TCP 9200, uma `HeadsetSession` por conexão), `MdnsAdvertiser` (`DnsServiceRegister`).
- `Host.App`: `HeadsetSession` (handshake → pareamento → `StartStream` → `StreamPipeline` para o IP do headset;
  reaproveita o pipeline na reconexão do mesmo `device_id`), `IPairingApprover` com implementação `MessageBox`,
  comando `serve` (padrão). `stream`/`receive` continuam para testes.

**Headset `.aar`:** `HostDiscovery` (Kotlin, `NsdManager` + `WifiManager.MulticastLock`), exposto por JNI com leitura
sem alocação (a Unity consulta 1×/frame).

**Unity:**
- Application: `ConnectionStateMachine`, `ConnectionStatus`, portas `IHostDiscovery`, `IControlChannel`,
  `IVideoStreamSink`, `IClock`, `IPairingStore`.
- Infrastructure (novo asmdef `ScreenPortal.Infrastructure`): `TcpControlChannel` (thread própria + fila para a
  thread principal), `AndroidHostDiscovery`, `NativeVideoBridge` (movido do spike; adapter de `IVideoStreamSink`),
  `PlayerPrefsPairingStore`, protocolo gerado + runtime FlatBuffers.
- Presentation: `ConnectionStatusView` (painel de texto no espaço); `OverlayScreenView` expõe a Surface da layer.
- Composition: registra tudo; `AppEntryPoint` deixa de criar a tela Demo fixa — ela nasce com a resolução do `Welcome`.

**Fluxo do vídeo:** `Welcome(monitores)` → `CreateScreen(resolução do primário)` → layer criada → Surface pronta →
`IVideoStreamSink.Start(surface, porta, stream_id)` → `StartStream` → host transmite.

## 5. Testes

- **`dotnet test` (Unity puro):** todas as transições da tabela; `Degraded` em 1,5 s; `Reconnecting` em 3 s; reconexão
  tenta o último IP antes do mDNS; pareamento sem timeout; versão incompatível → mensagem certa; `Workspace` intacta.
- **`dotnet test` (host):** `FrameCodec` (frames partidos/colados, tamanho máximo, lixo); `PairingStore`; roundtrip
  do protocolo entre o C# gerado do host e o da Unity.
- **Integração (`Host.Tests`):** cliente de teste contra o `ControlServer` real em localhost: `Hello` → pareamento
  (aprovador falso) → `Welcome` → `StartStream` → queda do socket → reconexão, **medindo o tempo**.
- **Headset (roteiro com o Cayo, ~10 min):** (1) sem host → "Procurando seu PC…"; (2) `serve` → código nos dois lados
  → Permitir → vídeo na tela Demo; (3) reabrir → conecta sem pareamento; (4) matar/reabrir o host ou desligar o Wi-Fi
  por 2 s → "Reconectando…" → vídeo volta, tempo medido no log; (5) Negar → mensagem e nenhuma tela.

## 6. Pronto quando

Todos os testes passam nos dois runners; os passos 1–5 funcionam no Quest; reconexão medida < 3 s.

## 7. Fora do escopo

Criptografia (v1.1) · vários PCs ao mesmo tempo (v1.2) · ícone na bandeja e instalador (Fase 5) · sincronização do
layout com o host (`LayoutChanged`, Fases 2/3) · áudio (Fase 2).
