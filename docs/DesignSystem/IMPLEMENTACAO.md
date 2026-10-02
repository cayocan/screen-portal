# Implementar o design system do Screen Portal — guia para o Claude Code

Este guia é para quem (pessoa ou agente) vai levar o design system para o código: o app Unity no Quest 3 e o host no PC.
Leia nesta ordem: este arquivo → `README.md` (regras visuais e de texto) → `tokens.json` → o `README.md` do componente que
você for implementar → a imagem dele em `previews/`.

## O que é cada arquivo

| arquivo | uso |
|---|---|
| `README.md` | Regras: escala em graus, voz e textos, cores, tipografia, estados, movimento, onde cada coisa fica. É a fonte de verdade. |
| `tokens.json` | Valores exatos. `color.tokens`, `type.groups`, `spacing`, `size`, `radius`, `stroke`, `distance`, `opacity`. |
| `components/<Nome>/README.md` | O que o componente recebe, estados, textos finais em pt-BR e o que não fazer. |
| `previews/<Nome>.png` | Como ele deve ficar (mockup a 16 px por grau). |
| `components/<Nome>/preview.html`, `components/bundle.css` | Mockups em HTML. Referência de medidas e estados; **não** é código a portar. |

## Regras que não podem quebrar

1. **Tudo em graus.** Os mockups estão a 16 px = 1°. Nunca copie milímetros fixos: tamanho no mundo = `tan(graus) × distância`.
   TextMeshPro: `fontSize = tan(Hg°) × distância / 0,1117` (Hg de cada estilo está no `usage` dele no `tokens.json`).
2. **Nada abaixo do piso de texto** (`label`/`body`/`caption`, Hg 1,63°).
3. **Painéis opacos** (`panel`), nunca transparentes; desabilitado = `panel-sunken` + `ink-faint`, sem alpha.
4. **`portal` só para interação** (hover, foco, seleção, arraste). **`edit` só no modo edição.**
5. **Linhas no mundo levam `halo`** (contorno escuro) por baixo.
6. **Nada desenhado por cima do vídeo das telas** (compositor layers), exceto o `ScreenState` sem vídeo.
7. Textos exatamente como nos READMEs (pt-BR, "…" como um caractere só).

## Passo 1 — fundação (fazer primeiro, uma vez)

- `Assets/ScreenPortal/UI/Design/DesignTokens.cs` (ou um `ScriptableObject` `DesignTokens.asset`): gerado a partir do
  `tokens.json` — cores como `Color` (converter de hex/rgba), espaçamentos/raios/traços em **graus** (`px / 16`),
  distâncias em metros. De preferência um script de Editor (`Tools/Screen Portal/Importar tokens`) que lê o JSON e
  regrava o asset, para o design poder mudar sem reescrever código.
- `AngularScaler` (MonoBehaviour): escala um canvas World Space para que 1 unidade de layout = 1° na distância atual até
  a câmera (recalcula quando a distância muda: pulso 0,3 m, flutuante 0,7 m, HUD 1,1 m, telas 1,3 m).
- Fontes: baixar Atkinson Hyperlegible Next e Atkinson Hyperlegible Mono (Google Fonts, OFL) e gerar os Font Assets SDF
  do TMP com Latin-1 + `… ▸ ← × ≈ ✓ ↶ ↷ ⇄ ⇥ ●`. Criar os TMP Style Sheets com os nomes dos estilos (`display`, `title`,
  `heading`, `label`, `body`, `caption`, `kbd`, `code`).
- Sprites 9-slice de cantos arredondados para `radius-sm/md/lg/pill` e para o contorno (`stroke-hair`, `stroke-focus`).
- `LazyFollow` (para HUD): só se move quando o alvo sai de 20° do centro; transições de 120–180 ms, ease-out.

## Passo 2 — componentes base (reusados por todo o resto)

`MenuButton` (com os 6 estados + toggle + valor/submenu) → `Kbd` → `Toast` (fila de até 2) → `EditBadge`.

## Passo 3 — superfícies existentes, redesenhadas

`Menu` (4 abas, cabeçalho de contexto, subpáginas com "← Voltar" no topo) → `AspectPicker` → `ScreenFrame` (alças em graus,
seleção visível fora do modo edição) → `ScreenState` → `Gizmo` → `ConnectionStatus`. Substituir os textos atuais pelos dos
READMEs. Cada item do menu atual tem lugar definido em `components/Menu/README.md`.

## Passo 4 — fases novas

- **3b Perfis:** `Profiles`, `ProfileSwitcher` (+ atalhos `Ctrl+Alt+1…9`).
- **4 Zona da mesa:** `PassthroughZone`, `ZoneSettings`, nível de Mundo (Claro/Escurecido/Escuro) com `world-dim`.
- **5 Introdução:** `Onboarding`, `CoachMark`.
- **Host no PC:** `HostTray`, `HostWindow`, `HostPairing` — em pixels de desktop com os estilos `host-*`, na stack que o
  host já usa. Ler/gravar `hotkeys.json`, `scales.json` e `paired.json` pela janela.

## Pendências de produto (não inventar — perguntar)

- Link real do instalador do host (o QR do mockup é ilustrativo).
- Logo / ícone definitivo (o da bandeja é provisório).
- Atalho `Ctrl+Alt+P` (zona) e "Pausar o vídeo" no host são propostas: confirmar antes de implementar.
- Tema claro do host não está definido.

## Como verificar cada componente

- Tirar screenshot no editor (ou no Quest) sobre fundo cinza médio e claro (`passthrough-dim`/`passthrough-bright`) e
  comparar com `previews/<Nome>.png`.
- Conferir no Quest a 0,3 m / 0,7 m / 1,3 m que o texto continua com o mesmo tamanho aparente.
- Conferir que todos os estados do README do componente existem.
