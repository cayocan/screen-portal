# Fase 1B — verificação da conexão no Quest 3

Data: 28/09/2026 · Quest 3 (192.168.0.171, Wi-Fi) · PC DESKTOP-3272RGP (192.168.0.64, cabo; também tem Radmin VPN) ·
host `ScreenPortalHost serve` (Release) · app com cena `Main` · branch `phase1b/connection`.

## Roteiro

| # | Passo | Esperado | Observado | Resultado |
|---|---|---|---|---|
| 1 | App aberto sem host | "Procurando seu PC…" → após 10 s "Não encontramos seu PC…" | Exatamente isso | ✅ |
| 2 | `serve` + pareamento | código no headset e no PC → Permitir → tela do PC no Quest | Descoberto por mDNS em ~1 s; código 245 993 nos dois lados; vídeo 2560×1080 ~60 fps | ✅ (bug encontrado, ver abaixo) |
| 3 | Fechar e reabrir o app | conecta sem pedir código | 14:04:28 reconectou sem pareamento; vídeo em 1 s | ✅ |
| 4 | Derrubar o host e subir em 2 s | "Reconectando…" → vídeo volta | Host de volta 14:05:00.49 → headset conectado e vídeo em < 1 s (queda total ~3 s, 2 s propositais) | ✅ (rápido demais para ver o texto) |
| 5 | Negar o pareamento | "Conexão recusada no PC", sem tela; bloqueio de 10 s | Recusado 14:05:55; nova tentativa exatamente 10 s depois (14:06:05) | ✅ (texto não observado: o Cayo olhava o diálogo no PC) |
| 2b | Reteste após correção: aprovar devagar (22 s) | conecta direto, sem queda | 14:20:05 → 14:20:27 aprovado; vídeo no mesmo segundo; nenhuma reconexão | ✅ |

Vídeo durante os testes: 59–60 fps, 16–20 Mbps, captura→encode 7,5–7,8 ms (máx. ~17 ms).
Reconexão em localhost (teste de integração do host): < 143 ms incluindo pareamento e stream.

## Bugs encontrados no Quest (e corrigidos com teste)

1. **Timeout de negociação contava o tempo de aprovação do pareamento.** Com ~7 s para clicar em Permitir, o `Welcome`
   chegou num frame depois do `PairingResult` e o headset derrubou a conexão (recuperou sozinho em seguida).
   Correção: o timer recomeça quando o pareamento é aceito. Teste: `SlowPairingApproval_DoesNotTimeOutBeforeWelcome`.
2. **mDNS anunciava o IP do Radmin VPN (26.x).** Correção: usar o IP da rota padrão do Windows (encontrado no PC, antes do Quest).

## Observações

- Com o headset fora da cabeça o app pausa e a conexão para (esperado). O sensor de proximidade volta a valer quando o
  USB cai; para testes longos, `adb` pelo Wi-Fi (`adb tcpip 5555` + `adb connect 192.168.0.171:5555`).
- Mensagens curtas ("Reconectando…") podem não ser vistas quando a reconexão leva < 1 s — comportamento desejado.
