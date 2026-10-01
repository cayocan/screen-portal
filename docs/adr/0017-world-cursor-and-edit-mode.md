# ADR 0017 — Cursor desenhado no headset que atravessa o mundo; modo edição global

- **Status:** Aceito (Fase 3B, 01/10/2026, aprovado pelo Cayo)
- **Spec:** docs/specs/2026-10-01-fase3b-cursor-edicao-design.md

## Contexto
O brief (§10.6–10.7) previa um roteador que **teleporta** o cursor entre telas por raycast angular, com "pular vão" ou
"parede". Com a 3A o arranjo do Windows já segue o Quest, mas diagonais, vãos e telas soltas continuam sem resposta.
O Cayo pediu que o cursor **apareça no mundo** ao sair de uma tela e entre na próxima onde tocar, e que a manipulação
pelo mouse seja igual à do controle e da mão, num **modo edição** geral do app.

## Decisão
1. O cursor do Windows é desenhado no headset (posição e forma por UDP/TCP; captura sem cursor).
2. Fora das telas, o host engole o mouse e o headset move um **cursor 3D no mundo** (graus em volta da cabeça); ao tocar
   uma tela, o host põe o cursor do Windows no pixel do toque. Substitui o "pular vão"/"parede" do brief. A saída tem
   resistência de borda (40 px em 300 ms).
3. Monitores do PC fora do Quest continuam alcançáveis pelo arranjo do Windows.
4. **Modo edição** global: só nele telas são manipuladas, por mouse (novo `IManipulationInput`), controle ou mão. Fora
   dele, os lasers só servem ao menu. Entra/sai por menu, `Ctrl+Alt+E`, `Esc`, `Ctrl+Alt` segurado (temporário).
5. O headset é dono da geometria (cursor 3D, toque, edição); o host só decide engolir/passar e posicionar o cursor.

## Consequências
- O host precisa de um hook `WH_MOUSE_LL` (callback leve) e de um canal UDP novo; sem headset conectado nada é interceptado.
- Mexer em telas com o laser passa a exigir o modo edição (menos movimentos acidentais, um passo a mais).
- O cursor (dados minúsculos) passa pelo C# do headset; o vídeo continua nativo.
