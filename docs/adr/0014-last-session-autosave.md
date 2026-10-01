# ADR 0014 — Última sessão salva no headset (antecipa um pedaço da Fase 3b)

- **Status:** Aceito (30/09/2026, decisão do Cayo depois do teste no Quest)

## Contexto
Até os perfis (Fase 3b), a workspace vivia só na memória. Quando o app reiniciava (reinstalação, crash, o sistema
fechando o app), ele abria vazio: recriava só o espelho automático, e o `HostSync` liberava os monitores virtuais que o
host ainda alugava (expiravam em 10 s). O Cayo perdeu uma tela assim e esperava que ela voltasse.

## Decisão
1. A workspace inteira (telas, posições, tamanhos, curvatura, origem, rótulos, superfície, foco e próximo id) é salva
   localmente a cada mudança, no máximo a cada 0,5 s, e também quando o app vai para segundo plano.
   Arquivo: `persistentDataPath/last-session.txt`, texto versionado (`SessionCodec`), gravado num temporário e trocado.
2. Na abertura, a sessão salva vira o estado inicial do `WorkspaceStore`, sem histórico de undo. O `HostSync` reata os
   monitores que o host ainda aluga (até 5 min) com as janelas no lugar; depois disso, recria vazios na mesma resolução.
3. Com sessão restaurada, o recentrar automático da abertura **também roda**. Verificado no Quest: cada abertura do app
   tem um tracking space novo (origem e frente onde o óculos estava), então o centro salvo da superfície cai em outro
   lugar. As posições das telas são relativas à superfície, então o layout volta igual em volta do usuário.
4. Arquivo ilegível ou de outra versão → abre vazio, como antes.

## Consequências
- Não são perfis: um só "estado atual", sem nomes, sem âncoras na mesa (continua na Fase 3b, que vai reaproveitar o
  `SessionCodec`).
- As telas voltam na mesma disposição em volta da cabeça, não no mesmo ponto do quarto (isso exige spatial anchors,
  Fase 3b).
