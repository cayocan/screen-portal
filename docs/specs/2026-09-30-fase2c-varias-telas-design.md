# Fase 2C — Várias telas: monitores virtuais sob comando do headset, N streams — Design

**Data:** 30/09/2026 · **Aprovado por:** Cayo (brainstorming de 30/09/2026)
**Depende de:** 2A ([ADR 0007](../adr/0007-virtual-display-driver.md) SudoVDA · [ADR 0008](../adr/0008-decode-capacity.md)
capacidade do decoder) · 2B (superfície de layout, manipulação, menu de pulso).
**Brief:** §10.1 (resize é zoom), §10.3 (seletor), §15.7 (resolução exata na criação, nunca troca de modo ao vivo).

## 1. Decisões

| # | Decisão |
|---|---|
| 1 | Toda tela tem uma **origem**: monitor virtual (SudoVDA, resolução exata), **espelho de um monitor físico** ou tela de teste. O menu cria as duas primeiras; a de teste fica só em build de desenvolvimento |
| 2 | Seletor em **dois níveis** no menu de pulso: proporções (cada item é um frame na proporção, com o nome no meio; botão Paisagem/Retrato no topo gira os frames) → resoluções daquela proporção → cria a tela. Custom fica na 2D |
| 3 | Monitores virtuais têm **tolerância na desconexão**: o host mantém por **5 min** (configurável) e depois remove; voltando antes, tudo reaparece com as janelas no lugar |
| 4 | **Remover** some na hora no headset; o host remove o monitor só após **10 s**. Undo dentro do prazo reata o mesmo monitor (janelas intactas); depois, recria vazio com a mesma resolução |
| 5 | **Sem limite** de telas; ao criar a 5ª tela com vídeo em diante, aviso "Mais de 4 telas: pode haver quedas e travamentos" (a tela é criada) |
| 6 | Sincronização **declarativa**: a workspace do headset é a fonte da verdade; o `HostSync` (puro) reconcilia com os "aluguéis" de monitor do host. Undo, remoção adiada e reconexão usam o mesmo caminho (ADR 0009) |
| 7 | Vídeo: **um stream por tela**, cada um na sua porta UDP (9100 + n) e `stream_id`, um decoder nativo por tela desenhando na Surface da sua compositor layer (arranjo medido na 2A). O quadro nunca atravessa o C# |
| 8 | Primeira conexão com a workspace vazia cria um **espelho do monitor principal** (mantém o comportamento da 1B); é um Command normal |

## 2. Escopo

**Dentro:** origem das telas; seletor proporção → resolução; espelho de monitor físico; monitores SudoVDA criados e
removidos a pedido do headset com aluguel e prazos; N streams e N decoders; estados visuais da tela; protocolo v2;
medição de 4K no Quest.

**Fora:** contorno fantasma, recriar e resolução custom (2D — encolhe, porque o seletor veio para cá); telas curvas
(2E); fps por tela e `BandwidthBudget` (2F); guardar o layout entre sessões do app (perfis); mais de um headset no
mesmo PC.

## 3. Domínio e menu (headset)

- `VirtualScreen` ganha `ScreenSource`: `Virtual(resolução)` · `Mirror(physicalMonitorId)` (resolução e nome vêm do
  host) · `Test`.
- `CreateScreen(origem, resolução, placement, larguraM)` cria os três tipos; `RemoveScreen` já existe. Nenhum Command
  fala com o host: o `HostSync` reage à workspace.
- **Largura inicial:** mesma densidade para todas as resoluções — 2560 px ≈ 1,0 m (a 1,3 m), limitada por
  `ScreenLimits`. É só zoom; as alças ajustam depois.
- **Posição inicial (revisada no Quest, 30/09):** a tela criada pelo menu nasce **à frente da visão**: no ponto da
  superfície para onde a cabeça olha, na distância padrão (1,3 m). Se o ponto estiver ocupado, vai para o espaço livre
  mais perto, à esquerda ou à direita, na mesma elevação (`SurfaceSlots.NearestFree`). O espelho automático da primeira
  conexão mantém a regra da 2B (posição padrão, primeiro espaço livre à direita).

**Menu de pulso:** "+ Tela de teste" sai do menu principal (fica em build de desenvolvimento) e entram
**"+ Monitor virtual"** e **"+ Espelhar monitor"**.
- "+ Monitor virtual" → submenu de proporções: botão **Paisagem | Retrato** no topo; itens 16:9, 16:10, 21:9, 32:9,
  1:1, 4:3, cada um um frame na proporção (girado em retrato) com o nome no meio → submenu de resoluções → tocar cria
  a tela. "← Voltar" nos dois níveis.
- "+ Espelhar monitor" → lista dos monitores físicos do `Welcome` (nome + resolução).
- 5ª tela com vídeo em diante: aviso (decisão 5). Sem PC conectado: itens de criar apagados e o status diz
  "Conecte ao PC".
- A navegação do menu (nível atual, orientação, itens habilitados, aviso) é um estado puro em Application; o painel só
  desenha.

**Catálogo de resoluções** (paisagem; retrato inverte; todas múltiplas de 8 e ≤ 4096, o limite do H.264 da GPU):

| Proporção | Resoluções |
|---|---|
| 16:9 | 1920×1080 · 2560×1440 · 3840×2160 |
| 16:10 | 1920×1200 · 2560×1600 · 3840×2400 |
| 21:9 | 2560×1080 · 3440×1440 |
| 32:9 | 3840×1080 |
| 1:1 | 1440×1440 · 2160×2160 |
| 4:3 | 1600×1200 · 2048×1536 |

## 4. Protocolo v2 e sincronização

**Controle (`protocol/control.fbs`, `protocol_version` = 2;** versões diferentes → `Bye(IncompatibleVersion)`):
- `screen_key` = valor do `ScreenId` (nunca reutilizado; undo restaura o mesmo id, então reata o mesmo monitor).
- `AcquireScreen{screen_key, kind: Virtual|Mirror, width, height, physical_monitor_id}` → o host cria o monitor
  SudoVDA (ou reata um aluguel vivo) e responde `ScreenReady{screen_key, width, height}` ou
  `ScreenFailed{screen_key, reason}`.
- `StartStream{screen_key, udp_port, stream_id}` (por tela, não mais por monitor) → `StreamStarted` / `StreamStopped`.
- `ReleaseScreen{screen_key, grace_ms}` → para o stream e agenda a remoção; um `AcquireScreen` da mesma chave dentro
  do prazo cancela.
- `Welcome` ganha `leases:[ScreenLease{screen_key, kind, width, height}]` (aluguéis vivos deste headset) e mantém a
  lista de monitores físicos.
- Desconexão: o host aplica 5 min a todos os aluguéis daquele headset.
- `protocol/control-v1.md` vira `control-v2.md`; o C# é regenerado com `tools/gen-protocol.ps1`.

**`HostSync` (Application, puro):**
- Entradas: telas `Virtual`/`Mirror` da workspace; estado de cada chave: `Pedido → Pronto → Transmitindo | Falhou`.
- Saídas: mensagens de controle e ordens para `IScreenVideo` (abrir/fechar a sessão nativa com a Surface da tela).
- Roda em toda mudança da workspace e nos eventos de conexão (após o `Welcome`) e desconexão.
- Porta e `stream_id` de um pool (menor índice livre; porta 9100 + n).
- Remover → `ReleaseScreen(10 s)`. Desconectar → nada (o host aplica a tolerância). Reconectar → lê os `leases` e pede
  de novo tudo o que a workspace tem.
- Falha → a tela fica no mundo com "Falha ao criar monitor: <motivo>"; sem remoção automática.

**Host:**
- `ScreenLeaseTable` (Host.Domain, puro, relógio injetado): `(device, screen_key)` → monitor, tipo, expiração.
  Aluguel de **espelho** nunca remove nada no Windows: expirar só encerra o stream.
- `StreamRegistry`: de 1 stream por headset para **N por headset**, um `StreamPipeline` por aluguel.
- Captura WGC resolve o `HMONITOR` do monitor novo com espera e nova tentativa (~2 s, ADR 0007).
- Aluguéis só em memória; no arranque o host remove monitores SudoVDA órfãos.

## 5. Vídeo e telas no headset

- **Views por origem:** `Test` → `TestPatternScreenView`; `Virtual`/`Mirror` → `VideoScreenView` (evolução do
  `OverlayScreenView`: OVROverlay com Surface externa na resolução exata).
- **Estados visuais** (quad no eye buffer enquanto não há vídeo; a layer fica escondida até o primeiro quadro):
  "Criando monitor…" (`Pedido`) · "Conectando vídeo…" (`Pronto`) · vídeo (`Transmitindo`) · "Falha: <motivo>" ·
  "PC desconectado" (última imagem escurecida).
- **Interface por cima das telas (revisado no Quest, 30/09, ADR 0010):** a layer é **Underlay**. Um furo no eye
  buffer (alpha 0 + profundidade, shader `ScreenPortal/ScreenHole`) a revela. Moldura, alças, laser e menu são
  desenhados na frente e ficam por cima da tela.
- **`IScreenVideo` (Infrastructure):** no Quest, `NativeVideo.start/stop` do `.aar` (uma sessão por tela); no Editor
  e no Simulator, no-op. `takeStats` por sessão, 1×/s fora do caminho quente: marca o primeiro quadro (→
  `Transmitindo`) e alimenta o log.
- **1B:** o `ConnectionStateMachine` deixa de pedir o stream do primário; só cuida da conexão.

## 6. Testes e verificação

**Automáticos:**
- Harness `dotnet test` (Domain + Application): `HostSync` (criar; remover → release 10 s; undo no prazo → mesma
  chave; reconexão com `leases`; falha; reuso do pool); `ScreenSource` e invariantes do `CreateScreen`; catálogo
  (múltiplos de 8, ≤ 4096, retrato inverte); largura inicial; navegação do menu; aviso da 5ª tela; itens apagados sem
  PC; espelho automático na primeira conexão.
- Host (xUnit): `ScreenLeaseTable` (prazo, reatar, expiração, por headset); codec v2 ida e volta; limpeza de órfãos.
- Unity EditMode: composição.

**Meta XR Simulator + host real no PC** (o Editor conecta em 127.0.0.1; controle real, vídeo no-op), pelo Meta XR
Operator como na 2B: submenus (frames girando, voltar, aviso, itens apagados sem PC); monitor aparece no Windows com a
resolução certa; remover + undo < 10 s (janelas ficam); remover > 10 s (janelas voltam ao físico); desconectar e
voltar < 5 min (tudo reaparece).

**Quest** (só o que depende dele): 4 telas a 60 fps incluindo uma 4K (pendência do ADR 0008); 5ª tela com aviso
(anotar onde começa a perder quadros); espelho do monitor físico; latência por stream; alças sob a layer; nitidez do
texto por resolução.

**Documentação:** ADR 0009 (protocolo v2, aluguéis e reconciliação); relatório em `docs/measurements/fase2c-*.md`;
STATUS: a 2D encolhe para contorno fantasma + recriar + resolução custom.

**Riscos:** a espera de ~2 s até o monitor novo ser capturável deixa um atraso visível ("Criando monitor…"); o 4K pode
exigir mais banda do que a 2A mediu.

## 7. Desvios na implementação
Do plano ([plans/2026-09-30-fase2c-varias-telas.md](../plans/2026-09-30-fase2c-varias-telas.md)):
1. **Portas 9000 + n**, não 9100 + n: a porta de vídeo da 1B já é 9000 e a 9101 é do KFRQ.
2. **Sem limpeza de órfãos no arranque:** o watchdog do SudoVDA remove os monitores quando o host para de pingar.
3. **"PC desconectado" depois de já ter tido vídeo:** a layer fica visível e escurecida, sem texto.
4. **O pipeline para na desconexão;** só o monitor fica 5 min. Na reconexão o `HostSync` pede o stream de novo.
5. **Fila de trabalho na `HeadsetSession`:** Acquire/StartStream/Release rodam fora da thread de leitura do socket.
6. **Ids de monitor físico estáveis** (número do `\.\DISPLAYn`); os monitores virtuais do app ficam fora do `Welcome`.
7. **O Editor pareia uma vez só** (`PlayerPrefsPairingStore`).

Do teste no Quest (30/09):
8. **Interface por cima das telas:** layers Underlay reveladas por um furo no eye buffer (§5, ADR 0010).
9. **Tela nova à frente da visão**, no espaço livre mais perto (§3).
10. **Reorganizar os monitores no Windows:** o host recria a captura de uma tela quando o monitor dela muda de posição
    no desktop; o pipeline novo continua os frameIds do anterior
    ([verificação §4](../measurements/fase2c-simulator.md)).
11. **Log do host em arquivo:** `serve` grava também em `%APPDATA%\ScreenPortal\host.log`; falha de pipeline registra o
    `DeviceRemovedReason` e a carga de memória.
12. **Posição das telas no mundo → arranjo do Windows** (pedido no teste): fica para a Fase 3, como o brief já prevê
    (cursor espacial; sincronizar a topologia do Windows é o plano B).
