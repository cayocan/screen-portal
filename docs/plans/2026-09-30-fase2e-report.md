# Fase 2E — Telas curvas — relatório da Parte A (código puro)

**Data:** 30/09/2026 · **Plano:** [2026-09-30-fase2e-telas-curvas.md](2026-09-30-fase2e-telas-curvas.md) ·
**Spec:** [../specs/2026-09-30-fase2e-telas-curvas-design.md](../specs/2026-09-30-fase2e-telas-curvas-design.md)

**Status:** Parte A (Tasks 1–6) concluída com TDD. Parte B (Tasks 7–13, Unity) escrita no plano e **não executada**
(precisa do Editor). Nada rodou na Unity, no Quest nem no host.

**Base:** o worktree nasceu da `main` (74b99d8), sem a spec. Avancei o branch (fast-forward) para
`phase2e/curved-screens` (a913171, que tem a spec, o ADR 0011 e o spike) antes de começar.

## Tarefas e commits

| Task | O quê | Commit |
|---|---|---|
| plano | Plano da 2E (Parte A pura + Parte B Unity em fila) | 3d8408b |
| 1 | `CurvaturePresets` (Plana 0 · Suave 0,3 · Média 0,6 · Envolvente 1,0) | 85c0d7f |
| 2 | `CurvedGeometry` (c', R, arco, limite de 120°, centro do cilindro, ponto na tela, rótulo) | 5db6867 |
| 3 | Hit-test no cilindro (`RaycastCylinder`/`RaycastScreen`, `Pick` curvo) + `HitRegion.CurvatureHandle` | 216b8aa |
| 4 | Menu do pulso: "Curvatura ▸" + página com presets (`CurvatureMenu`) | c0fede6 |
| 5 | Alça de curvatura no controlador (`ManipulationState.Curving`, prévia → um `SetScreenCurvature`); resize de tela curva pelo arco | cd29d30 |
| 6 | `HostSync`: plana ↔ curva fecha e reabre o decoder na Surface nova (mesmo slot, `StartStream` → IDR) | d021516 |

## Testes

- `dotnet test tests-dotnet/UnityPureCode.Tests`: **262 passando** (base 215; +47), 0 falhas, depois de cada tarefa.
- `dotnet test host/Host.Tests`: **71 passando** (o host compila Domain/Application via `Host.Shared`; só os testes, sem subir o host).
- Um caso antigo mudou de propósito: em `ScreenHitTestTests`, `(0; 0,29)` era `Frame` e agora é `CurvatureHandle` (o meio
  do topo da moldura é a alça nova, spec 2E §3). Os outros casos de moldura continuam.

## Decisões onde a spec é omissa

1. **Rótulo = curvatura efetiva** (a que se vê). Com arco limitado: `"70 % · ≈ 1400R · arco limitado"` (sem nome de preset,
   porque 70 % não é preset). O menu marca o preset pelo valor **pedido** (`VirtualScreen.Curvature`). Tela plana: `"Plana"`.
2. **Alça de curvatura só na faixa da moldura** (acima da borda de cima), 10 cm de largura, centrada
   (`ScreenHitTest.CurvatureHandleWidthM`). Não invade o conteúdo: é onde fica a barra de título do Windows (Fase 3).
3. **"Para frente" = para longe do usuário** (normal da tela, +Z local): empurrar curva, puxar achata. Mede a origem do
   ponteiro desde o gatilho; o analógico (`DistanceDeltaM`) **não** entra — na mão ele já é o empurrar/puxar e contaria
   duas vezes. Sem ímã nos presets durante o arraste (a spec não pede).
4. **A página "Curvatura" fica aberta** depois de escolher um preset (comparar); a linha de status mostra o rótulo da tela
   em foco (`CurvatureMenu.Status`, em cache por workspace). Sem foco: `"Curvatura: sem tela em foco"` e presets apagados.
5. **Resize de tela curva**: a distância inicial vem das coordenadas locais do hit (arco), e o arraste intercepta o cilindro
   com o raio do início. Na tela plana dá exatamente o mesmo de antes.
6. **Hit-test de fora do cilindro** (ex.: atrás da tela): vale a interseção mais próxima dentro do arco. De dentro (o caso
   normal) só existe a face interna.
7. **`HostSync` reabre o decoder** (não estava na lista, mas é Application pura e é o que faz o "decoder reabre na Surface
   nova e pede IDR" da spec §4): fecha o decoder no evento; em Starting/Streaming/Live volta a `Ready`; o host já responde
   a `StartStream` repetido com `RequestKeyframe`. Sem novo `AcquireScreen` (o monitor virtual não muda). Offline: só fecha
   (a reconexão reabre).

## O que a Parte B (Unity) precisa saber

- **Contrato da forma da layer:** a `VideoScreenView` deve recriar a `OVROverlay` só no `Apply` **confirmado** quando
  `radiusM > 0` muda (Task 9). O `HostSync` fecha o decoder no mesmo `StateChanged` e reabre no próximo `Tick`
  (`TryGetSurface` devolve false até a Surface nova existir — já tratado). A **prévia nunca troca a forma** (senão o decoder
  fica na Surface velha até soltar); durante a prévia a layer cilíndrica só muda o raio.
- **c' > 0 ⇔ c > 0**: o `HostSync` usa `screen.Curvature > 0` e a view usa `CurvedGeometry.RadiusM > 0`; são equivalentes.
- **Transform da layer cilíndrica:** filho da raiz da tela em `(0, 0, −R)`, escala `(largura, altura, R)` — mesma convenção
  do spike (`Phase0VideoSpike.CreateOverlay`).
- **`IScreenView` ganha `radiusM`** (Task 7). É Application, mas ficou na fila Unity porque quebra a compilação da
  Presentation se entrar sozinho.
- APIs novas prontas para a Presentation: `CurvedGeometry.Of(...)`, `.RadiusM`, `.Label()`, `.Percent`, `.RoundedRadiusMm`,
  `.ArcLimited`, `CurvedGeometry.SurfacePoint(pose, R, x, y)`, `CurvedGeometry.CylinderCenter`, `ManipulationPreview.Curvature`,
  `ManipulationState.Curving`, `HitRegion.CurvatureHandle`, `ScreenHitTest.CurvatureHandleWidthM`.
- Testes com UnityEngine (`CurvedQuadMeshTests`) vão em `Tests/Composition/` (o harness compila `Tests/EditMode/**` sem Unity).

## Riscos e pendências

1. **Merge com a 2D:** `MenuItemId`, `MenuPage`, as duas listas `MainItems*` e `WristMenuTests` ganham membros no fim nos
   dois ramos → conflito textual trivial (manter os dois). A principal fica com 11 itens (12 com "+ Tela de teste"); com
   "Recriar ▸" da 2D vai a 12/13. `WristMenu.MaxItems` (12) só limita a página de espelhos e o `MenuLayout` aceita qualquer
   número, mas o painel ganha uma linha — conferir no Simulator.
2. **Glifos "▸" e "●"** na fonte `LegacyRuntime.ttf` do painel: não verificados (conferir na Task 12; se faltar, trocar por
   "›" / "•" em `CurvatureMenu.MarkedPrefix` e no rótulo do item).
3. **Estado "Conectando" na troca plana ↔ curva:** o `HostSync` passa por Ready → Starting → Streaming → Live, e a view
   escurece a layer por ~35–100 ms (layer nova ainda vazia). Deve ser imperceptível como no spike, mas conferir no Quest
   (Task 13); se piscar, manter o estado Live durante a reabertura por forma.
4. O teste `Preview_CarriesTheScreenCurvature_WhileMoving` pega a moldura pelo ponto no arco (`SurfacePoint`), e não pelo
   plano como está escrito no plano — mais robusto; mesma intenção.
