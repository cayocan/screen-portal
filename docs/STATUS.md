# Status do projeto

**Fase atual:** Fase 0 concluída (28/09/2026) → próxima: **Fase 1 — esqueleto limpo**.
Brief: [brief.md](brief.md) v1.3.1 (ADRs 0002 e 0003).

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
[0002](adr/0002-app-refresh-rate-72hz.md) 72 Hz · [0003](adr/0003-passthrough-zone-user-defined.md) zona manual.

## Ferramentas
- `tools/unity-cmd.ps1 -Command build-apk|build-scenes|boot-video|boot-passthrough|refresh` — automação do Editor sem MCP.
- `tools/phase0-streams-test.ps1 -Streams N [-Fps] [-AppExtras]` — teste de vídeo de ponta a ponta no Quest.
- `tools/host-loopback-test.ps1` — host sem headset. `tools/build-native.ps1` — recompila o `.aar`.

## Pendências levadas para frente
- **Fase 2 (spike do decoder):** capacidade estável de telas (conteúdo fixo, bateria cheia, política de
  descarte sem cascata, IDR sob demanda, 3 repetições).
- **Fase 4a:** validar o shader de Passthrough Window com a zona manual e a oclusão overlay × janela.
- Remover MRUK / `sceneSupport = Required` quando a cena de spike deixar de ser usada (ADR 0003).

## Próximo: Fase 1
Esqueleto limpo: asmdefs Domain/Application/Presentation/Infrastructure/Composition, VContainer,
CommandBus + WorkspaceStore com testes, schemas FlatBuffers do canal de controle, handshake versionado,
state machine de conexão, descoberta mDNS.
