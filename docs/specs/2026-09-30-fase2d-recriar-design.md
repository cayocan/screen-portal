# Fase 2D — Recriar com outra resolução — Design

**Data:** 30/09/2026 · **Aprovado por:** Cayo (brainstorming de 30/09/2026)
**Depende de:** 2C (seletor, `HostSync`, aluguéis de monitor — [ADR 0009](../adr/0009-screen-leases-declarative-sync.md)).
**Brief:** §10.4 (recriar), v1.3.3 · [ADR 0012](../adr/0012-no-custom-resolution-no-ghost-outline.md) (escopo).

## 1. Decisões

| # | Decisão |
|---|---|
| 1 | A 2D é só **recriar com outra resolução** (proporção e orientação incluídas). Contorno fantasma e resolução personalizada saem do escopo (ADR 0012) |
| 2 | Entrada: item **"Recriar ▸"** no menu do pulso, para a tela em foco, que abre o seletor de proporção → resolução da 2C |
| 3 | **Sem confirmação:** escolher a resolução recria na hora; o undo recria na resolução anterior |
| 4 | O host cria o monitor novo, **move as janelas** do antigo para ele (posições relativas mantidas), e só então remove o antigo |
| 5 | No headset a tela fica no mesmo lugar e com a mesma largura; a altura segue a nova proporção (`RecreateScreen`, já existente) |

## 2. Escopo

**Dentro:** item e modo "recriar" do seletor; recriação no host com mudança das janelas; undo; testes; verificação no
Simulator (host real) e no Quest.

**Fora:** contorno fantasma e resolução personalizada (ADR 0012); recriar espelho ou tela de teste (espelho segue a
resolução do monitor físico; tela de teste não tem monitor).

## 3. Headset (Application)

Já existe: `RecreateScreen(screenId, resolution)` (mantém pose e largura), `HostSync` trata troca de resolução na
mesma chave (mantém o slot, pede `AcquireScreen` com o tamanho novo), `VideoScreenView` reconstrói a layer.

- **Menu:** "Recriar ▸" na página principal; apagado sem PC, sem tela em foco ou quando a tela em foco não é
  monitor virtual.
- **Modo recriar do seletor:** as mesmas páginas de proporção (com Paisagem/Retrato) e resoluções, com título
  "Recriar: <nome>"; a resolução atual aparece marcada e apagada. Escolher envia `RecreateScreen(foco, resolução)` e
  volta à página principal. "← Voltar" sai do modo sem mudar nada.
- O aviso de "Mais de 4 telas" não se aplica (a contagem não muda).

## 4. Host

- `AcquireScreen` da mesma chave com **outro tamanho** (virtual) = **recriar**:
  1. cria o monitor novo no SudoVDA (resolução exata);
  2. move para ele as janelas de nível superior visíveis que estão no monitor antigo, com
     `WindowRelocation.Map(janela, monitorAntigo, monitorNovo)`; maximizada: restaura → move → maximiza; minimizada:
     move a posição de restauração (`SetWindowPlacement`);
  3. para o stream do antigo e o remove;
  4. o aluguel passa a apontar para o monitor novo (mesma chave, tamanho novo).
- Falha ao criar o novo: o antigo fica intacto e a resposta é de falha (o headset mostra "Falha").
- O undo chega como outro `AcquireScreen` com o tamanho anterior: o mesmo caminho, no sentido inverso.
- A lógica de ordem e troca de aluguel fica testável (xUnit) com driver e mover-janelas por interface.

## 5. Testes e verificação

**Automáticos:**
- Harness `dotnet test`: menu (item apagado nos casos certos; modo recriar marca a atual; escolher envia
  `RecreateScreen`; voltar não envia nada); undo volta à resolução anterior; `HostSync` pede o tamanho novo na mesma chave.
- Host (xUnit): recriar cria antes de remover, move as janelas entre os dois, troca o aluguel; falha ao criar mantém o
  antigo; mapeamento de janela maximizada/minimizada.

**Meta XR Simulator + host real:** recriar pelo menu; o monitor muda de resolução no Windows e o Bloco de Notas vai
junto; undo volta.

**Quest:** recriar uma tela com vídeo e desfazer; a janela vai e volta, o vídeo reaparece.

**Documentação:** ADR 0012; brief v1.3.3; relatório em `docs/measurements/fase2d-*.md`; STATUS (2D = recriar).
