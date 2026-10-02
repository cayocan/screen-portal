# Onboarding
O fluxo da primeira vez em três passos — Instalar, Parear, Primeira tela — que liga as telas de status já existentes num caminho só.

**O consumidor fornece:** o passo atual, o link do instalador (o QR é gerado a partir dele em tempo real) e se o host já foi encontrado.

**Fluxo**
1. **Boas-vindas** (`display` + `body`): a promessa em uma frase, os três passos e "Começar ▸". Se o host responder enquanto isso, pula direto para Parear.
2. **Instalar**: QR + link do instalador. A busca pelo PC continua no rodapé; assim que o host aparece, avança sozinho.
3. **Parear**: o painel "Confirme no PC" do `ConnectionStatus`, com o indicador de passos acima.
4. **Primeira tela**: o monitor principal espelhado aparece à frente; o painel mostra só os 3 atalhos essenciais e "Ver atalhos" (folha do `Kbd`).

**Regras**
- O indicador de passos fica no topo de todos os painéis do fluxo: feito com ✓ em `ok`, atual em `portal`, futuros em `edge`.
- Depois do fluxo, `CoachMark` dá no máximo 3 dicas, cada uma no momento em que é útil.
- Ajustes → "Rever introdução" reabre o fluxo.
- **O QR da prévia é ilustrativo**, não um código válido, e o link ainda não existe: troque o marcador ‹link do instalador› pelo endereço real.
