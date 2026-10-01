# Fase 2G — Áudio — Plano

> Execução inline (superpowers:executing-plans), TDD onde é puro, um commit por tarefa.

**Goal:** som do PC só no Quest e microfone do Quest no PC, com Opus nos dois sentidos.
**Spec:** docs/specs/2026-10-01-fase2g-audio-design.md · **ADR:** 0015.

## Global Constraints
- PC → Quest: 48 kHz estéreo, quadro de 10 ms (480 amostras), 128 kbps, Opus `RESTRICTED_LOWDELAY`.
- Quest → PC: 48 kHz mono, quadro de 20 ms (960 amostras), 24 kbps.
- Pacote: `"SPAU"` · versão 1 (u8) · canais (u8) · seq (u32) · capture_us (u64) · payload Opus. Little-endian.
- Quest: começa a tocar com 40 ms no buffer; descarta acima de 120 ms; silêncio em falta.
- Host devolve a saída padrão original em todo encerramento (desconexão, Ctrl+C, fim do processo).
- Áudio nunca atravessa o C# no Quest.

### Task 1: Protocolo
`control.fbs`: `StartAudio { udp_port:ushort; playback:bool; mic:bool; }`, `AudioStarted { mic_port:ushort; speakers:bool; mic:bool; }`
(fim do union); mensagens na Application; codec; `protocol/audio-wire.md`. Teste: ida e volta.
Pacote de áudio: `AudioPacket` em Host.Domain (Write/TryRead) com teste xUnit; o mesmo layout no C++ do `.aar`.

### Task 2: Host — dispositivos e saída padrão
`AudioDevices` (NAudio): acha "Steam Streaming Speakers" e o par do microfone; `DefaultOutputSwitch` (IPolicyConfig) troca
para a do Steam e devolve a original (`IDisposable`, também no `ProcessExit`/Ctrl+C). Regra pura `AudioRouting.Plan(...)`
(Host.Domain) decide o que fazer com/sem drivers, com teste.

### Task 3: Host — som do PC → Quest
`PcAudioStreamer`: `WasapiLoopbackCapture` → int16 → Concentus → `AudioPacket` → UDP. Teste de PC: comando
`ScreenPortalHost audio-test` toca um tom na saída do Steam e decodifica o próprio stream (RMS > 0).

### Task 4: Host — microfone do Quest → PC
`MicPlayer`: UDP → `AudioPacket` → Concentus → `BufferedWaveProvider` (máx. 100 ms) → `WasapiOut` no par do Steam.
`AudioHost` + `HeadsetSession` (`StartAudio` → `AudioStarted`). Teste xUnit da sessão (fake `IAudioHost`).

### Task 5: Headset nativo (`.aar`)
`audio_session.{h,cpp}`: recepção + buffer + `AMediaCodec` decoder Opus + AAudio saída; microfone AAudio entrada +
encoder Opus + UDP. JNI `NativeAudio` (Kotlin + C++). `tools/build-native.ps1`.

### Task 6: Unity
`IAudioLink` (Application) + `NativeAudioLink` (Infrastructure, Android; no-op no Editor); `AudioSync` (Application):
`StartAudio` no Welcome e na mudança dos botões, liga o nativo com `AudioStarted`; menu "Som do PC" e "Microfone";
permissão de microfone; `RECORD_AUDIO` no manifest. Testes do `AudioSync` e do menu.

### Task 7: Quest + fechamento
APK, host; som só no Quest; claquete (dessincronia < 45 ms); microfone numa chamada de teste; mudo. Relatório
`docs/measurements/fase2g-audio.md`, STATUS, merge.
