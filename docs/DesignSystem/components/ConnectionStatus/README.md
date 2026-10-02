# ConnectionStatus
O painel que mostra a conexão com o PC: procurar, parear (com o código), conectar, instável, reconectando e erros.

**O consumidor fornece:** o estado, o nome do PC, o código de pareamento quando houver, e as ações disponíveis.

**Duas formas**
- **Painel bloqueante** (procurando, pareamento, não encontrado, recusado, versões incompatíveis, host fechou): centro da visão a `dist-hud`, à frente das telas, `title` + `body` + ações.
- **Pílula discreta** (conectado, instável, reconectando): canto superior da visão, uma linha `label` com ícone; "Conectado a <PC>" some após 2 s, os outros ficam até mudar.

**Textos**
| estado | título | detalhe |
|---|---|---|
| procurando | "Procurando seu PC…" | "O PC e o óculos precisam estar na mesma rede Wi-Fi." + barra indeterminada |
| não encontrado | "Não encontramos seu PC" | "Instale o Screen Portal no PC e deixe-o aberto." · Como instalar ▸ · Tentar de novo |
| pareamento | "Confirme no PC" | "O mesmo código aparece numa janela no <PC>." + código em `code` |
| recusado | "O PC recusou a conexão" | Tentar de novo |
| versões | "Versões diferentes" | "Atualize o app ou o Screen Portal no PC." |
| host fechou | "<PC> fechou o Screen Portal" | "Abra de novo no PC para continuar." |

**Regras**
- Ícone circular com contorno no tom (`portal` em andamento, `ok`, `warn`, `danger`) + palavra; nunca só cor.
- O código de pareamento usa `code` (`mono` 3,5°) em grupos de 3, sobre `panel-sunken`.
