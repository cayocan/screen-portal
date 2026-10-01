# Fase 2G — Áudio do PC no Quest + microfone do Quest no PC — Design

**Data:** 01/10/2026 · **Aprovado por:** Cayo (brainstorming de 01/10/2026)
**Brief:** §6.3 ("PC: áudio": WASAPI loopback → Opus → UDP; AAudio no `.aar`; sincronia pelo timestamp do vídeo),
barra de qualidade (dessincronia < 45 ms). **Spike:** [fase2g-spike-audio.md](../measurements/fase2g-spike-audio.md) ·
**ADR:** [0015](../adr/0015-audio-steam-streaming-drivers.md).

## 1. Decisões
| # | Decisão |
|---|---|
| 1 | Som do PC **só no Quest**: com o headset conectado, a saída padrão do Windows vira "Steam Streaming Speakers"; o host captura em loopback e devolve a saída original ao desconectar ou ao fechar |
| 2 | **Microfone do Quest no PC** (escopo novo, ADR 0015): o host toca o áudio do microfone em "Alto-falantes (Steam Streaming Microphone)"; os apps usam "Microfone (Steam Streaming Microphone)" |
| 3 | Codec **Opus** nos dois sentidos. PC → Quest: 48 kHz estéreo, quadros de 10 ms, 128 kbps. Quest → PC: 48 kHz mono, 20 ms, 24 kbps |
| 4 | No Quest, áudio **nativo** no `.aar` (UDP → buffer → MediaCodec Opus → AAudio), como o vídeo: o áudio não atravessa o C# |
| 5 | Menu do pulso: **"Som do PC: ligado/desligado"** e **"Microfone: ligado/mudo"** (os dois começam ligados; mudo corta no Quest) |
| 6 | Sem os drivers do Steam: aviso no log/menu; loopback da saída padrão (som também no PC) e sem microfone |

## 2. Protocolo (`protocol/control.fbs`, `protocol/audio-wire.md`)
- `StartAudio { udp_port:ushort; playback:bool; mic:bool; }` (headset → host, depois do Welcome e quando os botões mudam).
- `AudioStarted { mic_port:ushort; speakers:bool; mic:bool; }` (host → headset: o que está disponível).
- Pacote de áudio (UDP, os dois sentidos): `"SPAU"` · versão u8 · canais u8 · seq u32 · capture_us u64 (relógio do host;
  no microfone, relógio do headset) · payload Opus (1 quadro). Little-endian.

## 3. Host
- `AudioDevices`: lista saídas/entradas (NAudio `MMDeviceEnumerator`), acha as do Steam pelo nome, troca a saída padrão
  (IPolicyConfig, os três papéis) e guarda a original para devolver. Restauração também no Ctrl+C/fim do processo.
- `PcAudioStreamer`: `WasapiLoopbackCapture` → float → int16 → Opus (Concentus, `RESTRICTED_LOWDELAY`) a cada 480
  amostras → pacote → UDP para o headset.
- `MicPlayer`: UDP → Opus → `BufferedWaveProvider` (buffer máx. 100 ms, descarta o excesso) → `WasapiOut` (latência 20 ms)
  no par do Steam.
- `HeadsetSession` trata `StartAudio`; `AudioHost` (porta) liga/desliga e responde `AudioStarted`. Uma sessão de áudio por
  host (o headset atual); sessão substituída → áudio da nova.

## 4. Headset
- `.aar`: `AudioSession` (C++): thread de recepção → buffer anti-jitter ordenado por seq → `AMediaCodec` "audio/opus"
  (csd-0 = OpusHead, csd-1/csd-2 = pre-skip e seek pre-roll) → anel de PCM → callback do AAudio (baixa latência).
  Começa a tocar com **40 ms** no buffer; acima de 120 ms descarta os mais velhos; sem dados toca silêncio.
  Microfone: AAudio de entrada 48 kHz mono → `AMediaCodec` encoder "audio/opus" → pacote → UDP para `mic_port`.
- JNI `NativeAudio.start(host, rxPort, txPort, playback, mic)` / `stop` / `setPlayback` / `setMic` / `takeStats`.
- Unity: `AudioSync` (Application) manda `StartAudio` no Welcome e quando os botões mudam, e liga a sessão nativa com o
  `AudioStarted`; `IAudioLink` (port) com a implementação nativa em Infrastructure. Permissão de microfone pedida na
  primeira vez que o microfone liga.

## 5. Testes e verificação
**Automáticos:** codec do protocolo; pacote de áudio (ida e volta, rejeita magic/versão errados); `AudioDevices` decide a
troca e a restauração (com fakes); `AudioSync` (manda `StartAudio` no Welcome e na mudança, liga/desliga o nativo);
botões do menu.
**PC:** loopback → Opus → decodificador de teste no host recupera o tom (RMS).
**Quest:** música/vídeo do PC no Quest, sem som nas caixas do PC; claquete (piscar + bip) para medir a dessincronia
(meta < 45 ms); chamada de teste (gravador de voz do Windows ou Discord) com o microfone do Quest; mudo.
