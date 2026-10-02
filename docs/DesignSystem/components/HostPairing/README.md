# HostPairing
O diálogo do Windows que confirma o pareamento com o mesmo código que o óculos mostra no `ConnectionStatus`. Substitui a caixa de diálogo genérica de hoje.

**O consumidor fornece:** o nome do óculos, o código e o resultado.

**Regras**
- Janela pequena (420 px), sempre por cima e com foco. Título "Um óculos quer se conectar", uma frase pedindo para conferir o código, e o código em `host-code` sobre `panel-sunken` (mesmos grupos de 3 do óculos).
- Botões: "Recusar" (secundário) à esquerda de "Confirmar" (primário, `portal` com `on-portal`), como no Windows. `Enter` confirma, `Esc` recusa.
- Depois de confirmar: "Pareado" com ponto `ok` e uma frase que manda de volta ao óculos; fecha sozinho em 5 s.
- Recusar faz o óculos mostrar "O PC recusou a conexão" (`ConnectionStatus`).
