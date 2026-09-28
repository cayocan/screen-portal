# Fase 0 — legibilidade do texto

Critério do brief §4.2: **fonte 12 legível na distância padrão sugerida**, lendo por horas.
Setup: no PC, abrir `tools/text-test.html` no monitor primário (2560×1080), zoom do navegador 100 %,
tela cheia (F11). O Claude reinicia o app no headset com cada configuração; você só olha e responde.

## Por que variar tamanho e distância
O Quest 3 resolve ~25 pixels por grau (PPD). Uma tela de 2560 px que ocupa 57° tem ~45 px/grau:
o texto fica menor que o display consegue mostrar e borra por sub-amostragem. Ocupando ~100°, cada
pixel do Windows vira ~1 pixel do display. O teste procura o ponto de equilíbrio entre nitidez e
conforto (virar a cabeça).

## Configurações
| ID | Largura × distância | Ângulo horiz. | px/grau | Filtros da layer | Bitrate |
|---|---|---|---|---|---|
| T1 | 140 cm × 130 cm | 57° | 45 | bicúbico | 20 Mbps |
| T2 | 200 cm × 120 cm | 80° | 32 | bicúbico | 20 Mbps |
| T3 | 250 cm × 110 cm | 97° | 26 | bicúbico | 20 Mbps |
| T4 | melhor de T1–T3 | | | bicúbico + sharpen | 20 Mbps |
| T5 | melhor de T1–T3 | | | bicúbico + supersample | 20 Mbps |
| T6 | melhor de T1–T5 | | | melhor filtro | **40 Mbps** |

## O que responder em cada configuração (1 linha basta)
1. **Menor linha legível sem esforço** no bloco A (texto) e no bloco B (código), em pt.
2. `Il1|` e `O0o` se distinguem em 12 pt? (sim / com esforço / não)
3. As linhas de 1 px (bloco D) **tremulam** ao mexer a cabeça devagar? (nada / pouco / muito)
4. Blocos, borrão ou "sujeira" ao redor do texto quando o relógio muda? (sim / não)
5. Conforto: dá para ler a borda da tela sem virar a cabeça? (sim / vira pouco / vira muito)

Exemplo: `T2: A=10, B=11, Il1 sim, tremula pouco, sem blocos, vira pouco`.

## Resultados
| ID | A (pt) | B (pt) | Il1/O0 12pt | Tremulação | Artefatos | Conforto | Notas |
|---|---|---|---|---|---|---|---|
| T1 | | | | | | | |
| T2 | | | | | | | |
| T3 | | | | | | | |
| T4 | | | | | | | |
| T5 | | | | | | | |
| T6 | | | | | | | |

## Conclusão
_Distância e tamanho padrão sugeridos para a criação de monitor; filtros padrão da layer; bitrate mínimo para texto._
