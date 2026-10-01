# Fase 2F — Frame rate por tela + orçamento de banda — Plano

> Execução inline nesta sessão (superpowers:executing-plans), TDD por tarefa, um commit por tarefa.

**Goal:** cada tela com vídeo recebe fps e bitrate conforme o modo dela, o olhar (só em Auto) e o orçamento medido no
ADR 0008; o host aplica ao vivo.

**Architecture:** regras puras na Application (`FrameRates`, `GazeFocus`, `BandwidthBudget`) compostas por um
reconciliador `RateSync` (mesmo padrão do `HostSync`: estado desejado → mensagens só na mudança). Protocolo ganha
`SetStreamRate`. No host, `ScreenHost.SetRate` → `StreamPipeline.SetRate` (limitador da captura + bitrate do encoder
ao vivo).

**Tech Stack:** C# 9 (Unity 6.3) · .NET 10 host · FlatBuffers (`tools/gen-protocol.ps1`) · NUnit (harness `dotnet test`)
· xUnit (host).

## Global Constraints
- Modos: Auto (60 em foco / 15 fora) · Vídeo 60 fps 30 000 kbps · Fixo 30 · Fixo 15 · Eco 5 fps.
- Manual sempre manda; o olhar só decide em Auto.
- Bitrate = round(20 000 × fps / 60) kbps, mínimo 2 000; Vídeo = 30 000.
- Foco pelo olhar: ângulo ≤ 25° do centro da visão; vira foco após 0,3 s; perde após 1 s.
- Orçamento: ≤ 4 telas a 60 fps; ≤ 6 telas no total (7ª em diante a 5 fps); corte Eco → Auto fora do foco → Fixo →
  Vídeo, a mais recente primeiro; degraus 60 → 30 → 15 → 5; tela em foco intocável; aviso quando o estouro vem de
  modos manuais: "Essa configuração pode causar travadas".
- Host limita fps 1–60 e kbps 1 000–50 000; sem recriar pipeline nem IDR.
- Sem alocação por quadro no caminho do Tick (listas/arrays reaproveitados).

**Desvio da spec:** a linha de status mostra o fps **concedido** ("Auto · 60 fps"), não o medido — o `IScreenVideo`
não expõe fps por tela hoje (o medido continua no `SPSTAT` do logcat para o relatório).

---

### Task 1: `FrameRates` (fps pedido e bitrate por modo)
- Create `Application/Screens/FrameRates.cs`: `const int FocusFps = 60, UnfocusedFps = 15, EcoFps = 5, VideoKbps = 30000, MinKbps = 2000, KbpsAt60 = 20000`;
  `static int RequestedFps(FrameRateMode mode, bool focused)`; `static int Kbps(FrameRateMode mode, int fps)`; `static string Name(FrameRateMode)`.
- Tests (`Tests/EditMode/Screens/FrameRatesTests.cs`): Auto foco 60 / fora 15; Vídeo 60 com ou sem foco; Fixo 30/15 ignora o foco;
  Eco 5 olhando; kbps 60→20 000, 15→5 000, 5→2 000 (mínimo), Vídeo 30 000.

### Task 2: `GazeFocus`
- Create `Application/Screens/GazeFocus.cs`: `ScreenId Tick(double now, Pose head, Workspace ws)`; `const float MaxAngleDeg = 25, AcquireSeconds = 0.3f, ReleaseSeconds = 1f`.
  Candidata = tela com vídeo de menor ângulo (centro = `ws.Surface.ToPose(p)` ou `p.LoosePose`). Troca de foco só quando a mesma
  candidata persiste 0,3 s; sem candidata por 1 s → `ScreenId.None`.
- Tests: olhando direto vira foco só após 0,3 s; desviar 0,5 s mantém; 1,1 s solta; tela a 40° nunca; tela de teste nunca;
  entre duas, a de menor ângulo.

### Task 3: `BandwidthBudget`
- Create `Application/Screens/BandwidthBudget.cs`: `struct RateRequest { ScreenId Screen; FrameRateMode Mode; int Fps; int Order; }`;
  `bool Allocate(IReadOnlyList<RateRequest> requests, ScreenId focus, int[] grantedFps)` (true = aviso);
  `const int MaxAt60 = 4, MaxScreens = 6`.
- Tests: 4×60 cabe; 5×Vídeo → a mais recente vai a 30 e aviso; foco nunca rebaixado (foco é a 5ª Vídeo → outra cai);
  Auto fora do foco já a 15 não conta no limite de 60; 7 telas → a mais recente de menor prioridade vai a 5; Eco corta
  antes de Auto; sem estouro → sem aviso.

### Task 4: Protocolo `SetStreamRate`
- `protocol/control.fbs`: `table SetStreamRate { screen_key:uint; fps:ubyte; kbps:uint; }` no fim do union; `tools/gen-protocol.ps1`.
- `Application/Connection/ControlMessages.cs`: `SetStreamRateMessage(uint screenKey, byte fps, uint kbps)`.
- `ControlMessageCodec`: encode/decode (+ verificação do body). `protocol/control-v2.md`: nota.
- Tests: ida e volta no codec.

### Task 5: Host aplica a taxa
- `StreamPipeline.SetRate(int fps, int kbps)`: `_minFrameIntervalUs` volátil; `MfH264Encoder.SetBitrate(int kbps)` via
  `ICodecAPI` (`AVEncCommonMeanBitRate`) no thread do encoder (flag + valor, aplicado antes do próximo `ProcessInput`).
- `Host.Domain`: `StreamRateLimits.Clamp(fps, kbps)` (1–60, 1 000–50 000) com teste xUnit.
- `IScreenHost.SetRate(deviceId, generation, screenKey, fps, kbps)`; `HeadsetSession` trata `SetStreamRateMessage` (fila de
  trabalho, só sessão atual); `ScreenHost.SetRate` aplica no pipeline vivo e loga só quando muda.
- Tests: Clamp; HeadsetSession encaminha (fake IScreenHost, padrão dos testes existentes).

### Task 6: `RateSync` (reconciliador no headset)
- Create `Application/Screens/RateSync.cs`: ctor(WorkspaceStore, IScreenLinks, ConnectionStateMachine, IHeadPoseSource, IClock);
  `Tick()`: foco (GazeFocus) → pedidos das telas Live (ordem = id) → orçamento → envia `SetStreamRateMessage` quando (fps, kbps)
  muda por tela; esquece o enviado quando a tela sai de Live (novo pipeline começa no padrão do host).
  Expõe `ScreenId Focus`, `bool Warning`, `bool TryGetGranted(ScreenId, out int fps)`.
- Composition: registrar e tickar depois do `HostSync`.
- Tests (padrão dos testes do HostSync): manda só na mudança; olhar troca 60/15 entre duas telas; sair de Live e voltar reenvia;
  modo manual vence; aviso com 5×Vídeo.

### Task 7: Menu "Taxa de quadros ▸" + status
- `WristMenu`: item `FrameRate` na principal (apagado sem foco do menu ou em tela de teste); página `FrameRate` com Auto, Vídeo,
  Fixo 30, Fixo 15, Eco (" · atual"), envia `SetFrameRateMode`; status "Auto · 60 fps" (do `RateSync`) e o aviso do orçamento.
  `MaxItems` continua 13: a principal sem teste tem 12 → checar com teste e, se passar, tirar a tela de teste para uma página "Dev".
- Tests: itens, envio do Command, undo, status.

### Task 8: Unity + Quest
- Compilar (unity-cmd refresh / test-editmode), APK, host Debug.
- Quest: olhar troca fluidez; modos; Vídeo fora do foco a 60; Eco olhando a 5; aviso 5×Vídeo; medição de banda/perdas
  (log do host `TakeStats` + `SPSTAT`) contra 60 fixos.
- `docs/measurements/fase2f-fps-orcamento.md`, STATUS, merge.
