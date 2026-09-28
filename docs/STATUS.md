# Status do projeto

**Fase atual:** 0 — spike de viabilidade (branch `phase0/spike`). Brief: [brief.md](brief.md) §14/§17.

## Fase 0 — checklist (brief §17)
| # | Item | Estado |
|---|---|---|
| 0 | Pacotes Unity (§6.4): Meta XR Core/Interaction/MRUK **207.0.0**, AI Assistant 2.20.0-pre.1, Pipeline 0.8.0-exp.1 | ✅ já instalados (registry da Unity; sem scoped registry) · Project Setup Tool Fix All executado |
| 0 | Config Meta: anchors, Scene Support = Required, passthrough, mãos + controles, IL2CPP ARM64 | ✅ via `Phase0SceneBuilder.ConfigureProject` |
| 1 | Host .NET 10: WGC → H.264 HW low-latency → UDP com timestamp | ✅ implementado e medido no PC (~8 ms captura→bitstream) |
| 2 | `headset-native` .aar: UDP → remontagem → MediaCodec na Surface | ✅ compila (arm64, 16 KB pages); ⏳ testar no Quest |
| 3 | Cena Unity mínima com `OVROverlay` external surface → JNI | ✅ `Phase0_Video.unity`; ⏳ testar no Quest |
| 4 | Harness de latência + 1/2/4/6 streams, `fase0.md`, ADR 0001 | ✅ harness e ADR (Proposto) · ⏳ medições no headset |
| 5 | Cena MRUK Keyboard + Passthrough Window + overlay de oclusão, `fase0-passthrough.md` | ✅ `Phase0_Passthrough.unity` · ⏳ medições no headset |

## Pendências que dependem do Cayo
- Rodar o runbook no Quest 3 ([phase0-runbook.md](phase0-runbook.md)) e preencher as medições.
- Unity MCP: aceitar a conexão em Project Settings → AI → Unity MCP se pedir novamente.

## Próximo (Fase 1, após as medições)
Esqueleto limpo: asmdefs Domain/Application/Presentation/Infrastructure/Composition, VContainer,
CommandBus + WorkspaceStore com testes, schemas FlatBuffers do canal de controle, handshake, state machine de conexão, mDNS.
