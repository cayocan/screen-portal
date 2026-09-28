# Fase 2A — Spikes de risco: driver de monitor virtual e capacidade do decoder — Design

- **Status:** aprovado pelo Cayo em 28/09/2026 (brainstorming).
- **Brief:** §14 Fase 2 (spike do driver, 1º item), §15.7 (IddCx), D3/D4/D5, §8.8/§11 (fps por tela, nº de telas),
  pendência da Fase 0 (docs/measurements/fase0.md §3).
- **Saídas:** ADR 0007 (driver), ADR 0008 (limites de decode), `docs/measurements/fase2a-driver-<driver>.md`,
  `docs/measurements/fase2a-decoder.md`.

## 1. Decisões

| # | Decisão |
|---|---|
| 1 | Testar **VDD** (VirtualDrivers, decisão D3) **e SudoVDA** (já instalado no PC; usado pelo Apollo) com os mesmos testes. Parsec VDD fora (licença de redistribuição) |
| 2 | Critério de sucesso do decoder = **cenário real do brief: 1 tela@60 + 3@15** (MVP 4 telas). "Todas a 60" é informativo |

## 2. Spike do driver

Comando repetível `ScreenPortalHost display-spike --driver vdd|sudovda`, com verificação automática e relatório em
`docs/measurements/fase2a-driver-<driver>.md`. Controle de cada driver atrás de `IVirtualDisplayDriver`
(`CreateMonitor(width, height) → id`, `RemoveMonitor(id)`, `Capabilities`); a API exata de cada driver (VDD: XML de
configuração + named pipe; SudoVDA: IOCTL) é levantada no início da implementação.

| # | Teste | Verificação automática |
|---|---|---|
| 1 | Criar/remover com resolução exata (2560×1440, 3440×1440, 1920×1080) | enumeração de monitores confere a resolução; tempo de criação/remoção |
| 2 | Monitores simultâneos | cria até falhar ou até 8 |
| 3 | Retrato nativo 1440×2560 | resolução em pé sem rotação do Windows; WGC + encode entregam 1440×2560 |
| 4 | Recriar com outra resolução movendo janelas | janela de teste no monitor; após recriar, está no monitor novo na mesma posição relativa |
| 5 | Monitor-palco com recorte | captura WGC por janela; redimensionar a janela atualiza o recorte |
| 6 | Limite do H.264 da GPU | abrir o encoder em 4096 / 5120 / 7680 de largura; registrar falhas |

- **Instalação do VDD:** ação do Cayo (admin, aceitar o driver). SudoVDA já instalado.
- **ADR 0007:** confirma D3 (VDD) ou justifica SudoVDA, com a tabela comparativa.

## 3. Spike do decoder

**Correções antes de medir:**
1. `stream_session.cpp`: esperar até **16 ms** por input buffer; se faltar, descartar, enviar `RequestKeyframe` e esperar
   só esse IDR (não o periódico).
2. Protocolo: mensagem nova `RequestKeyframe { stream_id: ushort }` (headset → host), **no fim da union** (compatível
   com v1). Host força IDR imediato; IDR periódico passa de 1 s para **5 s** (rede de segurança).
   Canal: pelo controle TCP da 1B (o decoder nativo sinaliza a necessidade; a máquina de estados envia).
3. Host: `stream --mix "60x1,15x3"` roda pipelines simultâneos por taxa, portas consecutivas, stream ids distintos.

**Condições controladas:** conteúdo fixo (`tools/text-test.html`), headset com bateria ≥ 80 % e no cabo,
**3 repetições de 60 s** por cenário; `tools/phase0-streams-test.ps1` aceita `-Mix` e reporta média e pior caso.

| Cenário | Critério |
|---|---|
| **1@60 + 3@15, 2560×1080** | ✅ nas 3 repetições: foco ≥ 58 fps; demais ≥ 14 fps; latência média < 50 ms; 0 descartes do decoder |
| 1@60 + 5@15 | informativo (limite da FocusPolicy) |
| N@60, N = 2, 3, 4 | informativo (quantas telas podem ficar em "Video") |
| 1@60 + 3@15 em 3840×2160 | informativo (custo 4K) |

**ADR 0008:** limites para o `BandwidthBudget` (2F). Se o critério do MVP falhar: plano B do brief §12 (atlas).
**Onde testar:** Quest (só o hardware responde sobre o decoder) — uma sessão de ~20 min.

## 4. Fora do escopo

Integrar o driver ao fluxo do app (2C), UI de criação (2D), políticas de fps (2F). O spike produz ferramentas de
medição e decisões; o código de produção entra nos subprojetos seguintes.
