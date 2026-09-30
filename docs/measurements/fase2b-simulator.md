# Fase 2B — verificação (Simulator concluído; falta conferência visual do Cayo e o Quest)

**Branch:** `phase2b/manipulation` · **Plano:** [docs/plans/2026-09-29-fase2b-manipulacao.md](../plans/2026-09-29-fase2b-manipulacao.md)

## 1. Testes automáticos (30/09/2026)
- Harness `dotnet test`: **168** (Domain + Application, incluindo 9 invariantes da superfície, encaixe, acerto do raio,
  controlador de manipulação, menu e roteador). Host: **40**. Unity Test Runner (EditMode + composição): **169**, 0 falhas.

## 2. Play Mode no Editor, sem XR (30/09/2026)
Via `unity` CLI (`eval` + captura da câmera da cabeça). Sem Simulator: não há controle nem mão, então só o caminho
menu → Command → view foi exercitado.

| Verificação | Resultado |
|---|---|
| Composição sobe em Play Mode (VContainer, OVR adapters, menu, apresentador) | ✅ sem erros no console |
| "+ Tela de teste" ×3 | ✅ Teste 1/2/3 a az 0°, 43,2°, 78,4° (el −5°, 1,3 m), sem sobreposição; status "Cilindro · 3 telas" |
| Views de teste renderizam (textura, borda, rótulo legível, orientada para o eixo) | ✅ captura da CenterEyeAnchor |
| Cilindro → Esfera | ✅ posição mantida (0; 1,087; 1,295), tela inclina 5° para o centro |
| Desfazer | ✅ volta ao Cilindro |

## 3. Meta XR Simulator + Meta XR Operator (30/09/2026)
**Commits:** `0eb4f8d` (XR no Standalone), `788de5f` (texto estéreo), `258de58` (calibração da palma).
Simulator v207 (runtime OpenXR ativo do sistema) + Meta XR Operator (camada de API, backend em `127.0.0.1:8720`),
Play Mode na cena `Main`, sem PC host (telas de teste). Tudo pelo caminho real de entrada: pose/botões do controle e
mão sintética injetados via OpenXR (`openxr_*`); estado lido do `WorkspaceStore` e dos transforms (`unity` CLI `eval`);
capturas compostas do Simulator para o visual.

### Preparação (o que faltava e foi ajustado)
- **XR no Standalone:** só o Android tinha loader. Adicionado o OpenXR Loader para Standalone (Meta XR Feature e
  API Layers já ativos) + Oculus Touch Controller Profile. Sem isso o Play Mode roda sem XR.
- **Porta 8720 sequestrada:** um `adb forward tcp:8720` antigo (sessão do Quest) prendia `127.0.0.1:8720` e o cliente
  MCP caía no adb. `adb forward --remove-all` resolveu. Se o Operator "não responder", conferir `adb forward --list`.
- **Editor em segundo plano:** a sessão fica em READY e a captura sai branca enquanto a janela da Unity não tem foco;
  `editor_focus` + `set_autotick` levam a FOCUSED.
- **Raio do controle = pose de grip**, não a de aim: o `OVRInput` (e o raio do app) segue o grip, com 60° de inclinação
  para baixo. Para mirar: grip = direção × pitch(+60°), com a origem compensada (~4 cm).
- **Cabeça:** fixada com `openxr_set_head_pose` (0; 1,2; 0) — o Simulator a mexe sozinho quando recebe mouse.

### Bug achado e corrigido: texto só no olho esquerdo
Os rótulos (`TextMesh` com o material da fonte, `GUI/Text Shader`) saíam **só no olho esquerdo e com projeção mono**
(centrados, desalinhados das telas); o olho direito não tinha texto nenhum ([esq.](fase2b-simulator/01-texto-antes-olho-esq.png),
[dir.](fase2b-simulator/02-texto-antes-olho-dir.png)). Quads e linhas (URP/Unlit) estavam certos nos dois olhos.
Um shader CG próprio com macros de estéreo e `target 3.5` teve o mesmo defeito; reescrito em HLSL da URP
(`Core.hlsl`, `TransformObjectToHClip`) funcionou ([depois, olho dir.](fase2b-simulator/03-texto-depois-olho-dir.png)).
Afeta o Quest também (multiview) — o status "Conectando…" da 1B provavelmente só aparecia num olho.
Correção: `Presentation/Resources/StereoText.shader` + `StereoText.MaterialFor(font)` nos 3 pontos com `TextMesh`
(rótulo da tela de teste, menu de pulso, status da conexão). Sem teste automático (renderização); verificado aqui.

### Roteiro
| # | Ação | Resultado |
|---|---|---|
| 1 | Segurar Menu (≡) no controle esquerdo | ✅ menu acima da mão, virado para a cabeça; status "Cilindro · 0 telas" |
| 2 | Raio em "+ Tela de teste" e gatilho, 3× | ✅ hover no botão certo; Teste 1/2/3 a az 0°/43,2°/78,4°, sem sobreposição; 1 Command por clique |
| 3 | Apontar a moldura / o meio da Teste 1 | ✅ faixa externa de 3 cm: moldura branca + 4 alças ([captura](fase2b-simulator/04-moldura-alcas.png)); meio: nada |
| 4 | Gatilho na moldura, girar 20° à direita, soltar | ✅ tela segue o raio (preview) e fica a az 16,5° ao soltar; store só muda no soltar |
| 5 | X; depois Y (controle esquerdo) | ✅ X → az 0°; Y → az 16,5° |
| 6 | Arrastar para cima no Cilindro | ✅ el −5° → 5,1°, rotação continua (0; 16,5; 0) — sobe sem inclinar |
| 7 | Analógico para cima durante o arraste | ✅ distância 1,30 → 2,92 m; soltar = 1 passo (1 X volta tudo, Y refaz) |
| 8 | Arrastar a borda perto da vizinha | ✅ em passos de 1,5°: 2,65° → gruda em 1,00° com guia amarela ([captura](fase2b-simulator/05-encaixe-guia.png)) → continua e desgruda (−1,22°) |
| 9 | Idem segurando o gatilho esquerdo | ✅ 2,01° → 1,37° → 0,72° → 0,08°, sem guia |
| 10 | Gatilho numa alça e afastar | ✅ largura 1,00 → 1,33 m, altura proporcional (0,563 → 0,750); rótulo segue "1920×1080" ([captura](fase2b-simulator/06-redimensionar.png)) |
| 11 | Arrastar com o grip; menu "Voltar à superfície" | ✅ `loose=True`, 1,16 m do centro; botão habilitado → volta (az 10,7°, mantém 1,16 m) e desabilita |
| 12 | Menu "Cilindro → Esfera"; desfazer | ✅ nenhuma posição muda, todas inclinam 5°; X volta ao Cilindro |
| 13 | Olhar 40° à esquerda e "Recentrar" | ✅ heading −40°, o layout inteiro gira para a nova frente ([captura](fase2b-simulator/07-recentrar.png)) |
| 14 | B durante um arraste | ✅ tela volta na hora; nada gravado; o X seguinte desfez o Recentrar (nenhum passo extra) |
| 15 | Mãos: pinçar a moldura, mover, empurrar/puxar | ✅ hover pela pose de ponteiro da mão; lado: az 10,7° → 15,2°; empurrar 20 cm: 1,16 → 1,87 m; puxar 20 cm: 1,87 → 1,41 m |
| 16 | Mãos: palma esquerda para o rosto | ✅ após calibrar (abaixo) ([captura](fase2b-simulator/08-menu-palma.png)); menu operado por pinça da mão direita ("Desfazer" desfez o puxar) |
| 17 | Menu "Remover tela"; desfazer | ✅ remove a tela em foco (Teste 1); X a traz de volta no mesmo lugar |

**Calibração da palma (item 16):** com o valor original `(0,1,0)` o menu não aparecia. Medido o produto escalar da normal
candidata com a direção da cabeça em três poses da mão sintética: `(0,−1,0)` dá **+0,86** com a palma para o rosto,
−0,29 com a palma para baixo e −0,86 com a palma para fora (limiar 0,5). Gravado no `MainSceneBuilder` e cena regenerada.

### Observações (não bloqueiam; candidatas ao backlog)
- As 3 telas de teste vão todas para a direita (0°, 43°, 78°): a terceira fica quase ao lado. Centralizar o conjunto
  seria mais confortável.
- O status "Conectando…" é fixo no mundo e não acompanha o Recentrar.
- Com o menu da palma aberto, o raio da mão esquerda (apontando para cima) atravessa o painel.
- Redimensionar pode sobrepor a vizinha (coerente com "liberdade"; o encaixe só atua no mover).

### Testes depois das correções
Unity Test Runner (EditMode): **169**, 0 falhas.
