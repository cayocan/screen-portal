# Status do projeto

**Fase atual:** **Fase 2 — multi-monitor livre**: **Fase 2 concluída** (2A–2E 29–30/09, 2F e 2G 01/10/2026) → próxima: **Fase 3** (mouse e teclado em primeiro lugar).
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
- **Fase 5:** driver de áudio próprio assinado (hoje dependemos dos drivers de streaming do Steam, ADR 0015); medição da dessincronia áudio/vídeo por claquete (meta < 45 ms).
- **Fase 3:** arranjo do Windows segue a disposição das telas no Quest (pedido do Cayo, 30/09; era o plano B da Fase 3) — resolve também o arranjo perdido ao reiniciar o PC.
- **Fase 3 (junto do arranjo):** a escala de cada monitor virtual (Windows: "Alterar o tamanho do texto, dos aplicativos e de outros itens") é guardada por tela e reaplicada quando o monitor é recriado (reabrir o app, reiniciar o PC, recriar). Pedido do Cayo, 01/10. Obs.: a escala por monitor só tem API não documentada (`DisplayConfigGetDeviceInfo`/`SetDeviceInfo` com o tipo de DPI) — medir antes.
- **Host:** se o SudoVDA cair no meio da sessão, o host reinicia o dispositivo ao criar o próximo monitor, mas os monitores que já existiam não se recriam sozinhos.
- **Host:** recuperar de `DXGI_ERROR_DEVICE_REMOVED` (driver/TDR) sem reiniciar: recriar o `GpuDevice` e os pipelines.
- **Telas sobrepostas** com layers Underlay: ordenar a `compositionDepth` pela distância se incomodar (ADR 0010).
- **Fase 5:** bitrate adaptativo à rede e medição de perdas do Wi-Fi por minuto (o bitrate proporcional ao fps veio na 2F).
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
| 2B | Superfície de layout Cilindro/Esfera + manipulação (controle/mãos) + undo | Meta XR Simulator | — | ✅ Simulator 17/17 (bug do texto num olho só corrigido, palma calibrada) + Quest: todos os testes passaram — [spec](specs/2026-09-29-fase2b-manipulacao-design.md) · [plano](plans/2026-09-29-fase2b-manipulacao.md) · [verificação](measurements/fase2b-simulator.md) |
| 2C | Várias telas: monitores virtuais sob comando do headset, N streams | PC + Quest | 2A | ✅ Simulator 1–7 + Quest: 5 telas a 60 fps, 4K, remover/undo, reconexão; UI por cima das telas (Underlay), tela nova à frente da visão, reorganizar monitores no Windows — [spec](specs/2026-09-30-fase2c-varias-telas-design.md) · [plano](plans/2026-09-30-fase2c-varias-telas.md) · [verificação](measurements/fase2c-simulator.md) · [ADR 0009](adr/0009-screen-leases-declarative-sync.md) · [ADR 0010](adr/0010-screens-as-underlay-layers.md) |
| 2D | Recriar com outra resolução (contorno fantasma e resolução custom saíram, ADR 0012) | Quest | 2B, 2C | ✅ Quest: recriar e desfazer levam as janelas; o monitor novo ocupa o lugar do antigo no Windows — [spec](specs/2026-09-30-fase2d-recriar-design.md) · [plano](plans/2026-09-30-fase2d-recriar.md) · [verificação](measurements/fase2d-recriar.md) |
| 2E | Telas curvas: toda tela curva é layer cilíndrica (ADR 0011) | Quest | 2B, 2C | ✅ spike (4 cilindros a 60 fps) + Quest: presets, alça, troca sem piscar, UI por cima — [spec](specs/2026-09-30-fase2e-telas-curvas-design.md) · [plano](plans/2026-09-30-fase2e-telas-curvas.md) · [verificação](measurements/fase2e-telas-curvas.md) · também: texto em TMP (ADR 0013), mãos/lasers/tchau, última sessão (ADR 0014) |
| 2F | Frame rate por tela (Auto pelo olhar, Vídeo, Fixo 30/15, Eco) + BandwidthBudget | testes + Quest | 2A, 2C | ✅ Quest: olhar troca a fluidez, modos, aviso; banda de ~100 → ~70 Mbps com 5 telas — [spec](specs/2026-09-30-fase2f-fps-orcamento-design.md) · [plano](plans/2026-09-30-fase2f-fps-orcamento.md) · [verificação](measurements/fase2f-fps-orcamento.md) |
| 2G | Áudio do PC no Quest + microfone do Quest no PC (WASAPI → Opus) | PC + Quest | — | ✅ som só no Quest, microfone no Windows, mudo; saída padrão devolvida — [spec](specs/2026-10-01-fase2g-audio-design.md) · [plano](plans/2026-10-01-fase2g-audio.md) · [verificação](measurements/fase2g-audio.md) · [ADR 0015](adr/0015-audio-steam-streaming-drivers.md) |

## Decidido e documentado, para fases futuras
- **Superfície de layout Cilindro/Esfera** ([ADR 0005](adr/0005-layout-surface-cylinder-sphere.md), brief §10.12,
  [spec](specs/2026-09-28-superficie-layout-design.md)): domínio no início da manipulação da **Fase 2**; mouse, atalhos
  e `SpatialMap` na **Fase 3**. Não altera a ordem do roadmap.
- **Princípio "Liberdade"** registrado no brief e no CLAUDE.md.
