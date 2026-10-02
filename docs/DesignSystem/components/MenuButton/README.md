# MenuButton
Botão do menu do app: alvo de 5° (`target`) que funciona igual com laser, pinça e cursor do mouse.

**O consumidor fornece:** o texto (verbo ou substantivo, sem ponto), opcionalmente um ícone à esquerda, uma linha `sub` em `caption`, um texto final (valor atual + "▸" para submenu) ou um toggle, e o estado.

**Estados**
- `normal`: `panel-raised` + contorno `edge`.
- `hover` (laser ou cursor em cima): `panel-hover` + anel `portal` em `stroke-focus`. Sem hover não há clique: o anel é a confirmação de mira.
- `pressionado` / ligado: fundo `portal`, texto `on-portal`, por 120 ms após o clique.
- `selecionado` (opção atual numa subpágina): `portal-tint` + ✓ à esquerda. Substitui o "● " atual no texto.
- `desabilitado`: `panel-sunken` + `ink-faint`, sem contorno, sem hover. Use quando a ação existe mas não se aplica agora (sem histórico, sem PC); se nunca se aplica nesse contexto, esconda.
- `aviso`: contorno `warn` + ícone "!" (ex.: "Fixo 15" quando o total de fps é alto).

**Faça**
- Mostre o valor atual no fim: "Curvatura · Média ▸", "Taxa de quadros · Auto ▸".
- Use toggle para ligado/desligado ("Grade", "Modo edição"), nunca o texto "ligado/desligado".
- Ações destrutivas ("Remover tela") com ícone × em `danger` e sempre desfazíveis (toast com "Desfazer").

**Não faça**
- Texto menor que `label`, duas linhas de título, ou botão com menos de `target-min` de altura.
