Screen Portal é um escritório de monitores virtuais no Meta Quest 3. As telas do PC são o produto; a UI do app existe para ser **encontrada quando preciso e esquecida no resto do dia**. Sessões duram horas: conforto, legibilidade e silêncio visual valem mais que efeito.

## Princípios

- **A tela é intocável.** O conteúdo do PC é uma compositor layer nítida; a UI só vive em volta dela (moldura, alças, rótulos, menus). Nunca desenhe por cima do vídeo, exceto o estado sem vídeo (`ScreenState`).
- **Liberdade.** Encaixes, grades e regras ajudam, nunca prendem. Toda guia `edit` mostra junto que `Shift` desliga o ímã.
- **Mouse primeiro.** Todo componente tem estado de hover para o cursor 3D e para o laser, e toda ação tem atalho mostrado com `Kbd`.
- **Opaco sobre o mundo.** O passthrough atrás é imprevisível: painéis usam `panel` opaco com contorno `edge`; linhas e rótulos soltos no mundo levam `halo`.
- **Ângulo, não metro.** Toda medida deste sistema é um ângulo de visão. O mesmo componente tem o mesmo tamanho aparente no pulso (0,3 m), no menu flutuante (0,7 m) e no HUD (1,1 m).

## Escala: graus, mockups e Unity

Os mockups deste sistema são desenhados a **16 px por grau**. Para levar para a Unity:

| ângulo | mockup | a 0,3 m (`dist-wrist`) | a 0,7 m (`dist-float`) | a 1,1 m (`dist-hud`) | a 1,3 m (`dist-screen`) |
|---|---|---|---|---|---|
| 0,5° | 8 px | 2,6 mm | 6,1 mm | 9,6 mm | 11,3 mm |
| 1° | 16 px | 5,2 mm | 12,2 mm | 19,2 mm | 22,7 mm |
| 2° | 32 px | 10,5 mm | 24,4 mm | 38,4 mm | 45,4 mm |
| 5° | 80 px | 26,2 mm | 61,2 mm | 96,2 mm | 113,7 mm |

Fórmula: `metros = tan(graus) × distância`. Para TextMeshPro (1 de `fontSize` ≈ 0,1117 m de altura "Hg"): `fontSize = tan(Hg°) × distância / 0,1117`. Construa os prefabs em graus (um componente escala o canvas pela distância até a cabeça) em vez de copiar milímetros.

## Conteúdo e voz

- Português do Brasil, **você** implícito, frases curtas no presente. Sem ponto final em botões; com reticências "…" (caractere único) em processos em andamento: "Procurando seu PC…", "Criando monitor…".
- Botão = verbo ou substantivo direto: "Recentrar", "Desfazer", "+ Monitor virtual". Submenus terminam com "▸"; voltar é "← Voltar".
- Diga o que aconteceu e o que fazer: "Não encontramos seu PC." + "Instale o Screen Portal no PC pelo link ou QR code.". Erros nunca culpam o usuário e nunca mostram código técnico cru; o motivo vai numa segunda linha em `caption`.
- Estados ligados/desligados ficam no controle (toggle), não no texto: "Grade" com toggle, em vez de "Grade: ligada".
- Ações que dependem de uma tela dizem **qual**: o cabeçalho do menu mostra "DISPLAY3 · 2560×1440" acima da aba Tela.
- Números com unidade e separador brasileiro: "60 %", "≈ 1300R", "1,3 m", "2560×1440" (× sem espaços).
- Sem emoji. Ícones só onde uma palavra não cabe ou para status (✓ ! ×), sempre com palavra ao lado.

## Cor

- Base: `panel` opaco com contorno `edge` em `stroke-hair`; controles em `panel-raised`. Texto em `ink`; secundário em `ink-muted`; `ink-faint` só para desabilitado, sobre `panel-sunken` (desabilitado nunca usa transparência).
- **`portal` é a única cor de interação**: hover, foco, seleção, arraste, cursor no mundo. Pressionado/ligado = fundo `portal` com texto `on-portal`. Selecionado/atual = fundo `portal-tint` + marca ✓.
- **`edit` é exclusivo do modo edição**: selo, guias de encaixe, parte ativa do gizmo. Se algo está amarelo, você está editando.
- Eixos do gizmo seguem a convenção da Unity: `axis-x` vermelho, `axis-y` verde, `axis-z` azul, cada um com forma própria.
- Status: `ok`, `warn`, `danger` sempre com ícone e palavra (`ok` e `danger` não se distinguem por luminância).
- `passthrough-dim` e `passthrough-bright` existem só para testar mockups: todo componente é revisado sobre os dois.

## Tipografia

- Família **Atkinson Hyperlegible Next** (`sans`), desenhada para baixa acuidade: formas distintas para I/l/1, O/0, acentuação completa. Para teclas e o código de pareamento, **Atkinson Hyperlegible Mono** (`mono`). Ambas são Google Fonts (OFL); gere o font asset SDF do TMP com o conjunto Latin-1 + "…" "▸" "←" "×" "≈" "✓" "↶" "↷" "⇄" "⇥".
- **Piso: `label`/`body`/`caption` = Hg 1,63° (altura de x ≈ 0,92°).** Nada fica menor. A hierarquia vem de peso (`label` 600, `body` 400) e cor (`caption` em `ink-muted`), e de subir para `heading`, `title`, `display`.
- Um tamanho por linha, no máximo dois pesos por painel. Sem itálico, sem caixa-alta corrida (borra no eye buffer).

## Espaço, tamanho e forma

- Alvos de laser/pinça: altura mínima `target-min` (4°); botões do menu em `target` (5°); folga `space-2` entre eles. Painéis com padding `space-6` e largura `panel-width`.
- Cantos: `radius-md` em botões e toasts, `radius-lg` em painéis, `radius-sm` em teclas e chips, `radius-pill` em toggles e no selo.
- Sem sombras, gradientes ou blur (material unlit, custo alto e borrão). Profundidade é distância real: o painel fica 1–2 cm à frente do que ele cobre, nunca colado.
- Linhas: contorno de painel `stroke-hair`; foco e seleção `stroke-focus`; tudo que é desenhado direto no mundo `stroke-world` com `halo` de `stroke-halo` dos dois lados.

## Estados de controle

| estado | fundo | contorno | texto |
|---|---|---|---|
| normal | `panel-raised` | `edge` | `ink` |
| hover (cursor ou laser) | `panel-hover` | `portal` em `stroke-focus` | `ink` |
| pressionado / ligado | `portal` | `portal` | `on-portal` |
| selecionado / atual | `portal-tint` | `portal` | `ink` + ✓ |
| desabilitado | `panel-sunken` | nenhum | `ink-faint` |
| aviso / erro | `panel-raised` | `warn` / `danger` | `ink` + ícone colorido |

## Movimento

- O app roda a 72 Hz: transições de 120–180 ms, ease-out, só em opacidade e escala (0,96 → 1). Nada pisca, nada pulsa em loop; processos em andamento usam reticências estáticas ou uma barra indeterminada lenta (1,2 s por ciclo).
- Painéis que seguem a cabeça (selo, toast, status) usam atraso suave (lazy follow) e só se movem quando saem de 20° do centro.
- Toasts ficam 2 s (3 s se tiverem ação) e somem em 180 ms.

## Onde cada coisa mora

| superfície | distância | ancoragem |
|---|---|---|
| Menu de pulso | `dist-wrist` | 12 cm acima da mão esquerda, virado para a cabeça |
| Menu flutuante | `dist-float` | na direção do cursor; fecha com Esc ou clique fora |
| Selo do modo edição, toasts | `dist-hud` | parte de baixo da visão, 15° abaixo do horizonte, lazy follow |
| Status de conexão e pareamento | `dist-hud` | centro da visão, à frente das telas (é bloqueante) |
| Moldura, alças, gizmo, rótulos de tela | junto da tela | tamanho constante em graus |
| Faixa de perfis (`ProfileSwitcher`) | `dist-hud` | logo acima do selo, enquanto `Ctrl+Alt` está segurado |
| Zona da mesa (`PassthroughZone`) | no plano da mesa | presa à âncora espacial da mesa, abaixo das telas |
| Introdução (`Onboarding`) | `dist-float` | centro da visão, à frente de onde a primeira tela vai nascer |
| Dicas (`CoachMark`) | junto do alvo | ao lado da tela, menu ou cursor que explicam |

## Mundo e zona da mesa

- O passthrough tem três níveis (Ajustes → Mundo): **Claro** (padrão, como hoje), **Escurecido** (`world-dim` por cima, opacidade ajustável) e **Escuro**. Telas e UI nunca mudam de cor entre os níveis.
- Fora do Claro, a **zona da mesa** mantém teclado, mouse e mesa visíveis, com borda suave. Ela é editada no modo edição com as mesmas alças do `ScreenFrame`.
- Se a âncora da mesa se perder, a zona some e o aviso oferece "Mundo claro": o usuário nunca fica sem ver a mesa.

## Perfis de layout

- Um perfil guarda telas, superfície, curvaturas, taxas e zona. Até 9, com `Ctrl+Alt+1…9`; o número é o atalho e aparece sempre num `Kbd`.
- Aplicar é desfazível; salvar por cima nunca é automático. Nomes são digitados no teclado físico.

## Host no PC

- O app do PC usa as mesmas cores e a mesma voz, mas em **pixels de tela**, não graus: estilos `host-*` (corpo 14 px), janela em `host-surface`, contornos de 1 px, botão primário em `portal`.
- O host é discreto: vive na bandeja (`HostTray`); a janela (`HostWindow`) mostra estado e ajustes; criar e posicionar telas é sempre no óculos.
- O pareamento usa o mesmo código, no mesmo formato, nos dois lados (`ConnectionStatus` no óculos, `HostPairing` no PC).

## Iconografia

O projeto ainda não tem um conjunto de ícones. Até lá, use só os glifos tipográficos já presentes na interface (▸ ← ● ✓ ! × ≈) na fonte `sans`, e para os ícones de cursor do modo edição (mover, girar, escalar) bitmaps próprios de 64 px com `halo`. Ao adotar um conjunto, prefira traço de 2 px em grade de 24, cantos arredondados, um peso só; nunca ícone sem palavra no menu.

## Logo

Ainda não há marca. Escreva "Screen Portal" em `sans` 600, `ink`. Não desenhe símbolo até existir um oficial.
