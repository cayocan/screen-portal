# Screen Portal — Project Brief

> Documento de partida para o Claude Code. Descreve o produto, a arquitetura, os design patterns e a ordem de execução. Em caso de dúvida entre velocidade e qualidade do núcleo de vídeo, a qualidade do núcleo vence. Em caso de dúvida na UI/UX, entregue rápido e itere.
>
> **Status: v1.3 do brief, escopo do MVP FECHADO em 27/09/2026** (inclui a manipulação com o mouse da seção 10.10, os monitores em retrato da seção 10.3 e as telas curvas com curvatura ajustável da seção 10.2). A partir daqui, mudanças de escopo só com: (1) atualização deste documento, (2) ADR em `docs/adr/` explicando o porquê e o que sai para compensar. Ideias novas vão direto para o backlog (seção 4.3).

---

## 1. Visão

App de produtividade para o Meta Quest 3 que substitui monitores físicos por telas virtuais: **quantidade livre**, **resolução escolhida na criação** (incluindo ultrawide e retrato), **tamanho e posição livres no espaço 3D**, **janelas destacáveis**, **mouse que vai para onde a tela está**, **perfis de layout salvos** e **passthrough ajustável da área do teclado/mouse**, para ver as mãos e os periféricos reais.

Requisitos que não se negociam: **leve** (sem travadas e sem desperdício de bateria) e **texto nítido** (dá para programar e ler por horas).

### 1.1 Onde o produto roda: dois programas trabalhando juntos

| | **App no Quest** (o que o usuário usa) | **Host no Windows** (em segundo plano) |
|---|---|---|
| **O que é** | App nativo do headset (APK), distribuído pela loja da Meta. Roda **standalone**, sem cabo e sem Quest Link/SteamVR | Programa leve na **bandeja do sistema** (ao lado do relógio), que inicia com o Windows, como Discord ou Steam |
| **Responsável por** | Desenhar as telas no espaço 3D, passthrough da mesa, tracking do teclado, mãos e menus, seletor de monitor, perfis, **decodificar o vídeo**, desenhar o cursor e **aplicar a manipulação das telas** | Criar os monitores virtuais, **capturar e comprimir** cada tela, enviar pro Quest pelo Wi-Fi, áudio, **cursor espacial**, **captar o mouse pra manipular as telas** e **atalhos de teclado** |
| **Onde ficam os programas do usuário** | Em nenhum lugar. O Quest só **mostra** as telas | IDE, navegador, Spotify etc. rodam **no PC**, como sempre. Teclado e mouse continuam ligados no PC |

**Fluxo do dia a dia:**
1. O host inicia junto com o Windows e fica em segundo plano.
2. O usuário coloca o Quest e abre o app.
3. O app **encontra o PC sozinho** na rede (mDNS) e conecta.
4. As telas aparecem como estavam na última sessão.

**Por que não renderizar tudo no PC (modelo Quest Link / SteamVR):** nesse modelo, o PC desenha a cena VR inteira e manda pro headset. Isso pesa na GPU, exige Link/Air Link e costuma deixar texto e passthrough piores. No nosso modelo, **o PC só comprime as telas** (leve), e o Quest faz o resto com as camadas nativas de nitidez e passthrough. É a mesma arquitetura de Virtual Desktop e Immersed.

**Consequência (intencional):** sem o PC ligado e com o host rodando, o app no Quest não tem o que mostrar. O produto é uma **extensão do computador do usuário**, não um computador independente. O app deve deixar isso claro na primeira abertura, com uma tela "Instale o host no seu PC" (link + QR code) e um status "Procurando seu PC…" quando não achar o host.

---

## 2. Problema: gaps do mercado

| Dor | Onde acontece |
|---|---|
| Número de telas limitado pelo hardware | Virtual Desktop: até 3 no Quest 3. Horizon Workrooms: limitado ao nº de monitores físicos do PC |
| Texto pouco nítido, screen-door | Todos. Reclamação nº 1 em reviews de "Quest 3 para trabalho" |
| Passthrough de teclado só com modelos homologados | Virtual Desktop / Meta (lista fechada de teclados) |
| Sem perfis de layout por contexto | Todos. O usuário reorganiza as telas toda vez |
| Resize grosseiro, layout "template" | Especialmente o Horizon Workrooms |
| Instabilidade (travamentos, áudio, calls) | Relatos em fóruns e reviews |

---

## 3. Diferenciais do produto

1. **N monitores virtuais**, desacoplados dos monitores físicos
2. **Resolução escolhida na criação, tamanho livre depois**: ao criar um monitor, o usuário escolhe a proporção (16:9, 16:10, ultrawide, super ultrawide, quadrado, 4:3, custom) e a **orientação: paisagem ou retrato (vertical)**, que vale pra qualquer proporção, num **seletor com preview visual**. Depois, redimensionar é **zoom proporcional** (a resolução nunca muda) o **posicionamento é livre** (qualquer distância e ângulo) e qualquer tela pode ser **plana ou curva, com a curvatura ajustável** por atalho, mouse ou menu (seções 10.1–10.3)
3. **Perfis de layout** ("Dev", "Reunião", "Escrita"), ancorados na mesa real com spatial anchors
4. **Zona de passthrough "sugere e você ajusta"**: o app detecta o teclado (qualquer marca) e **sugere** uma área de passthrough que cobre teclado, mouse e mãos. O usuário **ajusta livremente** (tamanho, altura, formato, bordas suaves, zonas extras), e o ajuste acompanha o teclado quando ele é movido (seção 9)
5. **Texto nítido** via compositor layers, sem reamostragem pela cena 3D
6. **Frame rate por tela, automático ou manual**: por padrão, a tela em foco roda a 60 fps e as outras a 10–15 fps (isso viabiliza muitas telas). O usuário pode **fixar o fps de qualquer tela** (ex.: um filme numa tela secundária a 60 fps enquanto trabalha em outra), e o app também **detecta sozinho conteúdo em movimento** (vídeo) e sobe o fps daquela tela
7. **Janela destacada**: qualquer janela do Windows (Spotify, terminal, Slack) pode ser "arrancada" e virar uma tela flutuante própria, do tamanho exato dela (seção 10)
8. **Cursor espacial**: o mouse vai para onde a tela **está no espaço 3D**, não para onde o Windows acha que ela está. Janela à esquerda, mouse pra esquerda, entra nela. Janela acima do desktop, mouse pra cima, entra nela (seção 10)
9. **Controle total pelo teclado**: atalhos globais para mover, redimensionar, destacar, trocar perfil, mudar fps e mostrar a zona de passthrough, sem tirar a mão do teclado (seção 10.9)
10. **Mouse-first**: mover, **afastar/aproximar (segurando o clique e girando a roda)** e redimensionar as telas **com o mouse**, pela moldura da tela ou segurando uma tecla. Fora das telas, o mouse vira um **cursor 3D** que opera toda a interface do app. As mãos são uma alternativa, nunca obrigatórias (seção 10.10)

---

## 4. Escopo do MVP: três coisas perfeitas

> Um produto que faz três coisas muito bem vende mais do que um que faz dez pela metade, principalmente em VR, onde cada travada ou atrito é sentido no corpo. **O MVP só é lançado quando os três pilares atingirem a barra de qualidade.** Qualquer ideia nova entra no backlog, não no MVP.

### 4.1 Os três pilares do MVP (v1.0)

| Pilar | O que entra | Seções |
|---|---|---|
| **A. Telas nítidas e livres** | N monitores virtuais (IddCx) · **seletor de proporção/resolução na criação, com orientação paisagem ou retrato** · **janela destacada** · mover livre + resize proporcional (zoom) · **tela plana ou curva com curvatura ajustável** (atalho, mouse, menu) · frame rate por tela (Auto + manual: Fixed/Video/Eco) · áudio do PC · perfis de layout + retomar a última sessão ao colocar o headset | 8.8, 10 |
| **B. Zona de passthrough "sugere e ajusta"** | Sugestão via teclado (com fallbacks) · modo edição completo · ajustes como deltas · zona por perfil · congelar ao perder o tracking · invariante "tela não invade zona" | 9 |
| **C. Mouse e teclado em primeiro lugar** | **Cursor espacial** (roteamento 3D) · cursor desenhado localmente no headset (latência baixa) · **manipulação das telas com o mouse** (moldura + tecla modificadora: mover, afastar com a roda, redimensionar, menu da tela) · **cursor 3D livre** operando toda a UI do app · atalhos globais de teclado · mãos como alternativa · Windows organizado como plano B | 10 |

### 4.2 Barra de qualidade para lançar (critérios de aceite)
- 90 Hz estáveis, 0 bytes de GC por frame, latência de vídeo dentro do orçamento (seção 11)
- **Cursor:** atravessar entre telas em **100%** dos casos dos testes de layout (esquerda, direita, diagonal, acima, abaixo, com vão e sem vão), sem "pulos" nem cursor preso
- **Cursor local:** movimento percebido com < 20 ms de atraso (independente do vídeo)
- **Zona:** sugestão aceitável sem ajuste em ≥ 80% dos testes com teclados diferentes
- **Estabilidade:** sessão de 4 h sem crash; reconexão em < 3 s sem perder o layout
- **Texto:** fonte 12 legível na distância padrão sugerida
- **Janela destacada:** destacar e reanexar sem perder nem deslocar a janela em 100% dos testes (apps comuns: navegador, IDE, terminal, Spotify, Slack)
- **Resolução:** nenhuma ação de resize ou de curvatura no headset altera a resolução de um monitor (teste de domínio + teste em hardware)
- **Curvatura:** ligar/desligar e ajustar por atalho e pelo mouse com resposta imediata; troca da layer cilíndrica entre telas curvas sem piscada perceptível
- **Sem as mãos:** um usuário consegue fazer tudo **só com mouse e teclado**: criar monitor, mover, afastar, redimensionar, destacar janela, editar a zona de passthrough, trocar perfil e usar os menus
- **Sem vazamento:** nenhum clique ou scroll de manipulação chega ao app do Windows (0 ocorrências na bateria de testes)

### 4.3 Backlog pós-MVP (ordem sugerida)

| Versão | Itens |
|---|---|
| **v1.1** | **Assistente de legibilidade** (sugere a escala de texto do Windows ideal para o tamanho e a distância de cada tela) · **olhar e pular** (olhar para a tela + atalho teleporta o cursor) · **modo foco** (escurece tudo menos a tela ativa) · **modo chamada** (passthrough total temporário) · **conforto** (lembrete 20-20-20, bateria com tempo estimado, aviso de "modo cabo") · HEVC + FEC |
| **v1.2** | **Vários PCs ao mesmo tempo** (notebook do trabalho + PC pessoal, cada um com seu host) · zona em **polígono livre** · detecção de mouse por câmera (se necessário) · resoluções acima de 4096 px em qualquer dimensão (se não entrarem com o HEVC na v1.1) |
| **Futuro** | Host para Mac · USB tether · colaboração/compartilhamento de tela com outra pessoa |
| **Fora do escopo** | Jogos · apps rodando nativamente no headset · ambientes 3D elaborados |

---

## 5. Usuário-alvo (MVP)

Dev ou profissional remoto com Quest 3 e PC Windows 10/11 na mesma rede local, que quer vários monitores sem ter os monitores.

**Requisitos mínimos do MVP:** Wi-Fi 6/6E com o headset perto do roteador e o PC **no cabo de rede**; GPU com encoder H.264 de hardware (NVIDIA NVENC, AMD AMF ou Intel QuickSync); Quest 3/3S com Horizon OS v74+. (USB tether fica para o futuro, seção 4.3.)

---

## 6. Decisão de stack: híbrido Unity + nativo

**Unity cuida do "cérebro" e da interação. O código nativo cuida do caminho do vídeo.** O frame de vídeo nunca passa pelo C#.

### 6.1 Por que não Unity pura
O GC do C# e as cópias de textura no caminho do vídeo causam travadas (judder), que em VR geram desconforto. Renderizar a tela como um mesh na cena reamostra a imagem duas vezes, e o texto borra.

### 6.2 Por que não 100% nativo (NDK + OpenXR)
Seria preciso reescrever interação, hand tracking, UI, raycast e passthrough na mão, com documentação mais fraca. Isso custa de 2 a 3 vezes mais tempo até o MVP, e o ganho sobre o híbrido bem feito é marginal.

### 6.3 Stack final

| Camada | Tecnologia |
|---|---|
| Headset: app | **Unity 6.3 LTS** (último patch; suportado até ~dez/2027) + **Meta XR SDK na versão mais recente** (mínimo 83.0.1 no Unity 6.3), URP enxuto, **OpenXR + Meta XR Core SDK**, **Meta Interaction SDK** (grab/resize/poke), IL2CPP, ARM64 |
| Headset: vídeo | **Android library (.aar)** em Kotlin + C++ (NDK): rede, `MediaCodec` (decode por hardware) |
| Headset: renderização das telas | **Compositor layers** (`OVROverlay` quad/cylinder) com **external surface**: o `MediaCodec` decodifica direto na Surface da layer (zero-copy, sem passar pela cena) |
| Headset: passthrough | **MR Utility Kit (MRUK) Keyboard Tracking** (rastreamento de teclado genérico, sem depender de marca) + **Passthrough Windows** (shader que zera o alpha, recortando a região da mesa). Detalhes e fontes na seção 15 |
| PC: host | **C# / .NET 10 LTS** (Windows), com interop para as APIs nativas. (.NET 8 perde suporte em 10/11/2026, então não usar) |
| PC: captura | **Windows Graphics Capture** (por monitor **e por janela**). Fallback: DXGI Desktop Duplication |
| PC: monitores virtuais | **Indirect Display Driver (IddCx)**: cria monitores "reais" para o Windows, sem limite físico. MVP: controlar o **Virtual Display Driver** open-source (MIT, já assinado). Resolução definida **na criação** do monitor e nunca trocada ao vivo (seções 10.1 e 15.7) |
| PC: encode | Encoder de hardware (NVENC / AMF / QuickSync) via Media Foundation ou FFmpeg. **H.264 no MVP, HEVC depois**. Low-latency: sem B-frames, intra-refresh, bitrate por tela |
| PC: áudio | Captura do áudio do sistema (WASAPI loopback) → **Opus** low-latency → UDP. O headset toca via AAudio/Oboe no `.aar`, com sincronia pelo timestamp do vídeo (necessário para filmes e calls) |
| Protocolo | Canal de controle **TCP** + mensagens **FlatBuffers** (versionadas). Vídeo por **UDP** (um fluxo por tela), com NACK/retransmissão e FEC depois. Descoberta via **mDNS** |
| Ferramentas de dev | **Unity CLI** (`unity`, beta) + pacote `com.unity.pipeline` (experimental) pra o Claude Code controlar o Editor pelo terminal: compilar, rodar testes, abrir cenas, build. **Fallback obrigatório:** modo clássico `Unity.exe -batchmode` (`-runTests`, `-executeMethod`). `dotnet` CLI pro host, Gradle pro `.aar`, `adb` pro deploy e logs no Quest |
| PC: entrada | **Raw Input** + hook **WH_MOUSE_LL** + `SetCursorPos` (cursor espacial), `RegisterHotKey` (atalhos globais), `SetDisplayConfig` (plano B da topologia). Cursor desenhado localmente no headset (seção 10) |

> **Entrada (teclado/mouse):** o teclado e o mouse físicos continuam ligados ao PC, e os monitores virtuais existem de verdade para o Windows (IddCx), então clique e digitação funcionam nativamente. O que o app controla é **para onde o cursor atravessa** entre telas (cursor espacial, seção 10).

### 6.4 Pacotes Unity obrigatórios (o Claude Code instala)
O projeto Unity (`headset-unity/`, criado a partir do template **Universal 3D** no Unity 6.3 LTS) precisa de **dois grupos de pacotes**. **O Claude Code é responsável por instalar os dois** no início da Fase 0, antes de qualquer código de cena.

**Grupo 1: VR / Meta Quest**

| Pacote | Nome UPM | Pra quê |
|---|---|---|
| Meta XR Core SDK | `com.meta.xr.sdk.core` | Base: OpenXR/Meta, `OVROverlay` (compositor layers com external surface), Passthrough, Building Blocks, Project Setup Tool |
| Meta XR Interaction SDK | `com.meta.xr.sdk.interaction.ovr` | Mãos e controles (alternativa ao mouse, seção 10.10) |
| Meta MR Utility Kit | `com.meta.xr.mrutilitykit` | Keyboard Tracking e plano da mesa (zona de passthrough, seção 9) |
| OpenXR Plugin + XR Plugin Management | `com.unity.xr.openxr` · `com.unity.xr.management` | Runtime XR (normalmente entram como dependência do Core SDK; conferir) |
| URP | `com.unity.render-pipelines.universal` | Já vem do template Universal 3D |

**Grupo 2: IA / automação do Editor**

| Pacote | Nome UPM | Pra quê |
|---|---|---|
| AI Assistant | `com.unity.ai.assistant` | Habilita o **Unity MCP** (Edit → Project Settings → AI → Unity MCP), pra o Claude Code inspecionar cenas, console e GameObjects |
| Unity Pipeline (opcional) | `com.unity.pipeline` | Backend do **Unity CLI** (`unity pipeline install`). Experimental: instalar só se `unity status` funcionar no 6.3 LTS |

**Como o Claude Code instala:**
1. **Pacotes da Meta** via **scoped registry** da Meta no `headset-unity/Packages/manifest.json` (automatizável, versionado no Git):
   ```json
   "scopedRegistries": [
     {
       "name": "Meta XR",
       "url": "https://npm.developer.oculus.com",
       "scopes": ["com.meta.xr"]
     }
   ]
   ```
   Depois, adicionar em `dependencies` as três `com.meta.xr.*` na **mesma versão, a mais recente** publicada no registry (mínimo 83.0.1 no Unity 6.3). Fixar a versão exata (nada de "latest"), pra builds reproduzíveis.
2. **Pacotes da Unity** (`com.unity.ai.assistant`, e `com.unity.pipeline` se aplicável) direto em `dependencies`, com versão exata compatível com o 6.3 LTS. O AI Assistant pode estar como pré-lançamento: nesse caso, usar a versão pré-lançamento mais recente compatível e registrar isso num ADR.
3. Abrir/recarregar o projeto (Unity CLI, batchmode ou pedindo pro Cayo abrir o Editor) e **conferir no Console que não há erros de resolução de pacote**.
4. Rodar o **Meta Project Setup Tool → Fix All** (via Unity CLI/MCP se possível; senão, pedir ao Cayo) e **commitar** o `manifest.json`, o `packages-lock.json` e os `ProjectSettings/` resultantes.

**O que só o Cayo pode fazer (o Claude Code deve pedir, não tentar):**
- Se a Meta recomendar instalar via **Unity Asset Store** (ou o registry falhar com erro de licença), adicionar os pacotes em "My Assets" com a conta Unity dele. O Claude Code não tem acesso à conta.
- Ativar o trial/assinatura das ferramentas de IA da Unity, exigido pelo AI Assistant.
- Na primeira conexão do Claude Code ao Unity MCP, clicar em **Accept** em Project Settings → AI → Unity MCP.

---

## 7. Arquitetura

### 7.1 Visão geral

```
┌──────────────────────────── PC (Windows) ─────────────────────────────┐
│  IddCx Driver ──► Monitores virtuais (N)                              │
│                                                                       │
│  Host App (.NET 10)                                                   │
│   ├─ SessionManager (estado da conexão, descoberta mDNS)              │
│   ├─ StreamPipeline × N  [Capture ► Encode ► Packetize ► Send]        │
│   │        (uma thread por estágio, filas lock-free, buffers em pool) │
│   ├─ ControlChannel (TCP, FlatBuffers)                                │
│   ├─ RateController (fps/bitrate por tela, conforme o foco)           │
│   ├─ SpatialCursorRouter (Raw Input + hook + SpatialMap)              │
│   ├─ CursorStreamer (posição/forma do cursor → headset, UDP)          │
│   ├─ VirtualDisplayManager + WindowDetacher (janela destacada)        │
│   └─ HotkeyService (atalhos globais → Commands)                       │
└───────────────────────────────┬───────────────────────────────────────┘
                                │ LAN: TCP (controle) + UDP (vídeo × N)
┌───────────────────────────────▼──────── Quest 3 ──────────────────────┐
│  Native .aar (Kotlin/C++)                                             │
│   ├─ Receiver × N  (depacketize, jitter buffer mínimo)                │
│   └─ Decoder × N   (MediaCodec ► Surface da compositor layer)         │
│                         ▲ zero-copy                                   │
│  Unity                  │                                             │
│   ├─ Domain (C# puro): Workspace, Screen, LayoutProfile, Commands     │
│   ├─ Application: WorkspaceStore, CommandBus, ProfileService          │
│   ├─ Presentation: ScreenView (OVROverlay), CursorView, Handles,      │
│   │                NewScreenPicker, Menus                             │
│   ├─ Infrastructure: NativeVideoBridge, ControlClient, AnchorStore    │
│   └─ Passthrough: DeskPassthroughZone / KeyboardDetector              │
└───────────────────────────────────────────────────────────────────────┘
```

### 7.2 Estrutura do monorepo

```
/ScreenPortal                  # raiz do repositório (nome do projeto: Screen Portal)
├── CLAUDE.md                  # regras para o Claude Code (seção 13)
├── docs/                      # este brief, ADRs, medições de latência
│   └── adr/                   # Architecture Decision Records (1 arquivo por decisão)
├── protocol/                  # schemas .fbs (FlatBuffers) → gera C# e C++/Kotlin
├── host/                      # PC companion (.NET 10)
│   ├── Host.Domain/           # regras puras (sem Windows API)
│   ├── Host.Capture/          # WGC, DXGI (Strategy)
│   ├── Host.Encode/           # NVENC/AMF/QSV/MF (Strategy)
│   ├── Host.Transport/        # TCP/UDP, mDNS
│   ├── Host.Input/            # cursor espacial, Raw Input, hook, atalhos, CursorStreamer
│   ├── Host.Display/          # monitores virtuais, janela destacada, topologia plano B
│   ├── Host.App/              # composição, UI da bandeja (tray)
│   └── Host.Tests/
├── driver/                    # MVP: integração/instalador do Virtual Display Driver (open-source, assinado).
│                              # Driver IddCx próprio só se o spike da Fase 2 exigir (ADR)
├── headset-native/            # Android library (.aar): Kotlin + C++ NDK
│   ├── receiver/  decoder/  jni-bridge/
│   └── tests/
└── headset-unity/             # projeto Unity
    └── Assets/_Project/
        ├── Domain/            # asmdef sem referência a UnityEngine
        ├── Application/
        ├── Presentation/
        ├── Infrastructure/
        ├── Composition/       # LifetimeScope (DI)
        └── Tests/ (EditMode + PlayMode)
```

---

## 8. Design patterns (e por quê)

### 8.1 Arquitetura geral: Clean / Hexagonal (Ports & Adapters)
- **Domain** (C# puro, sem `UnityEngine`) contém `Workspace`, `VirtualScreen`, `Pose`, `Size`, `LayoutProfile` e as regras (tamanho mínimo/máximo, snapping, colisão).
- **Ports** (interfaces): `IVideoStreamSink`, `IControlChannel`, `IAnchorStore`, `IProfileRepository`, `IPassthroughZone`.
- **Adapters** implementam as ports: `OvrOverlayScreenView`, `NativeVideoBridge` (JNI), `MetaSpatialAnchorStore`, `JsonProfileRepository`.
- **Por quê:** o domínio fica 100% testável no EditMode e em CI, sem headset. Dá para trocar a renderização (OVROverlay ↔ mesh), o transporte ou o SDK sem tocar nas regras.
- **Enforcement:** asmdefs com dependências unidirecionais `Presentation/Infrastructure → Application → Domain`.

### 8.2 Estado: Unidirectional Data Flow + Command
- `WorkspaceStore` é a **fonte única da verdade** (estado imutável ou com mudanças controladas).
- Toda alteração é um **Command**: `CreateScreen`, `RecreateScreen`, `MoveScreen`, `SetScreenDistance`, `ResizeScreen` (só escala), `SetScreenCurvature`, `ToggleScreenCurvature`, `ToggleScreenFrontAndCenter`, `RemoveScreen`, `ApplyProfile`, `SetFocus`, `SetFrameRateMode`, `DetachWindow`, `ReattachWindow`, `SetEdgeBehavior` + os Commands da zona de passthrough (seção 9.7).
- Commands passam pelo `CommandBus` → reducer/handler → novo estado → evento `StateChanged`.
- **Ganhos:** **undo/redo** de graça, log/replay para debug, perfis de layout = snapshot do estado (**Memento**), sincronização fácil com o host (o comando vira mensagem).

### 8.3 Views: Observer / MVP passivo
- `ScreenView` (MonoBehaviour) só **observa** o estado e **emite intenções** (ex.: "usuário arrastou o handle"). Nunca muda o estado direto.
- Toda entrada passa por **Adapters** que implementam a mesma interface (`IManipulationInput`): `MouseManipulationAdapter` (principal), `KeyboardShortcutAdapter` e `HandManipulationAdapter` (Meta Interaction SDK, alternativa). Todos alimentam o mesmo `ScreenManipulationController` → Command. **Mouse e mãos geram exatamente os mesmos Commands** (seção 10.10).

### 8.4 Injeção de dependência: VContainer
- Sem singletons `MonoBehaviour` nem `FindObjectOfType`. Um `LifetimeScope` monta tudo em `Composition/`.
- VContainer é leve e rápido e não gera lixo em runtime depois do boot.

### 8.5 Ciclo de conexão: State Machine
`Idle → Discovering → Connecting → Negotiating → Streaming ⇄ Degraded → Reconnecting → Streaming`
Cada estado é uma classe com `Enter/Exit/Tick`. Isso evita a "sopa de booleanos" e deixa reconexão e perda de Wi-Fi previsíveis.

### 8.6 Backends plugáveis: Strategy + Factory
- `ICaptureSource` (WGC monitor, WGC janela, DXGI)
- `IEncoder` (NVENC, AMF, QSV, software)
- `ITransport` (UDP, USB tether futuramente)
- A factory escolhe na inicialização conforme o hardware detectado e loga a escolha.

### 8.7 Pipeline de vídeo: Pipes & Filters + Producer/Consumer
- Host: `Capture ► Encode ► Packetize ► Send` como estágios independentes, cada um na sua thread, ligados por **ring buffers lock-free** de capacidade pequena (1–2 frames). **Descartar frame velho é melhor que enfileirar latência.**
- Headset: `Receive ► Reassemble ► Decode(Surface)`, tudo em código nativo.
- **Object Pool** para buffers de pacote e de frame: zero alocação em regime.

### 8.8 Frame rate por tela: Strategy + Chain of Responsibility
Cada `VirtualScreen` tem um `FrameRateMode` (parte do estado, salvo nos perfis):

| Modo | Comportamento | Uso típico |
|---|---|---|
| `Auto` (padrão) | 60 fps em foco, 10–15 fps fora de foco. Sobe automaticamente se detectar movimento contínuo | Trabalho geral |
| `Fixed(n)` | fps fixo escolhido pelo usuário (15 / 30 / 60 / 90), independente do foco | Filme, dashboard, câmera de segurança |
| `Video` | Atalho para `Fixed(60)` + bitrate alto + prioridade máxima de banda | Filme/série numa tela secundária |
| `Eco` | Teto de 5–10 fps | Chat, docs de referência, monitoramento |

- **Resolução do fps final** (Chain of Responsibility, a primeira regra que decidir vence): `ManualOverride` (Fixed/Video/Eco) → `MotionDetector` (o host usa os dirty rects do WGC e as estatísticas do encoder; se a tela muda continuamente por mais de 2 s, trata como vídeo) → `FocusPolicy` (**tela do cursor** como sinal principal, olhar como secundário) → padrão.
- **Orçamento global** (`BandwidthBudget`): a soma de fps × resolução × bitrate de todas as telas é limitada pela capacidade de decode e pela rede medidas na Fase 0. Prioridade na hora de cortar: **tela em foco > telas manuais (Video/Fixed) > Auto fora de foco > Eco**. Se o usuário fixar mais do que o orçamento aguenta, a UI avisa ("essa configuração pode causar travadas") em vez de degradar em silêncio.
- **UI:** menu de contexto por tela (ícone no handle) com os modos + um indicador discreto do fps real.
- O host aplica tudo via `RateController`, com mudanças suaves de bitrate para não gerar picos.

### 8.9 Protocolo: Versionamento explícito
- Toda mensagem FlatBuffers tem `protocol_version`. Handshake no `Negotiating`.
- Campos novos são sempre opcionais, para manter compatibilidade entre versões do host e do headset.

### 8.10 Decisões registradas: ADRs
Cada decisão estrutural (ex.: "H.264 no MVP", "OVROverlay external surface", "IddCx") vira um `docs/adr/NNNN-titulo.md` com contexto, decisão e consequências.

---

## 9. Zona de passthrough: o app sugere, o usuário ajusta

### 9.1 Princípio
O app **nunca impõe** a área de passthrough. Ele **sugere** um ponto de partida inteligente, e o usuário tem **controle total** para ajustar. O ajuste do usuário é **preservado** mesmo quando a sugestão muda (ex.: o teclado foi movido na mesa).

**Modelo central:**
```
Zona final = Sugestão (calculada do teclado/mesa) ⊕ Ajustes do usuário (offsets relativos)
```
Os ajustes são guardados **como deltas sobre a sugestão**, não como posição absoluta. Se o teclado anda 10 cm pra esquerda, a zona anda junto e mantém os ajustes (ex.: "+8 cm pro lado do mouse", "+5 cm de altura").

### 9.2 Como a sugestão é gerada (Strategy, em ordem de prioridade)

| Estratégia | Quando | Resultado |
|---|---|---|
| `KeyboardBasedSuggestion` | MRUK detectou teclado | Caixa sobre o `VolumeBounds` do teclado + margens padrão (abaixo) |
| `TablePlaneSuggestion` | Sem teclado, mas a mesa existe no Space Setup do Quest (MRUK, label de mesa) | Retângulo na borda da mesa, em frente ao usuário |
| `ManualPlacementSuggestion` | Nenhum dos dois | O usuário toca 2 cantos na mesa com o dedo/controle, e a zona nasce ali |

**Margens padrão da sugestão com teclado** (todas ajustáveis nas configurações):
- **Frente:** +8 cm (apoio dos pulsos)
- **Trás:** +4 cm
- **Lado do mouse:** +25 cm, no lado da **mão dominante** (configurada no onboarding; o app pode sugerir trocar se o hand tracking mostrar a outra mão parando fora do teclado com frequência)
- **Lado oposto:** +3 cm
- **Altura:** 25 cm acima da mesa (mãos visíveis enquanto digita)
- **Borda suave (feather):** 2 cm

### 9.3 O que o usuário pode ajustar

| Ajuste | Como (hand tracking ou controle) |
|---|---|
| **Largura/profundidade** | Arrastar **bordas** (1 eixo) ou **cantos** (2 eixos) com pinça |
| **Altura** | Handle no topo da caixa |
| **Mover** | Pinçar o centro e arrastar sobre o plano da mesa |
| **Girar** | Anel de rotação no eixo vertical (snap de 5°, desliga segurando) |
| **Formato** | Retângulo ou retângulo arredondado (raio ajustável). **Polígono livre** (desenhar contornando com o dedo) fica para a v1.2 |
| **Borda suave** | Slider de 0–8 cm (transição gradual entre virtual e real) |
| **Opacidade** | Slider de 30–100% (ex.: passthrough "fantasma" para quem quer imersão) |
| **Zonas extras** | Botão "+ zona" (ex.: caneca, celular, bloco de notas). Cada uma com os mesmos ajustes |
| **Âncora** | `Seguir teclado` (padrão) · `Fixa na mesa` (spatial anchor) · `Fixa no mundo` |
| **Resetar** | "Voltar à sugestão" descarta só os ajustes, sem apagar a zona |

**Precisão e conforto:**
- Snap de 1 cm (desliga segurando a pinça por mais de 0,5 s)
- Medidas em cm exibidas ao lado do handle durante o arraste
- Undo/redo de qualquer ajuste (Command pattern)
- Tamanho mínimo de 10×10 cm, para não "perder" a zona

### 9.4 Fluxo de UX
1. **Primeira vez:** o app detecta o teclado e mostra a zona sugerida com contorno pulsante + "Essa área está boa? [Usar] [Ajustar]".
2. **Modo edição** (menu do app → "Editar zona": com o mouse via `Ctrl+Alt+M` ou clicando no espaço vazio; com as mãos via menu de pulso): contorno com handles visíveis, **arrastáveis pelo cursor 3D do mouse** (seção 10.10) ou pela pinça, as telas ficam semitransparentes para não atrapalhar, e há um painel pequeno com sliders (feather, opacidade, formato).
3. **Sair da edição:** salva automaticamente no **perfil de layout** ativo. Cada perfil pode ter a sua zona (ex.: "Dev" com zona grande, "Filme" com zona mínima ou desligada).
4. **Uso normal:** handles escondidos, só o recorte suave.
5. **Atalhos rápidos:**
   - **Revelar ao olhar pra baixo** (opcional): a opacidade da zona sobe quando a cabeça inclina pra mesa
   - **Mostrar/ocultar zona** com um atalho de teclado (útil em filme)

### 9.5 Detecção e recuperação
- **Teclado perdido** (tracking caiu): a zona **congela** na última posição conhecida, sem sumir nem pular. Um ícone discreto indica "teclado não rastreado".
- **Teclado reencontrado:** a zona volta a seguir, com transição suave (lerp de ~200 ms), sem teleporte.
- **Teclado muito diferente do salvo** (outro teclado, outra mesa): pergunta "Recalcular sugestão? [Sim] [Manter a atual]".

### 9.6 Conflito com as telas
- **Invariante do domínio:** nenhuma tela ocupa o volume de uma zona. Se o usuário arrasta uma tela pra dentro, a zona fica destacada e a tela é **empurrada suavemente** para fora ao soltar.
- **Configuração avançada:** "permitir sobreposição". Nesse caso a tela fica por cima (limitação das overlays, seção 15.4), e a UI avisa.

### 9.7 Implementação (como encaixa na arquitetura)

**Domínio (C# puro, testável):**
```csharp
public sealed record PassthroughZone(
    ZoneId Id,
    ZoneShape Shape,            // Rect | RoundedRect(radius)  (Polygon na v1.2; o tipo já prevê a extensão)
    ZoneAnchorMode AnchorMode,  // FollowKeyboard | DeskAnchor | WorldAnchor
    ZoneSuggestion Suggestion,  // caixa base calculada (pose + size)
    ZoneAdjustment Adjustment,  // deltas do usuário: edges, height, offset, yaw
    float FeatherMeters,
    float Opacity,
    bool IsUserEdited);

// Zona final = Suggestion.Apply(Adjustment): função pura, coberta por testes
```

**Commands:**
`SuggestZone`, `AdjustZoneEdge(edge, delta)`, `AdjustZoneHeight`, `MoveZone`, `RotateZone`, `SetZoneShape`, `SetZoneFeather`, `SetZoneOpacity`, `SetZoneAnchorMode`, `AddZone`, `RemoveZone`, `ResetZoneToSuggestion`, `ToggleZoneVisibility`.

**Patterns:**
- **Strategy:** `IZoneSuggestionStrategy` (Keyboard / TablePlane / Manual), escolhida por **Chain of Responsibility** (a primeira que conseguir sugerir vence)
- **Decorator/composição:** `Suggestion ⊕ Adjustment`, onde a sugestão recalcula e o ajuste persiste
- **Memento:** a zona faz parte do snapshot do perfil
- **Observer:** `MRUKTrackable` (Added/Removed/pose) → `KeyboardTrackingAdapter` → Command `UpdateKeyboardPose` no domínio

**Renderização (Presentation):**
- Uma **malha de caixa arredondada** com o shader de **Passthrough Window** (seção 15.3), estendido com **feather via SDF** no fragment shader (alpha gradual nas bordas, em vez de corte seco).
- A malha é regenerada só quando a forma muda (fora do caminho quente). A pose é atualizada por Transform, sem alocação.
- Handles de edição: Meta Interaction SDK (`Grabbable` + `OneGrabTranslateTransformer` restrito ao eixo), instanciados só no modo edição.

**Testes:**
- EditMode: `Suggestion.Apply(Adjustment)` com teclado movido/girado, cantos arredondados, limites mínimos, invariante "tela não invade zona"
- Dispositivo: precisão da sugestão com 2–3 teclados reais, estabilidade ao perder/recuperar tracking

---

## 10. Telas: criação, tamanho, janela destacada, cursor espacial e manipulação com o mouse

### 10.1 Regra de tamanho: redimensionar é zoom, resolução nunca muda
- **No headset, redimensionar uma tela = mudar o tamanho físico dela no espaço, sempre em proporção.** A proporção fica travada: arrastar qualquer canto ou borda escala a tela inteira por igual, e a imagem nunca distorce.
- **A resolução nunca muda ao redimensionar.** Um monitor 2560×1440 continua 2560×1440 com 60 cm ou com 3 m de largura. Só fica maior ou menor no espaço.
- **Por quê:**
  1. Evita o problema confirmado de troca de modo em tempo real (tela preta, distorção, janelas deslocadas, seção 15.7).
  2. O Windows nunca reorganiza as suas janelas por causa de um resize no headset.
  3. O resize fica **instantâneo e suave** (é só a escala de uma compositor layer).
  4. Simplifica o domínio: `VirtualScreen` tem `Resolution` (imutável) + `WidthMeters` (a altura deriva da proporção).
- **Vale para janela destacada também:** redimensionar no headset é zoom. Para mudar o tamanho *real* da janela (mais conteúdo), você arrasta a borda da janela com o mouse, como no Windows. O recorte acompanha (seção 10.5).
- **Ajuda de legibilidade (v1.1, não entra no MVP):** ao redimensionar, um indicador discreto mostra se o texto está confortável naquela distância e tamanho (ex.: "texto pequeno demais", "ótimo").

### 10.2 Tela curva: ligar e ajustar a curvatura
Qualquer tela pode ser **plana ou curva**, e o usuário define **quanto** ela curva, por atalho, pelo mouse ou pelo menu. Assim como o zoom, curvar **nunca muda a resolução**: é só a forma da tela no espaço.

**Como funciona a curvatura:**
- A curva é **horizontal** (cilindro, como um monitor curvo de verdade). Curvatura vertical/esférica fica fora do MVP.
- A curvatura é um valor de **0% a 100%**:
  - **0% = plana**
  - **100% = envolvente**: o centro da curva fica na sua cabeça, então **todo ponto da tela está à mesma distância dos seus olhos**. É o mais confortável pra ler as bordas de telas largas (ultrawide).
  - Por baixo: raio da curva = distância da tela ÷ curvatura. Afastar ou aproximar a tela mantém a mesma sensação de curva.
- Pra facilitar, a UI também mostra o valor equivalente de monitor real (ex.: **"≈ 1800R"**, raio em mm, como vem na caixa de monitores curvos).
- A **largura física não muda** ao curvar (a tela "dobra", não encolhe), então zoom e curvatura são independentes.
- Presets: **Plana (0%)**, **Suave (30%)**, **Média (60%)**, **Envolvente (100%)**.

**Como controlar:**

| Por onde | Ação |
|---|---|
| **Atalho** `Ctrl+Alt+C` | Alterna **plana ↔ última curvatura usada** na tela focada (primeira vez: Média) |
| **Atalho** `Ctrl+Alt+[` / `Ctrl+Alt+]` | Diminui / aumenta a curvatura em passos de 10% (com `Shift`: passa pelos presets) |
| **Mouse na moldura** | Segurar o clique + `Shift` + girar a roda: ajuste contínuo (seção 10.10) |
| **Sem mirar a moldura** | Com o cursor em qualquer ponto da tela, `Ctrl+Alt+[` / `Ctrl+Alt+]` ajusta a curvatura daquela tela (a tela sob o cursor é a tela focada) |
| **Menu da tela** (clique direito na moldura) | "Curvatura" com os 4 presets e um slider |
| **Mãos** | Alça de curvatura no topo da moldura (arrastar pra frente curva, pra trás aplana) |
| **Seletor "Novo monitor"** | Escolher a curvatura já na criação (padrão: plana; ultrawide e super ultrawide sugerem Média) |

Durante o ajuste, a tela se curva **na hora** (preview local) e aparece o valor (ex.: "Média · 60% · ≈ 1800R"). Ao soltar, vira um único Command (`SetScreenCurvature`), então `Ctrl+Alt+Z` desfaz.

**O limite técnico (e como a UX lida com ele):**
- A Meta permite **no máximo 1 compositor layer cilíndrica por cena** (seção 15.4). A tela curva que estiver nessa layer fica com **nitidez máxima**; as outras curvas precisam ser desenhadas como malha curva no eye buffer, **um pouco menos nítidas**.
- **Regra de prioridade:** a layer cilíndrica vai pra **tela curva em foco** (a do cursor, seção 8.8). Quando o foco muda para outra tela curva, a layer troca de dono **no próximo frame, com um crossfade curto** pra não piscar. Telas planas continuam sempre em layers quad nítidas.
- Se a troca de dono se mostrar perceptível nos testes, o plano B é **"tela curva principal" fixa**, escolhida pelo usuário no menu. Decidir na Fase 2 com medição.
- A UI é honesta: um ícone discreto indica quando uma tela curva está no modo "nitidez reduzida", e o menu explica o motivo.
- Com curvatura muito alta em telas estreitas, o texto das bordas distorce sem ganho: o app limita a curvatura efetiva pelo arco (no máximo ~120° de arco) e avisa.

**Integração com o resto:**
- **Cursor espacial:** o `SpatialMap` já trata telas curvas (raycast na superfície cilíndrica, seção 10.11). A curvatura entra no hit-test da moldura e do conteúdo.
- **Zona de passthrough:** a invariante "tela não invade zona" (9.6) usa o volume real da tela curva.
- **Perfis:** a curvatura é salva por tela no perfil.

**Domínio:**
- `VirtualScreen.Curvature` (0..1, padrão 0) + `LastNonZeroCurvature` (pro toggle).
- Commands: `SetScreenCurvature(screenId, value)`, `ToggleScreenCurvature(screenId)`.
- **Invariante:** mudar a curvatura nunca altera `Resolution`, `Orientation` nem `WidthMeters`.
- Política de layer: `CylinderLayerPolicy` (**Strategy**: `FocusedCurvedScreen` padrão, `PinnedPrimaryCurvedScreen` plano B).

### 10.3 Criar monitor novo: seletor de proporção e resolução
A resolução é escolhida **uma vez, na criação**. É aí que o usuário decide "quero um ultrawide", "quero um vertical para código" etc.

**Como abrir:** botão "+ Monitor" no menu do app (mouse ou menu de pulso), ou `Ctrl+Alt+N`. O seletor abre com a última escolha pré-selecionada (`Enter` confirma direto) e é operado com o **cursor 3D do mouse** (seção 10.10), pelo teclado ou com as mãos.

**Tela do seletor (layout):**
```
┌──────────────────────── Novo monitor ─────────────────────────┐
│  Orientação:   [ ▭ Paisagem ]  [ ▯ Retrato ]      (tecla R)   │
│                                                               │
│  Proporção                                                    │
│  ┌──────┐ ┌──────┐ ┌────────┐ ┌────────────┐ ┌───┐ ┌───┐ ┌───┐│
│  │      │ │      │ │        │ │            │ │   │ │   │ │ ? ││
│  └──────┘ │      │ └────────┘ └────────────┘ └───┘ │   │ └───┘│
│   16:9    └──────┘   21:9         32:9       1:1   └───┘Custom│
│           16:10    Ultrawide  Super UW              4:3       │
│                                                               │
│  Resolução            ○ 2560×1080   ● 3440×1440   ○ 5120×2160 │
│  Curvatura:  ● Plana  ○ Suave  ○ Média  ○ Envolvente          │
│  Custo  ▮▮▮▯▯  (banda/decoder)      Nitidez ▮▮▮▮▯             │
│                                                               │
│  Favoritos:  ★ Dev vertical 1440×2560   ★ Filme 3840×2160     │
│                              [ Cancelar ]  [ Posicionar ▶ ]   │
└───────────────────────────────────────────────────────────────┘
  Com "Retrato" ativo, todos os previews giram 90° (16:9 vira 9:16,
  21:9 vira 9:21...) e as resoluções trocam largura ↔ altura.
```

**Orientação: paisagem ou retrato, pra qualquer proporção**
- Uma chave **Paisagem / Retrato** no topo do seletor vale pra **todas** as proporções: 16:9 ↔ 9:16, 16:10 ↔ 10:16, 21:9 ↔ 9:21, 32:9 ↔ 9:32, 4:3 ↔ 3:4. O quadrado (1:1) não muda.
- Ao alternar, os **previews giram na hora** e as resoluções trocam largura por altura (ex.: 2560×1440 → 1440×2560).
- Atalho: tecla `R` alterna a orientação com o seletor aberto.
- **Como é criado:** o monitor virtual é criado **já com a resolução em pé** (ex.: 1440×2560). **Não se usa a rotação de tela do Windows** (girar 90° nas configurações de vídeo): o Windows e os apps enxergam um monitor vertical nativo, a captura e o encoder recebem a imagem já na orientação certa, e nada precisa ser girado no headset.
- **Trocar a orientação de um monitor que já existe** = "Recriar com outra resolução…" (seção 10.4), escolhendo a outra orientação. Continua valendo a regra: nada muda sozinho, e redimensionar nunca muda a resolução nem a orientação.
- Retrato é ótimo pra código, chat, documentação, feeds e PDFs. Os extremos (9:21 e 9:32) ficam disponíveis mas vêm com um aviso de "muito alto: confira o conforto do pescoço".

**Preview de proporção:** cada opção é um **retângulo desenhado em escala**, com o nome e a razão embaixo. Em paisagem, todos têm a mesma altura e larguras proporcionais; em retrato, a mesma largura e alturas proporcionais. Olhando, você entende a forma na hora: o quadrado, o "tijolo" 16:9, a "faixa" ultrawide, a "tira" vertical.

**Catálogo de proporções e resoluções** (mostradas em paisagem; em retrato, largura e altura se invertem):

| Proporção | Nome | Resoluções oferecidas | Uso típico em paisagem | Uso típico em retrato |
|---|---|---|---|---|
| **16:9** | Padrão | 1920×1080 · 2560×1440 · 3840×2160 | Geral, filme | Código, chat, feeds |
| **16:10** | Produtividade | 1920×1200 · 2560×1600 | Código, docs | Documentos longos, PDFs |
| **21:9** | Ultrawide | 2560×1080 · 3440×1440 · 5120×2160 | Timeline, várias janelas lado a lado | Logs, terminais compridos |
| **32:9** | Super ultrawide | 3840×1080 · 5120×1440 | Substituir 2 monitores | Casos extremos (com aviso) |
| **1:1** | Quadrado | 1440×1440 · 2048×2048 | Dashboards, design | (igual) |
| **4:3** | Clássico | 1600×1200 · 2048×1536 | Apps antigos, leitura | Leitura, revistas, partituras |
| **Custom** | Personalizado | Largura × altura livres | Qualquer caso | Qualquer caso |

**Personalizado:**
- Dois campos (largura, altura) + o preview do retângulo atualizando ao vivo + a razão calculada (ex.: "≈ 2,39:1"). O botão de orientação troca os dois campos entre si.
- Validação: mínimo 640×480 (ou 480×640 em retrato), **múltiplos de 8** (encoder), máximo dentro do limite do encoder/decoder (ver o aviso abaixo).
- "Salvar como favorito" com nome (a orientação é salva junto).

**Indicadores na escolha:**
- **Custo** (barras): quanto essa resolução pesa no `BandwidthBudget` e no decoder. Ajuda a não criar 3 monitores 4K sem perceber.
- **Nitidez** (barras): estimativa de quão nítido o texto fica no tamanho padrão de criação. Resolução alta demais pra um monitor pequeno é desperdício, porque o headset não mostra detalhe menor que um pixel do display dele.
- ⚠️ **Limite de encoder (a validar):** encoders H.264 de hardware costumam limitar cada dimensão em ~4096 px. Resoluções maiores **em qualquer orientação** (ex.: 5120×1440 em paisagem ou 1440×5120 em retrato) provavelmente precisam de **HEVC**. No MVP (H.264), essas opções aparecem marcadas "requer HEVC" e ficam desabilitadas até a v1.1, ou o host divide o monitor em 2 streams (decidir no spike da Fase 2).

**Depois de escolher: posicionar no espaço**
1. "Posicionar ▶" mostra um **contorno fantasma em tamanho real** na frente do usuário, com a proporção escolhida.
2. O usuário ajusta **onde** e **quão grande** (zoom proporcional, regra 10.1): com o mouse (arrastar move, roda afasta, `Ctrl`+roda redimensiona, igual à seção 10.10), com as setas do teclado ou com pinça.
3. Confirma, e só então o host cria o monitor virtual **com a resolução exata** (padrão comprovado em produção, seção 15.7) e o stream começa.

**Domínio e Commands:**
- `ScreenSpec { Resolution, AspectRatio, Orientation (Landscape | Portrait), InitialCurvature, Label, IsFavorite }`. A `Resolution` já vem na orientação final (ex.: 1440×2560); `Orientation` serve pra UI e pros favoritos
- `CreateScreen(spec, pose, widthMeters)` · `ResizeScreen(widthMeters)` (só escala) · `SaveScreenSpecFavorite(spec, name)`
- **Invariante:** `ResizeScreen` **nunca** altera `Resolution` nem `Orientation`. Teste de domínio garante.

### 10.4 Mudar a resolução depois (ação explícita, nunca automática)
- No menu da tela: **"Recriar com outra resolução…"** abre o mesmo seletor (dá pra trocar resolução, proporção e/ou orientação).
- O host cria um monitor novo com a resolução nova, **move as janelas** do antigo pra ele (mantendo as posições relativas), remove o antigo e **mantém a posição e o tamanho físico** da tela no espaço (a nova proporção é centralizada no mesmo lugar).
- É raro e deliberado. Uma confirmação avisa: "As janelas desse monitor serão movidas pro novo".

### 10.5 Janela destacada ("arrancar" uma janela)
**Como funciona:**
1. O usuário destaca a janela ativa (atalho `Ctrl+Alt+D` ou pinça-e-puxa na barra de título dentro do headset).
2. O host cria um **monitor virtual dedicado** ("palco") com resolução folgada (maior que a janela), move a janela para ele e a posiciona no canto (sem maximizar).
3. O host captura **só o retângulo da janela** (recorte), não o monitor inteiro. Para o Windows, a janela vive num monitor de verdade: clique, arraste, scroll, menus e diálogos funcionam normalmente.
4. No headset, ela vira uma `VirtualScreen` como qualquer outra (move, resize, fps por tela, perfis).
5. **Reanexar** (`Ctrl+Alt+Shift+D` ou menu da tela) devolve a janela à posição original e remove o monitor virtual.

**Detalhes:**
- **Tamanho: "monitor-palco com recorte" (seção 15.7).** O palco é criado grande (ex.: 3840×2160) e **nunca muda de modo**. Redimensionar a tela destacada **no headset é só zoom** (regra 10.1). Para mudar o tamanho *real* da janela, o usuário arrasta a borda dela com o mouse, como faria no Windows, e o host ajusta o recorte da captura automaticamente. A proporção da tela no headset acompanha a nova forma da janela, mantendo a largura física.
- **Limite:** a janela não pode ficar maior que o palco. O host impede (`WM_GETMINMAXINFO`/`SetWindowPos`) e avisa.
- **O cursor espacial usa o retângulo da janela**, não o do monitor-palco. A área vazia do palco é tratada como "fora da tela", e o cursor nunca se perde nela.
- **Perfis:** a janela destacada é salva pela identidade do app (nome do processo + padrão de título). Ao aplicar o perfil, o host re-destaca os apps que estiverem abertos (ex.: perfil "Dev" sempre destaca o terminal à esquerda e o Spotify acima).
- **Diálogos e popups** do app destacado aparecem no monitor dele, então aparecem na tela destacada.
- **Fallback sem driver** (limite de monitores atingido, ou driver indisponível): a janela fica num **monitor de "bastidores"** compartilhado e é capturada por janela (WGC). O cursor espacial usa o retângulo da janela nesse monitor. Funciona, mas com mais risco de sobreposição com outras janelas.
- **Custo:** cada janela destacada = 1 monitor virtual + 1 stream + 1 compositor layer. Entra no `BandwidthBudget` e no teto de layers (seção 15.4). Janelas pequenas custam bem menos banda que monitores inteiros.

### 10.6 Cursor espacial (roteamento do mouse em 3D)

**O problema:** o Windows só entende monitores como **retângulos num plano 2D, colados borda com borda**. Ele não sabe que uma janela está no canto superior esquerdo, afastada, maior que o desktop ou flutuando acima dele. Se dependermos só do Windows, o mouse vai para onde a configuração de monitores mandar, **não para onde a tela está no 3D**.

**Regra de ouro do produto:** o mouse sempre vai na direção em que o usuário **vê** a tela.

**Arquitetura:** o host é dono da travessia do mouse entre telas. O Windows vira só plano B.

```
Headset ──(layout 3D: pose, tamanho, resolução, id do monitor Windows)──► Host
Host: SpatialCursorRouter
   ├─ Raw Input      → delta real do mouse (mesmo quando o cursor está preso na borda)
   ├─ Hook WH_MOUSE_LL → intercepta o movimento que sairia da tela atual
   ├─ SpatialMap     → todas as telas projetadas num mapa angular (yaw/pitch) em volta da cabeça
   └─ SetCursorPos   → coloca o cursor no ponto de entrada da tela de destino
```

**Algoritmo de travessia:**
1. **Mapa angular:** o headset envia a pose de cada tela. O host projeta todas numa esfera em volta da cabeça do usuário (coordenadas yaw/pitch). Esse é o espaço em que o usuário **percebe** direção: "esquerda", "acima" e "diagonal" significam o mesmo que ele vê.
2. **Detecção de saída:** o hook de baixo nível vê cada movimento. Quando o cursor está na borda da tela atual e o delta do Raw Input aponta para fora, o movimento é **interceptado** (engolido), antes de o Windows mandar o cursor para o monitor que *ele* acha que é o vizinho.
3. **Raycast angular:** do ponto de saída, traça-se um raio no mapa angular **na direção do movimento do mouse** (convertida pela orientação da tela atual).
4. **Alvo:** a primeira tela que o raio atinge (até um limite, ex.: 60°) é o destino. O ponto de entrada é a interseção do raio com a borda dela.
5. **Teleporte:** converte o ponto de entrada para pixels do monitor Windows de destino (considerando o DPI de cada monitor) e chama `SetCursorPos`.
6. **Nenhum alvo:** comportamento de borda (ver 10.7).

**Os casos que o usuário pediu, verificados pelo algoritmo:**
| Layout no 3D | Movimento | Resultado |
|---|---|---|
| Janela à esquerda do desktop | Mouse pra esquerda | Entra pela borda direita da janela, na altura correspondente |
| Janela no canto superior esquerdo | Mouse na diagonal (↖) | O raio aponta pra lá e entra na janela |
| Janela grande acima do desktop | Mouse pra cima | Entra pela borda de baixo da janela, na posição horizontal correspondente |
| Voltando da janela pro desktop | Movimento inverso | Mesmo algoritmo, cursor volta pro ponto coerente |

**Plano B (Windows):** o host também configura a disposição dos monitores do Windows (`SetDisplayConfig`) como uma **aproximação 2D** do layout 3D. Se o roteador falhar ou estiver desligado, o mouse ainda vai mais ou menos pro lado certo.

### 10.7 Regras de comportamento (configuráveis)
- **Vão entre telas:** `Pular vão` (padrão, o cursor atravessa o espaço vazio) ou `Parede` (o cursor para na borda e só atravessa com um "empurrão" extra).
- **Histerese:** após uma travessia, a borda de entrada ignora movimento de volta por ~80 ms. Evita o cursor ficar "pingando" entre duas telas.
- **Resistência de borda (ligada por padrão, ajustável):** exige um pequeno empurrão para sair da tela (pra moldura ou pra outra tela). Preserva o gesto de "jogar" o mouse na borda pra acertar barras de rolagem, barra de tarefas e menus colados na borda (seção 10.10).
- **Sobreposição no 3D:** se duas telas estão na mesma direção, vence a **mais à frente** (mais próxima do usuário).
- **Escalas diferentes** (100% numa, 150% noutra): o ponto de entrada é calculado em coordenadas angulares, então o cursor não "pula" de posição relativa.
- **Foco = tela do cursor:** a tela onde o cursor está é a tela em foco para a `FocusPolicy` (seção 8.8). O mouse e o frame rate trabalham juntos.

### 10.8 Cursor desenhado localmente no headset
O cursor dentro do vídeo chega com a latência do stream (40–80 ms), e isso é perceptível ao mover o mouse. Solução:
- O host envia **posição do cursor + forma (ícone)** por um canal UDP leve, a cada movimento (até 240 Hz, poucos bytes).
- O host **desliga o cursor na captura** (WGC permite capturar sem o cursor).
- O headset desenha o cursor **por cima da tela correta**, na posição mais recente. A resposta fica praticamente imediata, mesmo com o vídeo atrasado.
- Formas especiais (texto, redimensionar, carregando) são enviadas uma vez e ficam em cache.

### 10.9 Atalhos globais de teclado
O host registra atalhos globais (`RegisterHotKey`) e os envia ao headset como Commands. Todos são **reconfiguráveis** (para evitar conflito com IDEs e outros apps).

⚠️ **Conflito conhecido:** em algumas máquinas com gráficos Intel, `Ctrl+Alt+setas` gira a tela do Windows. No primeiro uso, o host detecta se `RegisterHotKey` falhou (atalho já ocupado) e sugere uma alternativa (ex.: `Win+Alt+setas`).

| Atalho (padrão) | Ação |
|---|---|
| `Ctrl+Alt+←/→/↑/↓` | Mover a tela focada |
| `Ctrl+Alt+Shift+←/→/↑/↓` | Redimensionar a tela focada |
| `Ctrl+Alt+PgUp/PgDn` | Aproximar/afastar a tela focada |
| `Ctrl+Alt+D` / `Ctrl+Alt+Shift+D` | Destacar a janela ativa / reanexar |
| `Ctrl+Alt+N` | Abrir o seletor "Novo monitor" (`Enter` repete a última escolha) |
| `Ctrl+Alt+1…9` | Aplicar perfil de layout 1–9 |
| `Ctrl+Alt+F` | Alternar o modo de fps da tela focada (Auto → Video → Eco → Fixed) |
| `Ctrl+Alt+P` | Mostrar/ocultar a zona de passthrough |
| `Ctrl+Alt+Z` / `Ctrl+Alt+Y` | Desfazer / refazer (layout) |
| `Ctrl+Alt+C` | Tela focada: alternar **plana ↔ curva** (volta à última curvatura usada) |
| `Ctrl+Alt+[` / `Ctrl+Alt+]` | Diminuir / aumentar a curvatura da tela focada (10% por passo; com `Shift`, pula entre os presets) |
| `Ctrl+Alt+M` | Abrir o menu do app (onde o cursor 3D estiver) |
| `Ctrl+Alt` **segurado** + mouse | Modo manipulação: arrastar move, roda afasta/aproxima, `+Shift`+roda redimensiona, botão direito arrastando redimensiona (seção 10.10) |
| `Ctrl+Alt+Espaço` | (v1.1) Olhar e pular: cursor vai para a tela que você está olhando |

### 10.10 Mouse-first: mover, afastar e redimensionar as telas com o mouse

**Princípio:** tudo que dá pra fazer com as mãos dá pra fazer com **mouse + teclado**, e o **mouse é o caminho principal**. Quem trabalha não quer tirar a mão do mouse pra fazer gestos no ar. As mãos continuam como alternativa, nunca obrigatórias.

**O problema a resolver:** normalmente o clique do mouse vai pro app do Windows. O app precisa saber quando o usuário quer mexer **na tela** (mover, afastar, redimensionar) e não **no conteúdo**. São duas formas, que convivem.

#### A. Moldura da tela (fácil de descobrir, pra qualquer um)
- Cada tela tem uma **moldura fina do lado de fora** da área do Windows: uma barra superior mais grossa (tipo barra de título), bordas finas nas laterais e embaixo, e alças nos cantos.
- A moldura fica **invisível até o cursor chegar perto** (aparece suave), pra não poluir o visual.
- Quando o cursor sai pela borda da tela, ele **pousa na moldura** antes de seguir pra próxima tela (cursor espacial, 10.6). Ali ele vira o cursor 3D desenhado pelo headset (10.8), e o host **engole cliques e scroll**: nada chega ao Windows.
- Pra não atrapalhar quem "joga" o mouse na borda (barras de rolagem, barra de tarefas, menus colados na borda), a saída da tela pra moldura tem uma **resistência de borda**: o cursor para na borda e só sai com um empurrão extra (10.7). Quem só atravessa pra outra tela passa direto pela moldura, sem parar.

| Ação na moldura | Resultado |
|---|---|
| Segurar o clique e **arrastar** | **Move** a tela ao seu redor (ela mantém a distância e continua virada pra você) |
| Segurar o clique e **girar a roda** | **Afasta/aproxima** a tela, mantendo o tamanho físico |
| Segurar o clique + `Ctrl` e girar a roda | **Redimensiona** (zoom proporcional, regra 10.1) sem mudar a distância |
| Arrastar um **canto** | **Redimensiona** proporcionalmente, com o canto oposto fixo |
| Segurar o clique + `Shift` e girar a roda | Ajusta a **curvatura** continuamente (plana → envolvente, seção 10.2) |
| **Duplo clique** na barra superior | Traz a tela pra **frente e centro** do seu olhar. Outro duplo clique devolve à posição anterior |
| **Clique direito** na moldura | **Menu da tela**: modo de fps, destacar/reanexar, recriar com outra resolução, travar posição, remover |
| Soltar o botão | Confirma. Vira **um único Command**, então `Ctrl+Alt+Z` desfaz o movimento inteiro |

#### B. Tecla modificadora (rápido, pra quem já sabe)
Segurando `Ctrl+Alt` (configurável), o mouse manipula **a tela que está sob o cursor, em qualquer ponto dela**, sem precisar mirar a moldura. Funciona como nos gerenciadores de janela do Linux:
- `Ctrl+Alt` + arrastar com o botão esquerdo → **mover**
- `Ctrl+Alt` + girar a roda → **afastar/aproximar**
- `Ctrl+Alt+Shift` + girar a roda → **redimensionar** (zoom)
- `Ctrl+Alt` + arrastar com o botão direito → **redimensionar**
- Enquanto a tecla está segurada, a moldura aparece (sinal de "modo manipulação") e nenhum clique chega ao app.

#### C. Cursor 3D livre (fora das telas)
- Quando o cursor sai de uma tela e não há outra tela naquela direção, ele vira um **cursor 3D livre** no espaço, movido pelo Raw Input em coordenadas angulares (sensibilidade configurável).
- Com ele, o mouse opera **toda a interface do app**:
  - o **menu do app** (`Ctrl+Alt+M` ou clique no espaço vazio), que equivale ao menu de pulso das mãos
  - o seletor "Novo monitor" e o contorno fantasma de posicionamento (10.3)
  - as alças do modo edição da zona de passthrough (9.3–9.4)
- O cursor real do Windows fica parado na última borda, e os cliques são engolidos pelo host, então **nada acontece no Windows por acidente**.

#### Comportamento e conforto
- **Movimento em esfera:** arrastar move a tela sobre uma esfera centrada na sua cabeça, então ela nunca fica de lado nem "foge". Segurando `Alt` durante o arraste, move num plano, sem girar.
- **Snap:** ao mover, a tela gruda nas bordas das vizinhas, alinha centros e topos, e respeita a zona de passthrough (invariante 9.6). `Shift` segurado desliga o snap.
- **Distância:** de 0,4 m a 5 m. Cada passo da roda é pequeno quando a tela está perto (~5 cm) e maior quando está longe (escala logarítmica): preciso de perto, rápido de longe. Sensibilidade configurável.
- **Resposta imediata:** durante o arraste, o headset move a tela na hora (preview local, sem esperar a rede nem o Command). O Command é gerado ao soltar.
- **Contra clique acidental:** na moldura, o arraste só começa depois de segurar ~100 ms ou mover mais de 3 px. Um clique simples não faz nada; só duplo clique e clique direito têm ação.

#### Mãos (alternativa)
Os mesmos gestos pelo Meta Interaction SDK: pinçar a moldura pra mover, puxar as alças pra redimensionar, puxar/empurrar pra mudar a distância. Geram **exatamente os mesmos Commands** do mouse.

#### Implementação
- **Host (`Host.Input/`):** um `ManipulationInterceptor` dentro do hook. Quando o cursor está numa moldura ou no espaço livre, ou quando a tecla modificadora está segurada, ele engole botões e scroll e manda um `ManipulationEvent { kind, delta, wheel, modifiers, targetScreenId }` pro headset, pelo mesmo canal UDP rápido do cursor.
- **`SpatialMap`:** o hit-test passa a distinguir três tipos de superfície: `ScreenContent`, `ScreenFrame` (barra, borda, canto) e `FreeSpace`.
- **Headset:** `MouseManipulationAdapter`, `KeyboardShortcutAdapter` e `HandManipulationAdapter` implementam `IManipulationInput` (**Adapter**) e alimentam o `ScreenManipulationController`, uma **State Machine**: `Idle → Hover(moldura) → Pressed → Dragging | Wheeling → Commit`. No commit gera o Command (`MoveScreen`, `SetScreenDistance`, `ResizeScreen`, `SetScreenCurvature`).
- **Testes:**
  - domínio: movimento na esfera, snap, limites de distância, escala da roda
  - integração: sequências sintéticas de mouse (`SendInput`) → Commands esperados, e verificação de que nenhum evento vazou pro Windows
  - dispositivo: montar 3 layouts do dia a dia **só com o mouse**, cronometrado

### 10.11 Implementação (como encaixa na arquitetura)

**Host (novos projetos):**
- `Host.Input/`: `SpatialCursorRouter`, `RawInputReader`, `LowLevelMouseHook`, `ManipulationInterceptor`, `HotkeyService`, `CursorStreamer`
- `Host.Display/`: `VirtualDisplayManager` (cria/remove/recria monitores virtuais com a resolução exata; **nunca troca modo ao vivo**), `DisplayTopologySync` (plano B com `SetDisplayConfig`), `WindowDetacher` (mover para o palco, posicionar, recortar, restaurar)
- `Host.Domain/`: `SpatialMap` + algoritmo de raycast angular. **Puro, sem Windows API, 100% testável.**

**Patterns:**
- **Strategy:** `IEdgeBehavior` (`JumpGap`, `Wall`, `Resistance`) e `IDetachStrategy` (`DedicatedMonitor`, `StagingMonitor`)
- **Mediator:** `SpatialCursorRouter` coordena Raw Input, hook, mapa e `SetCursorPos` sem que eles se conheçam
- **Command:** `DetachWindow`, `ReattachWindow`, `SetEdgeBehavior` e os atalhos viram Commands no `CommandBus` do headset (undo/redo incluso)
- **Observer:** mudança de layout no headset → mensagem `LayoutChanged` → host reconstrói o `SpatialMap` e o plano B

**Regras técnicas (Windows):**
- O processo do host roda com **Per-Monitor DPI Awareness v2**, para as coordenadas não distorcerem entre monitores com escalas diferentes.
- O hook de baixo nível roda numa **thread dedicada** com message loop próprio e responde em microssegundos. O Windows desativa hooks lentos, e nada pesado roda ali.
- **Janelas de apps elevados (administrador):** o Windows (UIPI) bloqueia controlar o cursor sobre elas a partir de um processo comum. O host oferece um **modo elevado opcional** e, sem ele, avisa quando o cursor encontra um app elevado.
- **Jogos e apps que prendem o cursor** (`ClipCursor`/modo relativo): o roteador **desliga automaticamente** enquanto o cursor estiver preso.

**Testes:**
- Unitários (`Host.Domain`): bateria de layouts (esquerda, direita, acima, abaixo, diagonais, vãos, sobreposição, telas giradas/curvas, DPIs misturados) × direções → ponto de entrada esperado. **É o critério de aceite do pilar C.**
- Integração: harness que move o mouse sinteticamente (`SendInput`) e verifica a posição final.
- Dispositivo: teste de uso real com 3–5 layouts do dia a dia.

---

## 11. Orçamento de performance (Definition of Done técnica)

| Métrica | Alvo |
|---|---|
| Frame rate do app | 90 Hz estáveis (72 Hz como mínimo aceitável) |
| Alocação GC por frame (Unity, regime) | **0 bytes** |
| Latência glass-to-glass (PC → olho), LAN Wi-Fi 6 | **< 50 ms** (meta), < 80 ms (aceitável) |
| Tela em foco | 1440p @ 60 fps |
| Telas fora de foco (modo Auto) | 10–15 fps, bitrate reduzido |
| Tela em modo Video/Fixed | fps escolhido, **estável** (sem cair), com áudio sincronizado (dessincronia < 45 ms) |
| Nº de telas | MVP: 4. Meta: 6+ com FocusPolicy. **Teto técnico: 15 compositor layers por cena, no máximo 1 cilíndrica** (ver seção 15.4) |
| Texto | Legível em fonte 12 pt a ~1 m de distância virtual |
| Cursor (desenhado localmente) | < 20 ms de atraso percebido; travessia entre telas sem pulos |
| Hook do mouse | Processamento por evento em microssegundos (nunca bloquear a thread do hook) |
| Manipulação com o mouse (mover/afastar/redimensionar) | Tela acompanha o mouse com < 20 ms de atraso percebido (preview local); zero cliques de manipulação vazando pro Windows |

**Ferramentas:** OVR Metrics Tool (HUD), Unity Profiler (Deep Profile off, Memory Profiler), Perfetto e RenderDoc para GPU, e um **harness de latência**: o host desenha um timestamp na tela e o headset compara com o relógio sincronizado (log em CSV em `docs/measurements/`).

---

## 12. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Limite de decoders de hardware simultâneos no Quest 3 | Medir cedo (Fase 0). Se estourar, usar FocusPolicy e/ou **atlas** (várias telas de baixa prioridade num único stream) |
| Usuário fixa várias telas em 60 fps e estoura o orçamento | `BandwidthBudget` com prioridade clara + aviso na UI. Nunca degradar a tela em foco |
| Assinatura do driver de monitor virtual para distribuição | **Resolvida pela decisão D3** (Virtual Display Driver, já assinado). Se um dia for preciso driver próprio: certificado EV + atestação Microsoft. Captura **por janela** (WGC) segue como modo sem driver |
| Wi-Fi congestionado | Bitrate adaptativo, FEC e, futuramente, USB tether |
| Keyboard Tracking do MRUK falhar com algum teclado ou iluminação | Cadeia de sugestão cai pra `TablePlane` → `Manual`. Zona congela ao perder o tracking (seção 9.5). Testar na Fase 0 com os teclados que você tem |
| Mouse não é rastreado pelo SDK | Margem de +25 cm no lado da mão dominante na sugestão, ajustável pelo usuário |
| Telas (overlays) cobrindo a zona de passthrough | Invariante do domínio "tela não invade zona" (seção 9.6). Testar Depth Buffer Testing entre layers na Fase 0 |
| Limite de 15 compositor layers / 1 cilindro | Telas em Eco renderizadas no eye buffer. A única layer cilíndrica vai pra **tela curva em foco** (`CylinderLayerPolicy`, seção 10.2), com crossfade na troca; as outras curvas usam malha no eye buffer. Plano B: tela curva principal fixa. Medir na Fase 2 |
| Texto ainda borrado | Compositor layers + resolução nativa por tela + supersampling só na tela em foco |
| Cursor espacial com comportamento estranho em layouts exóticos | Algoritmo puro em `Host.Domain` com bateria grande de testes. Plano B via `SetDisplayConfig`. Atalho para desligar o roteador na hora |
| UIPI bloqueia o cursor sobre apps de administrador | Modo elevado opcional no host + aviso na UI |
| Windows desativa hook lento | Thread dedicada, zero trabalho pesado no callback, monitoramento de tempo por evento |
| Troca de resolução do monitor virtual em tempo real (confirmado como problemática, seção 15.7) | **Eliminada por design:** resize é só zoom (regra 10.1). Resolução definida na criação. Mudar depois = recriar o monitor (ação explícita, seção 10.4) |
| Resoluções acima de ~4096 px em qualquer dimensão (largura em paisagem, altura em retrato) não suportadas pelo H.264 de hardware | Marcadas "requer HEVC" no seletor até a v1.1, ou dividir em 2 streams. Confirmar limites do encoder e do decoder do Quest 3 no spike da Fase 2 |
| Moldura "roubar" o gesto de jogar o mouse na borda (barras de rolagem, barra de tarefas, menus) | Resistência de borda: o cursor para na borda e só vai pra moldura com um empurrão extra. Configurável |
| Tecla modificadora (`Ctrl+Alt` + mouse) conflitar com atalhos de algum app (ex.: multi-cursor de IDEs) | Configurável. Primeiro uso sugere a combinação. Lista de apps em que o modo modificador fica desligado |
| Clique de manipulação vazar pro app do Windows (ou clique normal ser engolido) | Estado explícito no `ManipulationInterceptor`, feedback visual da moldura, testes de integração com `SendInput` |
| Jogos/anti-cheat que não gostam de hooks ou `SetCursorPos` | Roteador desliga sozinho quando o cursor está preso (`ClipCursor`) ou o app está em tela cheia exclusiva. Lista de exceções configurável |

---

## 13. Regras para o Claude Code (copiar para `CLAUDE.md`)

- Respeitar as fronteiras: **Domain não referencia UnityEngine**, nem APIs Windows/Android.
- Toda mudança de estado da workspace é um **Command** via `CommandBus`. Nada de mutação direta a partir de views.
- **Zero alocação** em `Update`/`LateUpdate` e no caminho de vídeo: sem LINQ, sem closures, sem `string` concatenada, sem `new` em loop. Usar pools e `Span`/`NativeArray`.
- **O frame de vídeo nunca atravessa o C#.** Decode → Surface da compositor layer, sempre em código nativo.
- Sem singletons nem `FindObjectOfType`. Dependências via VContainer.
- Todo código de domínio vem com teste (EditMode NUnit / xUnit no host).
- Mensagens de rede só via schemas em `/protocol`. Não serializar JSON ad hoc no caminho quente.
- Toda decisão estrutural nova → criar ADR em `docs/adr/`.
- **O algoritmo do cursor espacial (`SpatialMap`) é código puro** em `Host.Domain`, sem Windows API. Todo bug de cursor vira um caso de teste antes de ser corrigido.
- **O callback do hook de mouse nunca faz trabalho pesado**: só lê, decide com dados já prontos e retorna.
- **`ResizeScreen` nunca altera a resolução.** O host nunca troca o modo de um monitor virtual vivo. Resolução só muda via `RecreateScreen` (ação explícita do usuário).
- **Mouse-first:** toda ação do app tem que ser executável **só com mouse + teclado**. Mãos são um adapter a mais, nunca o único caminho. Toda funcionalidade nova vem com o caminho do mouse definido.
- **Cliques de manipulação nunca vazam pro Windows**, e cliques normais nunca são engolidos. Todo bug desse tipo vira teste de integração antes da correção.
- **Escopo:** nada fora dos três pilares do MVP (seção 4) entra antes da v1.0. Ideias novas vão para o backlog (seção 4.3).
- **Pacotes Unity:** os dois grupos da seção 6.4 (**VR/Meta** e **IA: AI Assistant/Unity MCP**) são obrigatórios e instalados pelo Claude Code via `Packages/manifest.json`, com versões exatas. Nunca adicionar outra pilha de interação XR (ex.: XR Interaction Toolkit) nem templates da Unity de VR/MR: a interação é do Meta XR SDK.
- **Unity pelo terminal:** use o **Unity CLI** (`unity status`, `unity command …`) quando o Editor estiver aberto com o pacote Pipeline. Se o CLI falhar ou não suportar a versão do Editor, use `Unity.exe -batchmode -quit -projectPath headset-unity -runTests` / `-executeMethod`. **Nunca** use `unity command eval` na branch `main`: só em branch de trabalho, com o estado commitado antes.
- Commits pequenos, com um propósito cada. PR descreve o impacto em latência/performance quando tocar o pipeline.

---

## 14. Roadmap

### Fase 0: Spike de viabilidade (o mais importante)
Objetivo: **provar o caminho de vídeo e medir latência** antes de qualquer UI.
- Host: captura **1 monitor** (WGC) → H.264 hardware (low-latency) → UDP.
- Headset: `.aar` recebe → `MediaCodec` → **Surface de um `OVROverlay` external surface**, numa cena Unity mínima.
- Harness de latência + teste de **quantos decoders simultâneos** o Quest 3 aguenta (1, 2, 4, 6 streams).
- **Saída:** `docs/measurements/fase0.md` com números e um ADR confirmando (ou revisando) a arquitetura.

### Fase 1: Esqueleto limpo
- Monorepo, asmdefs, VContainer, Domain + CommandBus + WorkspaceStore com testes.
- Schemas FlatBuffers, handshake versionado, state machine de conexão, descoberta mDNS.

### Fase 2: Multi-monitor livre
- **Spike do driver (primeiro item da fase, 2–3 dias):** instalar o Virtual Display Driver num PC de teste e medir:
  - criar/remover monitores em runtime **com resolução exata** e controle programático (config/pipe), e quantos monitores simultâneos
  - criar um monitor **em retrato nativo** (ex.: 1440×2560) e conferir captura e encode sem rotação
  - **recriar** um monitor com outra resolução ou orientação, movendo as janelas junto (seção 10.4)
  - o **monitor-palco com recorte** (redimensionar a janela pelo Windows + recorte da WGC)
  - o **limite de largura do encoder H.264** da GPU e do decoder do Quest 3 (decide o destino das opções acima de 4096 px no seletor)
  - Resultado vira ADR: confirma a decisão D3 ou justifica driver próprio.
- **Seletor "Novo monitor"** (orientação paisagem/retrato, proporções com preview em escala, resoluções, custom, favoritos, indicadores de custo e nitidez) → contorno fantasma pra posicionar → monitor criado com a resolução exata (seção 10.3).
- "Recriar com outra resolução" (seção 10.4). Modo "captura por janela" como alternativa.
- N `StreamPipeline`s no host e N decoders/layers no headset.
- **Tela curva** (seção 10.2): curvatura 0–100%, presets, `CylinderLayerPolicy` com crossfade, e **medição** da troca de dono da layer cilíndrica (decide entre "curva em foco" e "curva principal fixa").
- Mover livre + resize **proporcional** (zoom sem mudar resolução) e curvatura já pelo `ScreenManipulationController` + Commands. Nesta fase a entrada provisória é o **controle/mãos** (Interaction SDK, rápido de ligar); o caminho principal, o **mouse**, entra na Fase 3, porque depende do hook e do `SpatialMap` do host.
- Undo/redo.
- **Frame rate manual por tela** (modos Auto/Fixed/Video/Eco no menu de contexto), já com o `BandwidthBudget` básico.
- **Áudio do PC** (WASAPI loopback → Opus → headset), sincronizado com o vídeo.

### Fase 3: Mouse e teclado em primeiro lugar (pilar C, seção 10)
- **Janela destacada:** `WindowDetacher` + monitor virtual dedicado, reanexar, fallback de "bastidores".
- **Cursor espacial:** `SpatialMap` + raycast angular (com a bateria de testes primeiro), Raw Input + hook, histerese, vão/parede, plano B com `SetDisplayConfig`.
- **Cursor local** desenhado no headset (`CursorStreamer`), com captura sem cursor.
- **Mouse-first (seção 10.10):** hit-test de moldura/espaço livre no `SpatialMap`, `ManipulationInterceptor`, moldura com as ações da tabela, tecla modificadora, cursor 3D livre, menu do app pelo mouse, `MouseManipulationAdapter`. O seletor de monitor e a edição da zona passam a funcionar pelo mouse.
- **Atalhos globais** (`HotkeyService` → Commands), reconfiguráveis.

### Fase 3b: Perfis de layout
- Salvar/aplicar perfis (Memento do estado), ancorados na mesa com **spatial anchors**, incluindo janelas destacadas (re-destacar ao aplicar).
- Retomar a última sessão automaticamente ao colocar o headset.
- Troca rápida (menu do app pelo mouse, `Ctrl+Alt+1…9` ou menu de pulso).

### Fase 4: Zona de passthrough (seção 9)
- **4a, sugestão:** MRUK Keyboard Tracking → `KeyboardBasedSuggestion` com as margens padrão. Passthrough Window com feather. Fluxo "Usar / Ajustar" na primeira vez. Fallbacks `TablePlane` e `Manual`.
- **4b, ajuste:** modo edição completo (bordas, cantos, altura, mover, girar, formato retângulo/arredondado, feather, opacidade, zonas extras, âncoras, reset). Ajustes guardados como deltas e salvos no perfil. Undo/redo.
- **4c, robustez:** congelar ao perder tracking, transição suave ao reencontrar, invariante "tela não invade zona", revelar ao olhar pra baixo.
- **4d (opcional):** detecção de mouse via Passthrough Camera API + modelo on-device, só se a margem do lado do mouse não bastar na prática.

### Fase 5: Polimento até a barra de qualidade (lançamento do MVP v1.0)
- FocusPolicy completa (foco = tela do cursor ou do olhar) + **MotionDetector** (sobe o fps sozinho quando detecta vídeo), bitrate adaptativo.
- Nitidez: resolução nativa por tela, sharpening leve opcional, supersampling na tela focada.
- Estabilidade: reconexão < 3 s sem perder o layout, sessão de 4 h sem crash, telemetria local de travadas.
- **Onboarding** (seção 1.1): primeira abertura com "Instale o host no seu PC" (link + QR code), status "Procurando seu PC…", e um instalador do host que já instala o driver de monitor virtual e configura o início automático com o Windows.
- **Só lança quando todos os critérios da seção 4.2 passarem.**

### Depois do MVP
Seguir o backlog da seção 4.3 (v1.1: legibilidade, olhar e pular, modo foco, modo chamada, conforto, HEVC/FEC · v1.2: vários PCs…).

---

## 15. Pesquisa verificada: passthrough, compositor layers e monitores virtuais

Levantamento feito na documentação oficial da Meta e da Microsoft e em projetos open-source (set/2026), antes de começar o projeto.

### 15.1 Tracked Keyboard antigo: NÃO usar
- O **Tracked Keyboard** (a lista fechada de teclados homologados que concorrentes usam) está **deprecated desde o v72**, "em favor do novo Generic Keyboard Tracker (disponível no Quest 3 e Quest 3S)". A documentação "não está mais sendo atualizada e está sujeita a remoção".
- Fonte: [Tracked Keyboard Overview (Deprecated)](https://developers.meta.com/horizon/documentation/unity/tk-overview/)

### 15.2 Substituto oficial: MRUK Keyboard Tracking ✅ (resolve o diferencial 4)
- **Rastreamento genérico:** a documentação fala em "a physical keyboard in the environment", sem lista de marcas.
- **Requisitos:** Quest 3/3S, Horizon OS v72+, Meta XR Core SDK v72+, MR Utility Kit v72+, Unity 6000.0.66f2 ou superior (o projeto usa Unity 6.3 LTS, que atende).
- **Setup:** permissão de **Spatial Data**, Scene Support = Required, Anchor Support = Enabled, componente MRUK com "Keyboard Tracking Enabled", Passthrough Layer (Building Blocks).
- **Dados expostos por teclado** (`MRUKTrackable` com `TrackableType.Keyboard`):
  - pose 6DoF (`Transform`)
  - retângulo 2D (`MRUKAnchor.PlaneRect`)
  - polígono 2D (`PlaneBoundary2D`)
  - caixa 3D (`VolumeBounds`)
- **Eventos:** `TrackableAdded` / `TrackableRemoved`.
- **Limitações:** **não é compatível com Fast Motion Mode** (manter a frequência do hand tracking em Low/Default). **Não rastreia mouse.** A doc não fala de latência, iluminação nem múltiplos teclados, então isso precisa ser medido na Fase 0.
- Fontes: [Track Keyboards in MR Utility Kit](https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-keyboard-tracking/) · [Unity-MRUtilityKitSample (GitHub)](https://github.com/oculus-samples/Unity-MRUtilityKitSample)

```csharp
public void OnTrackableAdded(MRUKTrackable trackable)
{
    if (trackable.TrackableType == OVRAnchor.TrackableType.Keyboard)
        deskZone.AttachTo(trackable); // posiciona a Passthrough Window no VolumeBounds
}
```

### 15.3 Como mostrar o mundo real só na mesa: Passthrough Windows ✅
- **Surface Projected Passthrough** (projetar passthrough numa geometria) está **deprecated desde o SDK v83**. A Meta recomenda **Passthrough Windows**. Não usar a primeira.
- **Passthrough Window:** um shader unlit retorna `float4(0,0,0,alpha)` com blend `BlendOp Add` / `Blend Zero SrcAlpha`, e o material fica com render queue alta (ex.: 5000). Onde o alpha vai a 0, o passthrough aparece. Com depth test ligado, a janela é ocluída por objetos virtuais na frente (pela profundidade da geometria, não do mundo real).
- **Aplicação no projeto:** a zona sugerida e ajustável da seção 9, renderizada com esse material + feather via SDF.
- Fontes: [Passthrough Windows](https://developers.meta.com/horizon/documentation/unity/unity-customize-passthrough-passthrough-windows/) · [Surface Projected Passthrough (deprecated)](https://developers.meta.com/horizon/documentation/unity/unity-customize-passthrough-surface-projected-passthrough/)

### 15.4 Compositor layers (telas): confirmado, com dois limites que mudam o design
- ✅ **External surface existe:** o `OVROverlay` tem "Is External Surface" pra "alimentar vídeo externo direto no compositor" via plugin Android (`isExternalSurface` / `externalSurfaceObject`). Suporta até conteúdo protegido (Widevine L1). O caminho `MediaCodec → Surface → layer` do projeto é viável.
- ✅ **Nitidez:** a Meta afirma que a versão em compositor layer "é visivelmente mais nítida, especialmente para texto", com opções de filtragem bicúbica, sharpening e supersampling. E as layers "mantêm fluidez mesmo quando o app perde frames".
- ⚠️ **Limite: até 15 layers por cena**, e **no máximo 1 layer cilíndrica e 1 cubemap**. Consequências:
  - no máximo ~12–13 telas em layer (passthrough e UI também contam)
  - **só 1 tela curva** pode ser layer; outras curvas precisam ir pro eye buffer (menos nítidas) ou ficar planas
- ⚠️ **Overlays são desenhadas na frente do eye buffer.** Uma tela-overlay pode cobrir a Passthrough Window da mesa. Usar `compositionDepth` e Depth Buffer Testing, e a regra de layout "telas não invadem o volume da mesa". Underlays são possíveis, mas "mais caras em banda" (punch-through de alpha).
- Fontes: [Use VR Compositor Layers](https://developers.meta.com/horizon/documentation/unity/unity-ovroverlay/) · [Compositor Layers sample](https://developers.meta.com/horizon/documentation/unity/unity-sample-compositor-layers/)

### 15.5 Passthrough Camera API: só para a Fase 4d (opcional)
- Quest 3/3S, Horizon OS v74+, permissão `horizonos.permission.HEADSET_CAMERA` (ou `CAMERA`). 1280×960 (1280×1280 a partir do v83), 60 Hz, **20–40 ms de latência**, ~45 MB de RAM e ~1–2% de GPU por câmera. Acesso via Camera2 / `WebCamTexture`. Imagem é um retângulo menor que o campo de visão.
- É pra **ML/visão computacional** (ex.: achar o mouse), **não** pra exibir o passthrough, que continua sendo o do sistema. Dados de câmera são "Device User Data" nas políticas da Meta, e isso exige cuidado de privacidade.
- Fontes: [Passthrough Camera API Overview](https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/) · [Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)

### 15.6 O que ainda precisa ser medido (entra na Fase 0)
- Confiabilidade do Keyboard Tracking com os seus teclados reais, e o tempo de detecção
- Comportamento de Depth Buffer Testing entre overlays e a Passthrough Window
- Quantas layers com external surface + decode simultâneo o Quest 3 sustenta a 90 Hz

### 15.7 Monitores virtuais (IddCx): trocar resolução em tempo real
Validação feita na documentação da Microsoft e em drivers open-source que já estão em produção (set/2026). **Não foi um teste em hardware**; o spike da Fase 2 confirma.

**O que a API oficial permite:**
- ✅ `IddCxMonitorUpdateModes` existe (Windows 10+) e serve para "atualizar a lista de modos de um monitor" já criado, **sem desplugar**. Com HDR, usa-se `IddCxMonitorUpdateModes2`.
- ✅ Criar e remover monitores em runtime (arrival/departure) é o uso normal do modelo IddCx.

**O que acontece na prática (relato de um desenvolvedor no Microsoft Q&A, sem resposta da Microsoft):**
- ❌ **Re-plugar o monitor com EDID novo:** cada re-plug cria entradas novas no registro. Depois de várias, o Windows passa a escolher uma resolução "aleatória". O monitor some por um instante e **as janelas abertas são deslocadas**. Veredito: não confiável.
- ⚠️ **`IddCxMonitorUpdateModes` + `SetDisplayConfig`:** o monitor **fica preto por um instante** a cada atualização, a resolução nova nem aparece nas configurações do Windows, e setar o `targetMode` dá erro (sem ele, **a imagem distorce**). Veredito: funciona parcialmente, com problemas em aberto.

**O que os drivers em produção fazem:**
| Driver | Como lida com resolução | Monitores | Assinado | Licença |
|---|---|---|---|---|
| **Parsec VDD** (via `parsec-vdd`) | Adiciona/remove monitores em runtime via IOCTL. Resoluções custom limitadas a **5 presets** no registro. Troca via Win32 Display API | Até **16 por adaptador** | ✅ | Wrapper MIT, mas **o driver é da Parsec** (redistribuir exige checar a licença) |
| **Virtual Display Driver** (VirtualDrivers) | Resoluções e refresh custom via `vdd_settings.xml`, sem reboot. HDR 10/12-bit no Win11 23H2+ | Vários (limite não documentado) | ✅ (SignPath Foundation) | MIT |
| **SudoVDA** (usado pelo Apollo) | O Apollo **cria o monitor com a resolução exata do cliente no início da sessão** e remove no fim | Não documentado | Certificado próprio (instalar como root confiável) | MIT/CC0 |

**Conclusões para o projeto:**
1. **Criar monitores com resolução exata no momento da criação** é comprovado em produção (Apollo/SudoVDA). ✅
2. **Trocar a resolução de um monitor vivo, continuamente, durante um arraste** é arriscado (tela preta, distorção, janelas deslocadas). ❌ Não basear a UX nisso.
3. **Decisão de design:**
   - **A resolução é escolhida na criação** (seletor da seção 10.3) e o monitor é criado com o modo exato (padrão Apollo, comprovado).
   - **Redimensionar no headset é zoom proporcional**, e a resolução nunca muda (regra 10.1). Zero troca de modo no uso normal.
   - **Janela destacada → monitor-palco fixo com recorte** (seção 10.5). O tamanho real da janela muda pelo próprio Windows (arrastando a borda com o mouse).
   - **Mudar a resolução de um monitor existente = recriar** (ação explícita, seção 10.4), nunca troca de modo ao vivo.
4. **Opção para acelerar o MVP e resolver a assinatura:** em vez de escrever o driver do zero, **controlar um driver open-source já assinado** (Virtual Display Driver, MIT, assinado via SignPath). Decisão registrada na seção 16.
5. **Referência de mercado:** o projeto open-source **Virtual Sunshine / Virtual Moonlight** (fork do Apollo) está fazendo multi-monitor com monitores virtuais pro Quest 3. Vale estudar como concorrente e referência técnica.

Fontes: [IddCxMonitorUpdateModes (Microsoft Learn)](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/nf-iddcx-iddcxmonitorupdatemodes) · [IDD dynamic resolution at runtime (Microsoft Q&A)](https://learn.microsoft.com/en-us/answers/questions/5924412/indirect-display-driver-(idd)-dynamic-resolution-s) · [Indirect Display Driver Model Overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/indirect-display-driver-model-overview) · [parsec-vdd](https://github.com/nomi-san/parsec-vdd) · [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) · [SudoVDA](https://github.com/Scotchy49/SudoVDA) · [Apollo](https://github.com/ClassicOldSong/Apollo/blob/master/README.md) · [virtual-sunshine](https://github.com/r0mn-creator/virtual-sunshine)

---

## 16. Decisões

### 16.1 Decididas (escopo fechado)

| # | Decisão | Validação pendente |
|---|---|---|
| D1 | **Stack híbrida:** Unity 6.3 LTS + Meta XR SDK (mais recente) pra interação/UI; vídeo em código nativo (`MediaCodec` → compositor layer, zero-copy); host em .NET 10 LTS | Fase 0 |
| D2 | **Windows-only no MVP** (Windows 10/11). Mac em "Futuro". `Host.Domain` fica portável | — |
| D3 | **Monitores virtuais via Virtual Display Driver** open-source (MIT, assinado). Driver próprio só com ADR justificando | Spike da Fase 2 |
| D4 | **Resolução e orientação (paisagem/retrato) definidas na criação; resize = zoom proporcional**; mudar resolução ou orientação = recriar o monitor. Retrato é criado nativo (ex.: 1440×2560), sem rotação de tela do Windows | Spike da Fase 2 |
| D5 | **H.264 no MVP**; HEVC na v1.1. Resoluções acima do limite do encoder ficam marcadas "requer HEVC" | Spike da Fase 2 (limite real) |
| D6 | **Rede:** Wi-Fi 6/6E + PC no cabo. USB tether em "Futuro" | Fase 0 (latência) |
| D7 | **Zona de passthrough:** MRUK Keyboard Tracking + Passthrough Windows; formatos retângulo/arredondado no MVP | Fase 0 (tracking real) |
| D8 | **Cursor espacial** no host (Raw Input + hook + mapa angular), com o Windows como plano B | Testes de domínio da Fase 3 |
| D9 | **Escopo do MVP = 3 pilares** (seção 4). Todo o resto vai pro backlog | — |
| D10 | **Mouse-first:** mouse (moldura + tecla modificadora + cursor 3D livre) é a forma principal de manipular as telas e operar a UI. Mãos são alternativa, gerando os mesmos Commands | Testes de integração e de uso da Fase 3 |
| D11 | **Telas curvas:** curvatura horizontal ajustável 0–100% por tela (atalho, mouse, menu, mãos); a única layer cilíndrica vai pra tela curva em foco; curvatura nunca muda resolução | Medição na Fase 2 |

Se uma validação pendente falhar, a decisão é revista **com ADR**, e o escopo só muda seguindo a regra do topo do documento.

### 16.2 Pendentes (não bloqueiam o início do desenvolvimento)
- **Modelo de negócio:** compra única na Meta Store vs. assinatura (ex.: perfis na nuvem e vários PCs como plano pago). Decidir antes da Fase 5.
- **Nome do produto:** o projeto se chama **Screen Portal** (nome do repositório e do projeto Unity). Antes da Fase 5, confirmar disponibilidade da marca e do nome na Meta Store.
- **Distribuição:** App Lab/Meta Store (headset) + instalador do host para Windows (site próprio, Microsoft Store ou winget). Decidir antes da Fase 5.

---

## 17. Primeira tarefa para o Claude Code

> Execute a **Fase 0**. Crie o monorepo com a estrutura da seção 7.2 (só as pastas necessárias para o spike) e o `CLAUDE.md` com as regras da seção 13. Implemente:
> 0. **Antes de tudo, no `headset-unity/`:** instale os pacotes da seção 6.4 (**grupo VR/Meta e grupo IA/AI Assistant**) via `Packages/manifest.json`, confirme que o projeto abre sem erros de pacote, rode o Project Setup Tool (Fix All) e commite. Peça ao Cayo as ações manuais listadas no fim da seção 6.4.
> 1. `host/`: app .NET 10 console que captura o monitor primário via Windows Graphics Capture, codifica em H.264 por hardware em modo low-latency (sem B-frames) e envia por UDP para um IP/porta configuráveis, embutindo um timestamp por frame.
> 2. `headset-native/`: Android library (Kotlin + NDK) que recebe os pacotes UDP, remonta os frames e decodifica com `MediaCodec` numa `Surface` recebida da Unity.
> 3. `headset-unity/`: cena mínima com Meta XR Core SDK e um `OVROverlay` (quad, external surface) que passa sua Surface para o `.aar` via JNI.
> 4. Harness de latência e teste de 1/2/4/6 streams simultâneos. Registre os resultados em `docs/measurements/fase0.md` e crie o ADR `0001-hybrid-unity-native-video-path.md`.
> 5. Cena de teste separada com **MRUK Keyboard Tracking** + **Passthrough Window** na zona sugerida (seções 9.2 e 15): só a sugestão com margens padrão e um arraste simples de borda, pra validar a ideia. Coloque um `OVROverlay` logo acima da mesa pra checar oclusão entre overlay e janela de passthrough. Meça também quanto tempo leva pra detectar o teclado e se o tracking se perde. Registre em `docs/measurements/fase0-passthrough.md`.
>
> Além dessas cenas de teste, não construa UI de layout, seletor de monitor, perfis, cursor espacial, manipulação com o mouse nem a zona de passthrough completa nesta fase. A Fase 0 só prova o caminho do vídeo e o tracking do teclado.
