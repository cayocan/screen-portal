# Fase 0 — zona de passthrough (MRUK Keyboard Tracking + Passthrough Window)

## Resultado (28/09/2026) — Keyboard Tracking reprovado → ADR 0003

| Item | Resultado |
|---|---|
| Ambiente | Quest 3, Horizon OS v83, MRUK/Core SDK 207, permissões Scene/Anchor concedidas via `adb pm grant` |
| Teclado | TKL (sem teclado numérico) do Cayo |
| App pediu o tracking? | ✅ `XR_META_dynamic_object_tracker` e `XR_META_dynamic_object_keyboard` carregados; MRUK consultando o tracker 1×/s |
| Sistema reconheceu o teclado? | ❌ Nunca. Log contínuo do serviço: `DynamicObjectClient: Object in query has empty predictions or tracking flags` |
| Tempo até detectar | — (não detectou) |
| Oclusão overlay × Passthrough Window | ⏳ não testada (a zona só aparecia após a detecção). Fica para a Fase 4a com a zona manual |

**Decisão:** abandonar a detecção automática; a zona é sempre desenhada pelo usuário e ancorada na
mesa ([ADR 0003](../adr/0003-passthrough-zone-user-defined.md)). Brief §9 revisado.

## Pendente para a Fase 4a
- Validar o shader `PassthroughWindowFeather` (feather via SDF) com a zona manual.
- Oclusão entre `OVROverlay` e a janela de passthrough (`noDepthBufferTesting` on/off).
- Cronometrar a criação da zona só com o mouse (< 60 s) e só com o controle (< 30 s).
