# Fase 2D — Recriar com outra resolução — Relatório da parte "código puro"

**Data:** 30/09/2026 · **Branch:** `worktree-agent-adabe15aea45e8df9` (worktree `.claude/worktrees/agent-adabe15aea45e8df9`)
**Base:** `a913171` (tip de `phase2e/curved-screens`: é onde estão a spec 2D e o ADR 0012; o worktree nasceu em `74b99d8` e foi
avançado por fast-forward). **Plano:** [2026-09-30-fase2d-recriar.md](2026-09-30-fase2d-recriar.md).

## Tarefas

| # | Tarefa | Commit | Testes |
|---|---|---|---|
| — | Plano | `b673dac` | — |
| 1 | "Recriar ▸" e modo recriar do seletor (`WristMenu`) | `1f7e974` | UnityPureCode 215 → 221 |
| 2 | `HostSync`: undo do recriar pede o tamanho anterior na mesma chave (caracterização; passou de primeira) | `24d7a5f` | 222 |
| 3 | Plano de movimento de janela normal/maximizada/minimizada (`WindowRelocation.Plan`) | `02915a8` | Host 71 → 75 |
| 4 | `MonitorRecreator<T>` (ordem criar → mover → parar stream → remover → aluguel; falha ao criar mantém o antigo) | `d9edcfb` | 79 |
| 5 | `Win32WindowMover`, `VirtualDisplayMonitors`, `ScreenHost.Recreate` (só compilado: `dotnet build host/Host.App`, 0 avisos) | `29bbf8b` | 79 |
| 5b | Revisão: pula janelas travadas (`IsHungAppWindow`) — `SetWindowPos` nelas bloquearia com o lock do `ScreenHost` preso | `d11b223` | 79 |

Final: **UnityPureCode 222/222**, **Host.Tests 79/79**, Host.App compila sem avisos. Nada rodado na Unity, no Quest nem o host.

## Decisões onde a spec é omissa (também no plano, "Desvios e decisões")

1. **Título "Recriar: <nome>" vai na linha de status** (o painel não tem título) → nenhuma mudança no Presentation.
2. **Marca da resolução atual por texto:** `"2560×1440 · atual"`, apagada (o painel já usa o material de desabilitado).
3. **Orientação inicial do seletor = a da tela em foco**; a orientação continua lembrada depois (como na 2C).
4. **Alvo fixado ao entrar no modo**; esconder o painel ou voltar à principal sai do modo sem enviar nada.
5. **Falha ao mover janelas ou ao remover o antigo não desfaz a recriação** (o novo já está alugado); vai para o log.
6. Troca de **tipo** na mesma chave (não acontece hoje) segue o caminho antigo (destrói e cria).
7. `rcNormalPosition` (maximizada/minimizada) está em coordenadas de área de trabalho: conversão pelo canto da área de
   trabalho do monitor principal (`SPI_GETWORKAREA`). Com a barra embaixo o deslocamento é 0.
8. Maximizada: `SetWindowPlacement(SW_SHOWNOACTIVATE)` com a restauração já no monitor novo → `WindowOps.Move` (duas
   chamadas, DPI) → `ShowWindow(SW_SHOWMAXIMIZED)` (este último ativa a janela; não há maximizar sem ativar).
   Minimizada: `SetWindowPlacement(SW_SHOWMINNOACTIVE)` com a restauração mapeada (flags preservadas).
9. Janelas movidas: nível superior, visíveis, não "cloaked" (DWM) e não travadas.

## Para as tarefas da Unity (fila: Tasks 6–8 do plano)

- **Nenhum arquivo novo em `Assets/`** → nenhum `.meta` a gerar. Presentation sem mudança esperada.
- Conferir no painel: o glifo `▸` na fonte (se virar quadrado, trocar `WristMenu.RecreateLabel`), "Recriar: 2560×1440" na
  linha de status e "3840×2160 · atual" no botão de 0,075 m.
- Menu principal agora tem **11 itens** (12 com "+ Tela de teste" em build de desenvolvimento = `MaxItems`).
- Roteiro do Simulator + host real (Task 7) inclui janela maximizada, minimizada e, se possível, barra de tarefas no topo
  (valida a decisão 7). Log do host a procurar: `recriado 2560×1440 → 1920×1080 (\\.\DISPLAYa → \\.\DISPLAYb, N janela(s) movida(s))`
  e, na falha, `recriar … falhou; … mantido`.

## Preocupações

1. **Conflito de merge com a 2E / limite de itens:** a 2E acrescenta "Curvatura ▸" à principal. Com o item de teste
   dá **13 > `MaxItems` (12)**; o painel só cria 12 botões. Decidir no merge: subir `MaxItems` (o `WristMenuPanel` cria
   `MaxItems` botões; a altura do painel cresce) ou tirar o item de teste da principal. As mudanças no `WristMenu` ficaram
   localizadas (enum no fim, arrays, casos novos nos switches, métodos novos depois de `Create`), mas as linhas dos
   arrays da página principal e do fim do enum vão conflitar trivialmente.
2. **Rótulo desatualizado:** o rótulo de um monitor virtual é a resolução de criação ("2560×1440") e `RecreateScreen` não
   o altera. Depois de recriar, o título mostra "Recriar: 2560×1440" numa tela de 1920×1080 (a marca "· atual" continua
   certa). Correção sugerida (Domain, fora do escopo aprovado): `RecreateScreen` trocar o rótulo quando ele for igual à
   resolução antiga. Precisa do ok do Cayo.
3. **Falha ao criar mantém o stream antigo rodando** (a spec pede o antigo intacto), mas o headset fecha o decoder no
   `ScreenFailed`: o host continua mandando quadros para a porta até o próximo pedido. Sem impacto funcional; custo de banda.
4. **Janela maximizada é ativada** ao maximizar de novo (rouba o foco). Se incomodar, trocar por `SetWindowPlacement`
   com `SW_SHOWMAXIMIZED` e verificar se o Windows maximiza no monitor da restauração.
5. **Coordenadas de área de trabalho** (decisão 7) são da documentação, não medidas; confirmar com a barra no topo/esquerda.
6. O trabalho de mover janelas acontece com o lock do `ScreenHost` preso (como a criação já fazia; poucas centenas de ms).
