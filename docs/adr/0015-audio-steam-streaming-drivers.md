# ADR 0015 — Áudio pelos drivers de streaming do Steam (e microfone do Quest no escopo)

- **Status:** Aceito (Fase 2G, 01/10/2026, aprovado pelo Cayo)
- **Spec:** docs/specs/2026-10-01-fase2g-audio-design.md · **Spike:** docs/measurements/fase2g-spike-audio.md

## Contexto
O brief prevê o som do PC no Quest. O Cayo pediu também que (1) o som saia **só** no Quest e (2) o **microfone do Quest**
funcione no PC (chamadas). As duas coisas exigem dispositivos de áudio virtuais no Windows. O Virtual-Audio-Driver
(VirtualDrivers, MIT) não serve hoje: sem releases assinadas e com o microfone virtual que só entrega silêncio.

## Decisão
1. **Microfone do Quest entra no escopo do MVP** (brief v1.3.5).
2. Usar os drivers **Steam Streaming Speakers / Steam Streaming Microphone** (assinados pela Valve, já instalados com o
   Steam) até a Fase 5: saída silenciosa para o loopback e o par "tocar → microfone".
3. Dependências novas do host: **NAudio.Wasapi** (MIT) para WASAPI e **Concentus** (port C# do Opus, BSD) para o codec.
   No Quest, Opus pelo `MediaCodec` do Android (sem libopus no `.aar`).
4. Sem os drivers do Steam: loopback da saída padrão (som também no PC) e sem microfone, com aviso.

## Consequências
- Requisito de instalação até a Fase 5: Steam instalado (drivers de streaming presentes).
- Distribuição (Fase 5): driver de áudio próprio assinado, ou o Virtual-Audio-Driver com um caminho de injeção no
  microfone. O resto do caminho (Opus, pacotes, nativo no Quest) não muda.
- A saída padrão do Windows muda enquanto o headset está conectado; o host precisa devolvê-la em todo encerramento.
