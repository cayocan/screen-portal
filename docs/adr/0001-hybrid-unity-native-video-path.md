# ADR 0001 — Caminho de vídeo híbrido: host .NET (WGC + MFT de hardware) → UDP → MediaCodec nativo → OVROverlay external surface

- **Status:** **Aceito** (28/09/2026) — validado no Quest 3: 1 tela 2560×1080@60 com ~33 ms
  captura→decodificado, 0 perdas, app estável a 72 Hz, texto 12 pt legível (fase0.md, fase0-texto.md).
  Capacidade multi-tela ainda não estável (2–4 telas@60 passaram numa rodada e falharam em outra):
  **remedido no spike do decoder da Fase 2** antes de fechar o `BandwidthBudget`.
- **Data:** 28/09/2026
- **Decisões do brief relacionadas:** D1, D5, D6; §6.3, §8.7, §15.4.

## Contexto
O pilar A exige texto nítido e 90 Hz estáveis com várias telas. Renderizar o vídeo como textura na
cena Unity reamostra a imagem duas vezes e põe o frame no caminho do GC. A Fase 0 precisa provar o
caminho de menor latência e zero cópia antes de qualquer UI.

## Decisão
**Host (PC, .NET 10, `host/`)**
- Captura: Windows Graphics Capture (monitor primário) num `ID3D11Device` único, multithread-protected.
- Conversão: BGRA → NV12 (e escala) na GPU com `ID3D11VideoProcessor`, anel de 4 texturas.
- Encode: MFT H.264 **de hardware do fabricante** (AMF/NVENC/QSV) via Media Foundation, modo
  assíncrono, entrada DXGI (sem ida à CPU). `MF_LOW_LATENCY`, CBR, **0 B-frames**, perfil Main,
  IDR a cada 1 s (Fase 0; a partir da Fase 1, IDR sob demanda pelo canal de controle).
  SPS/PPS reinjetados em todo IDR se o MFT não os emitir.
- Política de fila: "último frame pendente" — frame novo substitui o que ainda não entrou no encoder.
- Transporte: UDP, header fixo de 24 bytes (`protocol/video-packet-v1.md`), um fluxo por tela,
  DSCP EF. Clock sync UDP/9100 para o harness de latência.
- Interop: Vortice (D3D11/MF) + chamadas diretas de vtable onde o CsWinRT/Vortice não cobre
  (`IGraphicsCaptureItemInterop`, `IDirect3DDxgiInterfaceAccess`, `ICodecAPI`).

**Headset (`headset-native/` → `Assets/Plugins/Android/screenportal-native.aar`)**
- C++17/NDK: `recvfrom` → remontagem (só o frame mais recente) → `AMediaCodec` configurado com a
  `ANativeWindow` da Surface da `OVROverlay` (`isExternalSurface`), `low-latency=1` + chave Qualcomm,
  `releaseOutputBuffer(render=true)`. O frame nunca passa pela JVM nem pelo C#.
- Kotlin: só a fachada JNI `NativeVideo` (sem kotlin-stdlib no runtime; asserções desligadas).
- O host é descoberto pelo endereço de origem do 1º pacote (sem configuração de IP no headset).

**Unity**
- `Phase0VideoSpike`: N `OVROverlay` quad com external surface; ao surgir `externalSurfaceObject`,
  chama `NativeVideo.start(...)` via `AndroidJNI` com argumentos pré-alocados.

## Consequências
- ✅ Medido no PC de dev (RX 5500 XT, AMF), 2560×1080@60, 20 Mbps: **captura→bitstream ≈ 8 ms
  (média), ~25–30 ms (máx)**, 0 perdas em loopback, stream H.264 Main sem B-frames validado com ffprobe.
- ⚠️ O timestamp de captura é a **chegada** do frame no host (o `SystemRelativeTime` do WGC é o
  instante de apresentação alvo do DWM, ~6 ms no futuro). A medição subestima o glass-to-glass em
  ~1 vsync do PC + o scan-out do headset.
- ⚠️ WGC aloca wrappers WinRT por frame no host (GC do .NET, não da Unity). Aceitável na Fase 0;
  reavaliar com `dotnet-counters` numa sessão longa (critério de 4 h).
- ⚠️ No Windows 10 a borda amarela do WGC não pode ser desligada (`IsBorderRequired` é Win11+).
- ⚠️ `--streams N` replica o **mesmo** encode em N portas: mede decoders/layers do Quest, não o
  custo de N encoders no PC (esse entra na Fase 2 com N monitores virtuais).
- Pendente (fase0.md): latência glass-to-glass real, limite de decoders simultâneos a 90 Hz,
  comportamento do Wi-Fi 6 com 1/2/4/6 streams. Se o número de decoders for baixo → atlas/FocusPolicy (brief §12).
