# ScreenState
O conteúdo da área da tela enquanto não há vídeo do PC: criando, conectando, falhou e PC desconectado.

**O consumidor fornece:** o estado, a mensagem principal e, opcionalmente, uma segunda linha com detalhe (até 60 caracteres).

**Estados e textos**
| estado | ícone | principal (`body`) | detalhe (`caption`) |
|---|---|---|---|
| criando | … em `portal` | "Criando monitor…" | resolução |
| abrindo vídeo | … em `portal` | "Conectando vídeo…" | — |
| falhou | × em `danger` | "Falha ao criar o monitor" | o motivo, em linguagem do usuário |
| PC desconectado | — | "PC desconectado" | "Reconectando a <PC>…" |

**Regras**
- Fundo `screen-off`, centralizado, no tamanho real da tela; o texto escala em graus com a distância (nunca menor que `body`).
- Nada de "Falha:\n" + código cru: separe a frase da causa.
- Uma tela em erro mantém a moldura e o rótulo para poder ser removida ou recriada.
