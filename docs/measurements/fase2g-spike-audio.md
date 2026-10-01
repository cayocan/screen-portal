# Fase 2G — spike de áudio (01/10/2026)

**Pergunta:** como tocar o som do PC só no Quest e levar o microfone do Quest para o Windows (Teams, Discord, Meet)?

## Virtual-Audio-Driver (VirtualDrivers, MIT) — descartado por agora
- Sem releases publicadas: precisaria compilar e assinar o driver (ou ligar o test signing do Windows).
- O **microfone virtual só entrega silêncio**: a captura (`CMiniportWaveRTStream::WriteBytes`) preenche o buffer com
  zeros; não há caminho para um programa escrever no microfone. Usá-lo exigiria mexer no driver de kernel.

## Drivers do Steam (já instalados no PC do Cayo, assinados pela Valve)
Teste com WASAPI (NAudio.Wasapi 2.2.1, programa de spike fora do repositório), tom de 440 Hz, 48 kHz:

| # | Caminho | Resultado |
|---|---|---|
| A | tocar em "Alto-falantes (Steam Streaming Microphone)" → capturar "Microfone (Steam Streaming Microphone)" | ✅ tom chega (RMS 0,27), primeiro som em ~56 ms (inclui partida do áudio e buffer de 20 ms) |
| B | tocar em "Steam Streaming Speakers" → captura loopback | ✅ (RMS 0,19), 48 kHz estéreo float |

## Conclusão
- **Som do PC só no Quest:** o host põe "Steam Streaming Speakers" como saída padrão enquanto o headset está conectado
  (e devolve a original ao desconectar) e captura essa saída em loopback.
- **Microfone do Quest no PC:** o host toca o áudio recebido em "Alto-falantes (Steam Streaming Microphone)"; os apps usam
  "Microfone (Steam Streaming Microphone)".
- **Limite:** depende do Steam instalado e os drivers não podem ser distribuídos com o app. Distribuição (Fase 5): driver
  de áudio próprio assinado (ou o Virtual-Audio-Driver com um caminho de injeção no microfone). Sem os drivers do Steam, o
  host cai para o loopback da saída padrão (som também no PC) e sem microfone, com aviso.
