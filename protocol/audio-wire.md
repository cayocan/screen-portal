# Pacote de áudio (Fase 2G)

UDP, os dois sentidos (som do PC → Quest; microfone do Quest → PC). Um quadro Opus por pacote. Little-endian.

| Offset | Tamanho | Campo |
|---|---|---|
| 0 | 4 | magic `"SPAU"` |
| 4 | 1 | versão (= 1) |
| 5 | 1 | canais (2 = som do PC, 1 = microfone) |
| 6 | 4 | seq (u32, +1 por quadro; o receptor reordena e descarta atrasados) |
| 10 | 8 | capture_us (u64; som do PC: relógio do host, o mesmo do vídeo; microfone: relógio do headset) |
| 18 | n | payload Opus (48 kHz; som do PC: 10 ms estéreo 128 kbps; microfone: 20 ms mono 24 kbps) |

Portas: o headset informa onde recebe o som do PC em `StartAudio.udp_port` (padrão 9300); o host informa onde recebe o
microfone em `AudioStarted.mic_port` (padrão 9301).
