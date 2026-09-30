# Status do projeto

**Fase atual:** **Fase 2 — multi-monitor livre**: 2A concluída (29/09/2026) → próximas: 2B (Simulator) e 2C.
Brief: [brief.md](brief.md) v1.3.2 (ADRs 0002–0006).

## Fase 0 — resultado
| # | Item | Resultado |
|---|---|---|
| 0 | Pacotes Unity + config Meta (Project Setup Tool) | ✅ |
| 1 | Host .NET 10: WGC → H.264 HW (AMF) low-latency → UDP | ✅ captura→bitstream ~8 ms |
| 2 | `.aar` nativo: UDP → remontagem → MediaCodec → Surface | ✅ |
| 3 | Cena Unity com `OVROverlay` external surface | ✅ |
| 4 | Latência e multi-tela | ✅ 1 tela: **~33 ms**, 60 fps, 0 perdas · ⚠️ multi-tela instável (remedir na Fase 2) |
| 4b | Texto | ✅ **12 pt legível** no tamanho padrão (140 cm a 1,3 m) |
| 5 | Keyboard Tracking + Passthrough Window | ❌ teclado não reconhecido → **ADR 0003: zona desenhada pelo usuário** |

ADRs: [0000](adr/0000-unity-project-at-repo-root.md) Unity na raiz · [0001](adr/0001-hybrid-unity-native-video-path.md) caminho de vídeo (aceito) ·
[0002](adr/0002-app-refresh-rate-72hz.md) 72 Hz · [0003](adr/0003-passthrough-zone-user-defined.md) zona manual ·
[0004](adr/0004-immutable-workspace-and-shared-nunit-tests.md) workspace imutável + testes compartilhados ·
[0005](adr/0005-layout-surface-cylinder-sphere.md) superfície Cilindro/Esfera ·
[0006](adr/0006-control-protocol-pairing-mdns.md) protocolo de controle · [0007](adr/0007-virtual-display-driver.md) SudoVDA ·
[0008](adr/0008-decode-capacity.md) capacidade de decode.

## Ferramentas
- `dotnet test tests-dotnet/UnityPureCode.Tests` — testes de Domain/Application sem o Editor (segundos).
- `tools/unity-cmd.ps1 -Command refresh|resolve-packages|test-editmode|build-main-scene|boot-main|build-apk|…` — automação do Editor sem MCP.
- `tools/phase0-streams-test.ps1 -Streams N [-Fps] [-AppExtras]` — teste de vídeo de ponta a ponta no Quest.
- `tools/host-loopback-test.ps1` — host sem headset. `tools/build-native.ps1` — recompila o `.aar`.

## Pendências levadas para frente
- **2C:** espera de ~2 s antes de capturar um monitor recém-criado; resolver o HMONITOR na hora (ADR 0007). Medir 4K.
- **2F:** jitter do Wi-Fi (rajadas de ~1 s) e bitrate proporcional ao fps (ADR 0008).
- **Fase 4a:** validar o shader de Passthrough Window com a zona manual e a oclusão overlay × janela.
- Remover MRUK / `sceneSupport = Required` quando a cena de spike deixar de ser usada (ADR 0003).

## Fase 1 (branch `phase1/skeleton`)
- **1A (esqueleto + domínio):** ✅ asmdefs Domain/Application/Presentation/Composition/Editor, Workspace imutável,
  9 Commands com invariantes testados, WorkspaceStore (undo/redo), CommandBus + CommandJournal,
  ScreenViewSynchronizer, VContainer 1.19.0 + cena `Main` com views OVROverlay. **75 testes** passando no
  `dotnet test` e no Test Runner da Unity. Plano: [plans/2026-09-28-fase1a-esqueleto-dominio.md](plans/2026-09-28-fase1a-esqueleto-dominio.md) · [ADR 0004](adr/0004-immutable-workspace-and-shared-nunit-tests.md).
- **1B (conexão):** ✅ protocolo de controle v1 (FlatBuffers, TCP 9200), pareamento com código, máquina de estados
  (descoberta → pareamento → stream ⇄ instável → reconexão), mDNS nos dois lados, status visível no headset, vídeo do
  PC na tela da cena `Main`. **112 testes** (Unity/harness) + **28** (host). No Quest: descoberta ~1 s, reabertura sem
  código, reconexão < 1 s após o host voltar. [spec](specs/2026-09-28-fase1b-conexao-design.md) ·
  [plano](plans/2026-09-28-fase1b-conexao.md) · [ADR 0006](adr/0006-control-protocol-pairing-mdns.md) ·
  [verificação](measurements/fase1b-conexao.md).

## Fase 2 — subprojetos (aprovado em 28/09/2026)
Cada um com spec → plano → implementação → verificação. Ordem: **2A + 2B** → 2C → 2D/2E → 2F/2G.

| # | Subprojeto | Onde se testa | Depende de | Estado |
|---|---|---|---|---|
| 2A | Spikes: driver de monitor virtual + capacidade do decoder | PC (admin) + Quest | — | ✅ SudoVDA ([ADR 0007](adr/0007-virtual-display-driver.md)); 4×60 fps sem perdas, foco a ~16 ms ([ADR 0008](adr/0008-decode-capacity.md)) |
| 2B | Superfície de layout Cilindro/Esfera + manipulação (controle/mãos) + undo | Meta XR Simulator | — | 🔧 branch `phase2b/manipulation`: Tasks 1–12 prontas; Task 13 no Simulator ✅ (17/17, bug do texto num olho só corrigido, palma calibrada); **retomar na Task 13 Step 4** (conferência visual do Cayo) → Task 14 (Quest) — [plano](plans/2026-09-29-fase2b-manipulacao.md) · [verificação](measurements/fase2b-simulator.md) |
| 2C | Várias telas: monitores virtuais sob comando do headset, N streams | PC + Quest | 2A | — |
| 2D | Seletor "Novo monitor" + contorno fantasma + recriar | Simulator + Quest | 2B, 2C | — |
| 2E | Telas curvas (CylinderLayerPolicy, crossfade, medição) | Simulator + Quest | 2B, 2C | — |
| 2F | Frame rate por tela + BandwidthBudget | testes + Quest | 2A, 2C | — |
| 2G | Áudio do PC (WASAPI → Opus) | PC + Quest | — | — |

## Decidido e documentado, para fases futuras
- **Superfície de layout Cilindro/Esfera** ([ADR 0005](adr/0005-layout-surface-cylinder-sphere.md), brief §10.12,
  [spec](specs/2026-09-28-superficie-layout-design.md)): domínio no início da manipulação da **Fase 2**; mouse, atalhos
  e `SpatialMap` na **Fase 3**. Não altera a ordem do roadmap.
- **Princípio "Liberdade"** registrado no brief e no CLAUDE.md.
