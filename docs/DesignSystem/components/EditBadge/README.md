# EditBadge
O selo que avisa que o modo edição está ligado e como sair; segue a cabeça na parte de baixo da visão.

**O consumidor fornece:** o modo (fixo ou temporário, enquanto `Ctrl+Alt` está segurado).

**Regras**
- Pílula `edit` com texto `on-edit` em `label`, `halo` por fora (lê sobre qualquer passthrough). A tecla vai num `Kbd` invertido.
- Textos: fixo "Modo edição · sair Esc"; temporário "Modo edição · enquanto segura Ctrl+Alt".
- A `dist-hud`, 15° abaixo do horizonte, lazy follow (só se move ao sair de 20° do centro).
- É a única pílula amarela do app: nada fora do modo edição usa `edit`.
