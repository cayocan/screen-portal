# ProfileSwitcher
Faixa no HUD que mostra os perfis enquanto `Ctrl+Alt` está segurado e confirma a troca feita com `Ctrl+Alt+1…9`.

**O consumidor fornece:** os perfis com número e nome, e o ativo.

**Regras**
- Aparece junto do selo temporário do modo edição (segurar `Ctrl+Alt`), logo acima dele, a `dist-hud`: o usuário vê os números antes de apertar.
- Item ativo: fundo `portal`, texto `on-portal`. Números em `mono`.
- Ao trocar, a faixa marca o novo ativo por 600 ms e some; um toast confirma com "Desfazer".
- No máximo 9 itens; nomes com mais de 12 caracteres são cortados com "…".
