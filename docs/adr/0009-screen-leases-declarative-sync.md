# ADR 0009 — Aluguéis de monitor e sincronização declarativa headset ⇄ host (protocolo v2)

- **Status:** Aceito (Fase 2C, 30/09/2026, aprovado pelo Cayo no brainstorming)
- **Spec:** docs/specs/2026-09-30-fase2c-varias-telas-design.md · **Protocolo:** protocol/control-v2.md

## Contexto
Na 2C o headset cria e remove monitores virtuais (SudoVDA) e espelhos de monitores físicos. Três fluxos mexem nisso:
undo/redo (a workspace volta atrás), remover com prazo (10 s para desfazer sem perder as janelas) e reconexão (Wi-Fi
caiu, óculos dormiu). Mensagens imperativas por Command exigiriam operações inversas no undo e um caminho separado de
ressincronização na reconexão: três caminhos para o mesmo estado.

## Decisão
1. **A workspace do headset é a fonte da verdade.** Um reconciliador puro (`HostSync`, camada Application) compara as
   telas com vídeo da workspace com o estado de cada chave no host e envia só as diferenças (`AcquireScreen`,
   `StartStream`, `ReleaseScreen`). Nenhum Command fala com o host.
2. **Chave estável = `ScreenId`.** Undo restaura o mesmo id, então o mesmo pedido reata o mesmo monitor.
3. **Aluguéis no host** (`ScreenLeaseTable`, `(device, screen_key)` → monitor, tipo, expiração): remover = expira em
   10 s; desconectar = expira em 5 min; `AcquireScreen` da mesma chave antes do prazo cancela a expiração. Aluguéis só em
   memória; se o host morrer, o watchdog do SudoVDA remove os monitores.
4. **Welcome lista os aluguéis vivos**: o headset libera os que a workspace não tem mais.

## Consequências
- Undo, remoção adiada e reconexão usam o mesmo caminho e são testáveis sem Unity nem Windows.
- O host precisa de uma fila de trabalho por sessão (criar monitor + esperar a captura leva até ~3 s).
- Um só headset por PC (mais de um fica fora do escopo; a chave já inclui o device).
