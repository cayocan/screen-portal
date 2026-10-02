# Menu
O menu principal do app, o mesmo painel no pulso (`dist-wrist`) e flutuando pelo mouse (`dist-float`, `Ctrl+Alt+M`), organizado em quatro abas no lugar da lista de 17 itens.

**Abas e conteúdo**
| aba | itens |
|---|---|
| Espaço | Modo edição (toggle), Grade (toggle), Desfazer, Refazer, Recentrar, Alinhar à postura, Perfil ▸ (`Profiles`), Superfície (Cilindro/Esfera ▸), Zona da mesa ▸ (`ZoneSettings`), Arranjo automático (toggle) |
| Tela | cabeçalho de contexto com a tela em foco; Curvatura ▸, Taxa de quadros ▸, Recriar ▸ (só virtual), Voltar à superfície (só tela solta), Remover tela |
| Novo | + Monitor virtual ▸, + Espelhar monitor ▸, + Zona da mesa (só se não houver uma), + Tela de teste (só desenvolvimento) |
| Ajustes | Áudio ▸ (Som do PC, Microfone), Mundo ▸ (Claro · Escurecido · Escuro), Atalhos (folha de atalhos), Rever introdução (`Onboarding`) |

**O consumidor fornece:** a aba inicial, a tela em foco (nome, resolução, tipo) ou nenhuma, os estados de cada item e a linha de status.

**Regras**
- Painel `panel`, largura `panel-width`, padding `space-6`, grade de 2 colunas com folga `space-2`. Itens com valor ou submenu ocupam a linha inteira.
- Abre na aba **Tela** se uma tela recebeu clique nos últimos 30 s; senão em **Espaço**.
- Sem tela em foco, a aba Tela mostra no cabeçalho "Nenhuma tela selecionada" e os itens desabilitados, em vez de sumir.
- Subpáginas trocam o conteúdo abaixo das abas, com título `title` e "← Voltar" como primeiro botão (não o último): o laser sempre encontra a saída no mesmo lugar.
- A linha de status fica no rodapé, em `caption`; com aviso, ícone "!" em `warn`.
- Pulso: 12 cm acima da mão esquerda, virado para a cabeça. Flutuante: na direção do cursor; fecha com `Esc`, `Ctrl+Alt+M` ou clique fora.

**Não faça**
- Misturar ações da tela com ações do espaço na mesma aba; ações de tela sem dizer qual tela.
