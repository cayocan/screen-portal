# Screen Portal — inventário de UI/UX (para o design do app VR)

> Documento de entrada para o design de UI/UX do app no Meta Quest 3. Lista **tudo que o usuário vê e toca hoje**
> (estado em 02/10/2026, fim da Fase 3), com textos reais, estados, medidas e cores atuais, mais o que vem nas próximas
> fases. As medidas e cores atuais são de engenharia (placeholder): o objetivo do design é substituí-las.
> Idioma da interface: **português do Brasil**.

---

## 1. O produto em uma página

**Screen Portal** transforma o Quest 3 num **escritório de monitores virtuais** do PC com Windows. Um app no PC (o
"host") cria monitores virtuais de verdade no Windows e manda o vídeo de cada um para o óculos; no óculos, cada monitor
vira uma **tela flutuante** que o usuário posiciona, curva e redimensiona à vontade, com o mundo real visível em volta
(passthrough).

- **Público:** quem trabalha no PC várias horas por dia (dev, design, finanças, escrita) e quer mais telas, em qualquer
  lugar, sem carregar monitores.
- **Três pilares do MVP:**
  - **A. Telas nítidas e livres:** vários monitores virtuais, nitidez de texto, posição e tamanho livres, telas curvas,
    taxa de quadros por tela, som do PC no óculos e microfone do óculos no PC.
  - **B. Mesa real visível** (Fase 4): uma "janela" de passthrough para ver teclado, mouse e mesa.
  - **C. Mouse e teclado em primeiro lugar:** tudo se faz sem tirar a mão do mouse; mãos e controles são alternativa.
- **Princípio "Liberdade":** encaixes, grades e regras de layout **ajudam, nunca prendem**. Todo ímã desliga com `Shift`;
  toda tela pode sair da superfície de layout.
- **Sessão longa:** o app é usado por horas. Conforto visual, baixo ruído visual e legibilidade valem mais que efeito.

---

## 2. Restrições técnicas que o design precisa respeitar

| Tema | Como é hoje | Implicação para o design |
|---|---|---|
| **Telas do PC** | Cada tela é uma *compositor layer* (vídeo amostrado pelo Quest na resolução do painel) — nitidez máxima | O conteúdo das telas é intocável; o design só atua **em volta** (moldura, alças, rótulos, menus) |
| **UI do app** | Desenhada no *eye buffer* da Unity (render scale 1,0), **por cima** das telas | UI no eye buffer é menos nítida que as telas; textos pequenos borram. Preferir texto grande, alto contraste, pouco detalhe fino |
| **Cursor do PC** | É uma *compositor layer* própria (no eye buffer ficava borrado) | Ícones de cursor custom (mover, girar…) precisam ser bitmaps nítidos, 32–64 px |
| **Texto** | TextMeshPro SDF, fonte padrão; 1 unidade de `fontSize` ≈ 0,1117 m de altura de "Hg" | Definir tamanho mínimo em **graus** (sugestão: ≥ 0,9° de altura de x a 0,5–1,3 m) |
| **Frame rate** | App a 72 Hz | Animações curtas e suaves; nada piscando |
| **Distâncias** | Telas a ~1,3 m (0,4–5 m); menu de pulso a ~0,3 m da mão; menu pelo mouse a 0,7 m da cabeça | Layout de UI pensado por distância; ver §9 |
| **Passthrough** | O mundo real aparece em volta das telas | UI precisa funcionar sobre fundos imprevisíveis (contorno/halo/painel de fundo) |
| **Cores** | Material unlit chapado, sem sombras | Linguagem visual plana é natural; gradientes/blur custam caro |
| **Língua** | pt-BR, acentos e "…" | Fonte com acentuação completa |

---

## 3. Modos de entrada

| Entrada | Para que serve hoje | Detalhes |
|---|---|---|
| **Mouse + teclado (primário)** | Usar o Windows nas telas; manipular telas no **modo edição**; abrir o menu (`Ctrl+Alt+M`); atalhos | O cursor do Windows é desenhado pelo óculos na posição exata. Fora das telas vira um **cursor 3D no mundo** que anda em volta da cabeça |
| **Controles (Touch)** | Laser do controle direito: apontar e clicar no menu; no modo edição, pegar telas | Botão **≡ (menu) do controle esquerdo** segurado abre o menu de pulso; **X/Y** do controle esquerdo = desfazer/refazer |
| **Mãos** | Pinça = clicar/pegar; punho = soltar a tela da superfície | **Palma esquerda virada para o rosto** abre o menu de pulso. **Gesto de tchau** liga/desliga as mãos (laser, interação e modelo) |
| **Lasers** | Somem após **3 s** com o controle parado (movimento < 1 cm e < 2°) | — |
| **Mão/controle visíveis** | Modelos de mão e de controle aparecem (podem ser desligados pelo tchau) | — |

**Regra do modo edição:** fora dele, controle e mão **só usam o menu** (não mexem em telas). No modo edição, mouse,
controle e mão manipulam as telas com a mesma lógica e o mesmo desfazer.

---

## 4. Inventário de superfícies de UI

### 4.1 Tela (monitor) — o objeto central

**Tipos de tela**

| Tipo | Origem | Rótulo |
|---|---|---|
| Monitor virtual | Criado pelo app no Windows com resolução escolhida | "" (o Windows chama de DISPLAYn) |
| Espelho | Monitor físico do PC espelhado (o principal é espelhado sozinho na 1ª conexão) | Nome do monitor (ex.: "DISPLAY1") |
| Tela de teste | Padrão de teste, só em desenvolvimento | "Teste 1", "Teste 2"… |
| Janela espelhada | (desligada por enquanto) uma janela do Windows vira tela | Título da janela |

**Estados da tela** (texto no centro da tela, sobre fundo escuro, enquanto não há vídeo)

| Estado | Texto atual |
|---|---|
| Sem PC | "PC desconectado" |
| Criando o monitor no PC | "Criando monitor…" |
| Abrindo o vídeo | "Conectando vídeo…" |
| Falhou | "Falha:\n" + motivo (até 60 caracteres) |
| Ao vivo | vídeo do PC (sem texto) |

**Geometria e propriedades editáveis**
- Largura física 0,2–6 m (padrão: 2560 px ≈ 1 m); altura pela proporção. **Redimensionar é zoom: a resolução nunca muda.**
- **Plana ou curva:** curvatura 0–100 %; presets **Plana · Suave · Média · Envolvente**; padrão de "curvar" = 60 %; arco até 120°.
- **Posição:** na **superfície de layout** (Cilindro ou Esfera em volta do usuário, ver 4.6) ou **solta** (pose livre no espaço).
- **Taxa de quadros por tela:** **Auto** (sobe quando você olha para ela), **Vídeo**, **Fixo 30**, **Fixo 15**, **Eco**.
- **Recriar** com outra resolução (ação explícita; desfazer volta à anterior).

**Moldura e alças** (aparecem com o ponteiro perto, no modo edição)
- Moldura fina **3 cm** fora da tela; alças de canto **6 cm** (redimensionar proporcional); **alça de curvatura** no meio
  da borda de cima (10 cm de largura): arrastar para longe curva, para perto achata.
- Cores atuais: hover **branco**; arrastando **ciano** (0.25, 0.8, 1); guias de encaixe **amarelo** (1, 0.8, 0.2); linha 4 mm.
- Ao curvar: rótulo acima da tela, ex.: "Média · 60 % · ≈ 1300R".
- **Guias de encaixe:** linha vertical (azimute) e arco (elevação) quando a tela encaixa no alinhamento de outra.

**Gizmo do modo edição** (na última tela clicada; estilo Unity)
| Parte | Forma atual | Cor | Ação | Roda segurando |
|---|---|---|---|---|
| Mover X | seta para a direita | vermelho | desliza na horizontal (na superfície) | 5 mm / `Shift` 5 cm |
| Mover Y | seta para cima | verde | desliza na vertical | 5 mm / 5 cm |
| Perto/longe | barra vertical **abaixo** do centro + linha curta apontando para o usuário | azul | arrastar para cima aproxima, para baixo afasta | 5 mm / 5 cm |
| Girar X/Y/Z | três anéis (raio 1,3× as setas) | vermelho/verde/azul | gira em passos de 5° (`Shift` livre); **girar solta a tela da superfície** | 1° / 15° |
| Escalar | quadrado no centro | branco | arrastar para cima/direita cresce | 1 % / 10 % |
| Parte sob o ponteiro / em uso | — | **amarelo** | — | — |
Tamanho: setas ≈ 12 % da distância até a tela (constante na visão).

### 4.2 Menu de pulso (menu principal do app)

**Como abre:** palma esquerda virada para o rosto, ou segurar **≡** no controle esquerdo. Aparece **12 cm acima da mão**,
virado para a cabeça. **Pelo mouse:** `Ctrl+Alt+M` abre o **mesmo menu flutuando** na direção do cursor a 0,7 m; fecha
com `Esc`, `Ctrl+Alt+M` ou clique fora.

**Forma atual:** painel escuro (0.06, 0.06, 0.08); **grade de 2 colunas** de botões 7,5 × 3 cm com 6 mm de folga;
linha de status de 2,5 cm no topo; texto branco ~6,4 mm. Botão normal (0.18, 0.2, 0.24); **hover azul** (0.25, 0.55, 0.9);
desabilitado (0.1, 0.1, 0.12). Até **17 itens** numa página. Acionado pelo laser/pinça ou clique do mouse.

**Página principal** (ordem atual)
1. "Modo edição: ligado / desligado"
2. "Desfazer"  3. "Refazer"
4. "Cilindro → Esfera" / "Esfera → Cilindro"
5. "Recentrar"  6. "Alinhar à postura"
7. "Grade: ligada / desligada"
8. "+ Monitor virtual" ▸  9. "+ Espelhar monitor" ▸
10. "Voltar à superfície" (só com tela solta em foco)
11. "Remover tela"
12. "Curvatura ▸"  13. "Recriar ▸" (só monitor virtual)  14. "Taxa de quadros ▸"  15. "Áudio ▸"
16. "Arranjo automático: ligado / desligado"
17. "+ Tela de teste" (só desenvolvimento)
Itens indisponíveis ficam desabilitados (ex.: "Desfazer" sem histórico, "+ Monitor virtual" sem PC).

**Linha de status** (topo do painel): resumo do momento, ex.: aviso "Mais de 4 telas:\npode haver quedas e travamentos".

**Subpáginas** (todas terminam com "← Voltar")
| Página | Conteúdo |
|---|---|
| **Proporções** (de "+ Monitor virtual" ou "Recriar") | 1º botão alterna **paisagem/retrato**; depois as proporções **16:9 · 16:10 · 21:9 · 32:9 · 1:1 · 4:3**, cada botão com um **retângulo desenhado na proporção** |
| **Resoluções** | 16:9 → 1920×1080, 2560×1440, 3840×2160 · 16:10 → 1920×1200, 2560×1600, 3840×2400 · 21:9 → 2560×1080, 3440×1440 · 32:9 → 3840×1080 · 1:1 → 1440×1440, 2160×2160 · 4:3 → 1600×1200, 2048×1536. No modo **Recriar**: título "Recriar: <tela>" e a atual marcada " · atual" (desabilitada) |
| **Espelhos** | Um botão por monitor físico: "<nome> · <largura>×<altura>" |
| **Curvatura** | Presets com o atual marcado "● "; status com o rótulo da tela em foco, ou "Curvatura: sem tela em foco" |
| **Taxa de quadros** | Auto / Vídeo / Fixo 30 / Fixo 15 / Eco, atual com "● "; status "<modo> · <fps> fps", "Taxa de quadros: sem tela em foco" ou o aviso "Essa configuração pode causar travadas" |
| **Áudio** | "Som do PC: ligado/desligado" · "Microfone: ligado/mudo"; status ex.: "Som do PC: ligado · Microfone: ligado", "Som também nas caixas do PC", "Microfone indisponível (Steam)" |

### 4.3 Status de conexão (painel de texto central)
Aparece quando não há conexão/vídeo; "Conectado a …" some após 2 s.

| Estado | Texto atual |
|---|---|
| Procurando | "Procurando seu PC…" |
| Host não encontrado | "Não encontramos seu PC.\n" + dica de instalar o host |
| Conectando | "Conectando a <PC>…" |
| Pareamento | "Confirme no PC:\ncódigo 123 456" (o PC mostra uma janela pedindo para confirmar o mesmo código) |
| Conectado | "Conectado a <PC>" |
| Instável | "Conexão instável…" |
| Reconectando | "Reconectando a <PC>…" |
| Recusado | "Conexão recusada no PC." |
| Versão | "Versões incompatíveis.\nAtualize o app ou o host." |
| Host fechou | "<PC> encerrou o Screen Portal." |

### 4.4 Cursor
| Situação | Visual atual |
|---|---|
| Sobre uma tela | O cursor do Windows (seta, I-beam, mão, redimensionar, espera…) na forma real, no tamanho que teria na tela; I-beam ganha contorno branco |
| Fora das telas ("no mundo") | A mesma forma, de frente para o usuário, no tamanho angular que tinha na tela; anda numa esfera em volta da cabeça e **entra** na tela que tocar, no ponto do toque |
| Num monitor do PC fora do óculos | Invisível no óculos |
| Perdido | `Ctrl+Alt+Home` traz para a frente dos olhos |
| Oportunidade | ícones próprios no modo edição (mover/girar/escalar), halo para destacar no passthrough |

### 4.5 Modo edição
- Liga por `Ctrl+Alt+E`, menu, ou **segurando `Ctrl+Alt`** (temporário); `Esc` sai (com arraste: cancela o arraste).
- **Selo** amarelo na parte de baixo da visão, a 1,1 m, seguindo a cabeça com atraso: "Modo edição · Esc para sair"
  (temporário: "Modo edição (Ctrl+Alt)").
- Com o mouse: clicar e arrastar no conteúdo move; botão direito arrastando solta da superfície; roda aproxima/afasta;
  `Shift`+roda redimensiona; gizmo na tela selecionada (4.1).

### 4.6 Superfície de layout (invisível, mas define o arranjo)
- **Cilindro** (padrão) ou **Esfera** centrada na cabeça (centro a 1,2 m de altura). As telas "moram" nela:
  azimute/elevação/distância.
- **Encaixe magnético** nas bordas/alinhamento das vizinhas (guias amarelas); **grade angular** opcional (passo 1–30°).
- **Recentrar** (traz tudo para a frente), **Alinhar à postura** (inclina para quem está reclinado).
- **Arranjo automático:** o Windows organiza os monitores como eles estão no óculos (o mouse atravessa como você vê).
- Nova tela nasce **à frente da visão**, no primeiro espaço livre.

### 4.7 Mãos, controles e lasers
- Laser branco (0.9, 0.9, 0.9), 3 m; some com o controle parado; modelos de mão/controle visíveis.
- Gesto de **tchau** (3 inversões em 1,5 s, ≥ 5 cm) liga/desliga as mãos. Não há feedback visual próprio hoje.

### 4.8 PC (host) — sem interface gráfica hoje
- Roda em console (log de texto). Pareamento: caixa de diálogo do Windows com o código.
- Configuração por arquivos: `hotkeys.json` (atalhos), `scales.json` (escala por monitor), `paired.json`.
- **Oportunidade:** ícone na bandeja + janela simples (status, pareamento, atalhos) — previsto no onboarding (Fase 5).

---

## 5. Fluxos principais (jornadas)

1. **Primeira vez:** abre o app → "Procurando seu PC…" → (sem host: "Não encontramos seu PC" + instalar) → "Confirme no PC:
   código …" → usuário confirma no PC → monitor principal aparece espelhado à frente.
2. **Retomar o dia:** abre o app → a **última sessão volta** (telas no mesmo lugar, recentrada na cabeça) → reconecta sozinho.
3. **Criar um monitor:** menu → "+ Monitor virtual" → orientação/proporção → resolução → a tela nasce à frente.
4. **Arrumar o espaço com o mouse:** `Ctrl+Alt+E` → arrastar telas, gizmo, roda → `Esc`.
5. **Arrumar com as mãos/controle:** modo edição pelo menu → pinça/gatilho na tela ou nas alças.
6. **Curvar uma tela:** menu → "Curvatura ▸" → preset; ou alça de curvatura; ou `Ctrl+Alt+C` / `[` / `]`.
7. **Trocar a resolução:** menu → "Recriar ▸" → proporção → resolução (desfazer volta).
8. **Ajustar desempenho:** menu → "Taxa de quadros ▸" (aviso se exagerar no total).
9. **Som e microfone:** menu → "Áudio ▸".
10. **Perdi o cursor:** `Ctrl+Alt+Home`.
11. **Conexão caiu:** telas mostram "PC desconectado"; status "Reconectando…"; tudo volta sozinho.

---

## 6. Atalhos globais (padrão; reconfiguráveis)

| Atalho | Ação |
|---|---|
| `Ctrl+Alt+←/→/↑/↓` | Mover a tela sob o cursor 5° |
| `Ctrl+Alt+Shift+↑/↓` | Aumentar/diminuir 10 % |
| `Ctrl+Alt+PgUp/PgDn` | Aproximar/afastar 10 cm |
| `Ctrl+Alt+C` · `[` · `]` | Plana ↔ curva · menos/mais curvatura |
| `Ctrl+Alt+F` | Taxa de quadros: Auto → Vídeo → Eco → Fixo 30 |
| `Ctrl+Alt+Z` / `Y` | Desfazer / refazer |
| `Ctrl+Alt+R` · `L` · `T` · `G` | Recentrar · Cilindro↔Esfera · Alinhar à postura · Grade |
| `Ctrl+Alt+E` · `Ctrl+Alt` segurado · `Esc` | Modo edição · temporário · sair |
| `Ctrl+Alt+M` | Menu do app onde está o cursor |
| `Ctrl+Alt+Home` | Trazer o cursor perdido |

---

## 7. Problemas de UX conhecidos e oportunidades

- **Menu é uma lista longa** (17 itens em 2 colunas): falta hierarquia (ações da tela × ações do espaço × configurações).
- **Itens contextuais misturados** com globais ("Remover tela", "Curvatura" dependem da tela em foco, mas não dizem qual).
- **Foco da tela é invisível:** não há indicação clara de qual tela está selecionada fora do modo edição.
- **Sem feedback** para: tchau, desfazer/refazer, atalhos (nenhum toast/HUD), encaixe magnético ligado/desligado.
- **Textos de estado** aparecem crus no centro da tela (sem ícone, sem hierarquia).
- **Gizmo** usa linhas finas sem pontas de seta; anéis de perfil quase invisíveis de frente.
- **Moldura/alças** só aparecem no hover; difícil descobrir que a tela é editável.
- **Status de conexão** compete com as telas; precisa de um lugar fixo e discreto.
- Sem tela de **boas-vindas/onboarding** e sem **ajuda de atalhos** (cheat sheet).
- O host no PC não tem interface (só console).

---

## 8. O que vem depois (para o design já prever)

| Fase | UI nova |
|---|---|
| **3b — Perfis de layout** | Salvar/aplicar perfis nomeados ("Dev", "Reunião"), troca rápida `Ctrl+Alt+1…9`, perfis ancorados na mesa |
| **4 — Zona de passthrough** | Criar uma "janela" para o mundo real sobre a mesa (pelo mouse: retângulo + arrastar/roda; ou 2 cantos com a mão); editar bordas, cantos, altura, formato, borda suave, opacidade; aviso de âncora perdida; revelar ao olhar para baixo |
| **5 — Polimento** | **Onboarding** (instalar o host: link + QR code, "Procurando seu PC…"), instalador do host com bandeja, indicadores de desempenho/rede, avisos de travada |
| **Depois (v1.1+)** | Modo foco, modo chamada, "olhar e pular" o cursor, vários PCs |
| **Desligado (pode voltar)** | Janela espelhada: `Ctrl+Alt+D` destaca a janela ativa como tela própria |

---

## 9. Pedido ao design

1. **Linguagem visual** para UI espacial sobre passthrough: paleta, tipografia (tamanhos em graus), cantos, profundidade,
   estados (normal, hover, pressionado, desabilitado, selecionado, aviso, erro).
2. **Menu do app** redesenhado: hierarquia, agrupamento (Espaço · Tela selecionada · Novo · Configurações), versão de
   pulso e versão flutuante pelo mouse, navegação de subpáginas, seletor de proporção/resolução visual.
3. **Tela**: moldura, alças, seleção/foco, rótulo, estados sem vídeo (criando, conectando, falhou, desconectado), tela curva.
4. **Modo edição**: selo, gizmo (setas com ponta, anéis legíveis de frente, alça de profundidade, escala), guias de encaixe,
   cursor 3D e ícones de cursor de manipulação.
5. **Status de conexão e pareamento** (com o código), **toasts/HUD** de feedback (desfazer, atalho, tchau, aviso de desempenho).
6. **Onboarding** e **cheat sheet de atalhos**.
7. **Fluxos futuros:** perfis (3b) e zona de passthrough (4).
8. Opcional: **janela do host no PC** (bandeja + painel).

Entregáveis úteis: wireframes por superfície, especificação de componentes com medidas em metros/graus a 0,3 m (pulso),
0,7 m (menu flutuante) e 1,3 m (telas), estados, e os textos finais em pt-BR.
