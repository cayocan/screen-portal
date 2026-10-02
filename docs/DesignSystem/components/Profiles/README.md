# Profiles
Subpáginas de perfis de layout: lista com atalhos numerados, salvar o layout atual com nome, e o detalhe de um perfil.

**O consumidor fornece:** os perfis (nome, número 1–9, resumo "4 telas · cilindro", ancorado na mesa ou não), qual está ativo e se ele tem mudanças não salvas.

**Onde fica:** Espaço → "Perfil · <ativo> ▸". A troca rápida é `Ctrl+Alt+1…9` (`ProfileSwitcher`).

**Regras**
- Cada linha: `Kbd` com o número, nome em `label` (o ativo em estado selecionado, com ✓ antes do nome), resumo em `caption` terminando em "· na mesa" se ancorado, "▸" para o detalhe. Clicar na linha aplica; o "▸" abre o detalhe.
- Mudanças não salvas: a linha de status diz "<perfil> tem mudanças não salvas". Nunca salve sozinho por cima de um perfil.
- Salvar: o nome é digitado no **teclado físico** (mouse e teclado primeiro); o campo abre focado com caret `portal`. Chips sugerem nomes comuns. O atalho é o próximo número livre.
- **Ancorar na mesa:** o perfil guarda as posições relativas à âncora da mesa (volta ao mesmo lugar da sala). Desligado, as posições são relativas à cabeça e o perfil é recentrado ao aplicar.
- Aplicar um perfil é desfazível (toast "Perfil: Reunião · Desfazer"). Monitores virtuais que faltam são criados no PC; os que sobram ficam escondidos, não removidos.
- Apagar perfil: destrutivo, com "Desfazer".
