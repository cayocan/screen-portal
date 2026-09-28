# Superfície de layout (Cilindro / Esfera) — Design

- **Status:** aprovado pelo Cayo em 28/09/2026 (brainstorming). Entra no **escopo do MVP** (pilar A).
- **Documentos a atualizar na implementação:** ADR 0005, brief v1.3.2 (§3, §4.1, §4.2, §10.9, §10.10), CLAUDE.md.
- **Princípio de produto registrado aqui:** **Liberdade.** Encaixes e regras ajudam, nunca prendem: tudo que gruda
  pode ser desgrudado, e toda tela pode sair da superfície.

## 1. Objetivo

Uma "grade invisível" em volta do usuário define **onde** as telas ficam e **para onde elas olham** por padrão:

- **Cilindro:** as telas olham para o eixo vertical do cilindro. Giram só na horizontal e ficam sempre em pé.
  É o padrão, o mais parecido com monitores reais numa mesa.
- **Esfera:** as telas olham para o centro da esfera, girando na horizontal e na vertical. Telas altas ou baixas
  inclinam para o usuário. Pode ser inclinada inteira ("modo sofá").

Generaliza o brief §10.10 ("arrastar move a tela sobre uma esfera centrada na sua cabeça").

## 2. Decisões (brainstorming)

| # | Pergunta | Decisão |
|---|---|---|
| 1 | Onde fica o centro | **Ponto fixo capturado ao recentrar** (posição da cabeça sentado + direção "frente"). As telas não seguem a cabeça |
| 2 | Nível da escolha do modo | **Por perfil de layout**; cada tela pode ser marcada como **solta** (fora da superfície) |
| 3 | Trocar de modo com telas posicionadas | **Reprojetar mantendo a direção e a distância**: nenhuma tela se move, só a orientação muda. Um passo no undo |
| 4 | Encaixe padrão | **Encaixe nas vizinhas ligado, grade angular desligada.** Tudo desgrudável (limiar, `Shift`) |
| 5 | Inclinação da esfera | **"Alinhar à minha postura" + ajuste fino**, salvo no perfil |
| — | Modo de perfis novos | **Cilindro** |
| — | Arquitetura | **Superfície no domínio; telas guardam coordenadas da superfície; pose é calculada** |

## 3. Modelo de domínio (C# puro, `ScreenPortal.Domain`)

```text
LayoutMode    = Cylinder | Sphere
LayoutSurface { Mode, Center: Vec3, HeadingDeg: float, TiltDeg: float, Snap: SnapOptions }
SnapOptions   { NeighborSnap: bool = true, AngularGrid: bool = false, GridStepDeg: float = 5 }
Placement     = OnSurface { AzimuthDeg, ElevationDeg, DistanceM } | Loose { Pose }
```

- `LayoutSurface` faz parte da `Workspace` (portanto do snapshot do perfil e do undo).
- `Center`/`HeadingDeg`: capturados ao recentrar. `HeadingDeg` = yaw da "frente" (azimute 0°).
- `TiltDeg`: 0°–60°, **só a Esfera** usa (rotação do referencial da esfera em torno do eixo "direita" do centro).
  O Cilindro ignora `TiltDeg` (sempre em pé); o valor fica guardado para quando voltar à Esfera.
- `VirtualScreen` troca `Pose` por `Placement`. A pose 3D é **derivada**: `surface.ToPose(placement)`.

**Geometria (convenção da Unity: +Y cima, +Z frente, azimute positivo = para a direita):**
- Referencial da superfície: origem em `Center`, girado por `HeadingDeg` (yaw) e, na Esfera, por `TiltDeg` (pitch).
- Direção: `dir(az, el) = (cos el · sin az, sin el, cos el · cos az)` no referencial da superfície.
- **Posição (os dois modos):** `Center + R_surface · dir(az, el) · DistanceM`.
- **Orientação:**
  - Esfera: a frente da tela aponta para o centro (`-dir`, no referencial com tilt); rotação =
    `LookRotation(-dir, up do referencial)` (sem degenerar, pois a elevação é limitada a +75°).
  - Cilindro: a frente da tela aponta para o eixo, só no plano horizontal (yaw = heading + az); pitch = roll = 0.
- Consequência: **Cilindro e Esfera colocam a tela no mesmo ponto**; a diferença é só a orientação
  (mais o tilt, que só a Esfera aplica).

**Limites:** `DistanceM` 0,4–5 m (brief §10.10); `ElevationDeg` −60°…+75°; `TiltDeg` 0°–60°; `GridStepDeg` 1°–30°.
Valores fora são limitados (clamp), como a largura das telas hoje.

**Distância usada pela curvatura (§10.2):** `CurvatureDistance` = raio horizontal no Cilindro
(`DistanceM · cos el`), `DistanceM` na Esfera. Assim, curvatura 100% no Cilindro coincide com o próprio cilindro.

## 4. Comandos

| Comando | Efeito | Undo |
|---|---|---|
| `CreateScreen(resolution, placement, width, curvature, label)` | agora recebe `Placement` | sim |
| `SetLayoutMode(mode)` | troca o modo; **posições das telas no mundo preservadas**: se a Esfera tem tilt ≠ 0, os `Placement`s são reexpressos no referencial do novo modo (o Cilindro não tem tilt); telas soltas intactas | sim |
| `RecenterLayout(center, headingDeg)` | novo centro/frente; placements preservados (o layout inteiro acompanha) | sim |
| `AlignSurfaceToPosture(headPose)` | tilt = inclinação do olhar (limitado a 0°–60°); centro = posição da cabeça | sim |
| `SetSurfaceTilt(deg)` | ajuste fino do tilt | sim |
| `MoveScreenOnSurface(id, azimuthDeg, elevationDeg)` | move sobre a superfície (mantém distância) | sim |
| `SetScreenDistance(id, meters)` | Esfera: distância ao longo da direção. Cilindro: raio horizontal, mantendo a altura | sim |
| `SetScreenLoose(id, pose)` | tira a tela da superfície (pose livre) | sim |
| `ReturnScreenToSurface(id)` | volta ao ponto da superfície mais próximo da pose atual | sim |
| `SetSnapOptions(options)` | encaixe nas vizinhas / grade angular / passo | sim |

O `MoveScreen(Pose)` atual sai (substituído por `MoveScreenOnSurface` e `SetScreenLoose`).
`ResizeScreen`, curvatura, fps e `RecreateScreen` não mudam; a invariante "resize nunca muda a resolução" continua.

**Movimento vertical no Cilindro:** o comando continua `MoveScreenOnSurface(az, el)`, mas o **controlador de
manipulação** (Fase 2/3) calcula o novo `el` e a nova `DistanceM` para manter o raio horizontal
(`r = d · cos el` constante; nova altura `h` → `el = atan(h / r)`, `d = √(r² + h²)`). A função pura
`CylinderMath.MoveVertically(placement, deltaHeight)` fica no domínio e é testada.

## 5. Manipulação e encaixe

| Ação | Esfera | Cilindro |
|---|---|---|
| Arrastar na horizontal | azimute | azimute |
| Arrastar na vertical | elevação (tela inclina para o usuário) | altura, mantendo o raio horizontal (tela sobe reta) |
| Roda do mouse | distância na direção | raio horizontal (altura fixa) |
| `Alt` + arrastar | tela vira solta e move num plano | idem |

- Pré-visualização local durante o arraste (cálculo puro, sem alocação); **um comando ao soltar** (brief §10.10).
- **`SurfaceSnapper`** (puro, em ângulos): encaixe nas vizinhas (bordas, centros, topos) a ≤ 1,5°; grade angular
  (passo de azimute; fileiras por altura no Cilindro, passos de elevação na Esfera). Só age dentro do limiar;
  continuar arrastando desgruda; `Shift` desliga tudo. A invariante "tela não invade a zona" (§9.6) continua.
- **Guias invisíveis:** linhas-guia aparecem suavemente só durante o arraste.
- Controle/mãos geram exatamente os mesmos comandos (adapters, brief §8.3).

**Atalhos novos** (livres na §10.9): `Ctrl+Alt+L` alterna o modo · `Ctrl+Alt+R` recentra ·
`Ctrl+Alt+T` alinha à postura (segurar + roda = ajuste fino) · `Ctrl+Alt+G` liga/desliga a grade.
Os mesmos controles ficam no menu do app (cursor 3D, mouse-first).

## 6. Integração

- **Curvatura:** usa `CurvatureDistance` (seção 3).
- **Cursor espacial (§10.6):** o mapa angular do host usa o mesmo `Center`/`HeadingDeg`; `LayoutChanged` (Fase 1B)
  transporta a superfície e os `Placement`s; telas soltas vão como pose livre.
- **Zona de passthrough (§9.6):** recentrar ou inclinar pode empurrar telas para fora da zona, como hoje.
- **Perfis (§3b):** a superfície está no snapshot; o perfil "Sofá" pode nascer inclinado.
- **Recentrar pelo sistema:** quando o Quest recentra (evento do `OVRManager`), o app emite `RecenterLayout`.
- **Criação (§10.3):** o contorno fantasma nasce sobre a superfície: azimute 0°, elevação −5°, distância 1,3 m;
  se ocupado, o primeiro espaço livre à direita; encaixe ativo durante o posicionamento.
- **Presentation:** `OverlayScreenView` recebe a pose de `surface.ToPose(placement)`; nunca calcula orientação.
  O `ScreenViewSynchronizer` reaplica todas as telas quando a superfície muda (outra instância de `LayoutSurface`).

## 7. Testes (invariantes)

1. Cilindro: pitch = 0 e roll = 0 para qualquer azimute/elevação/distância (tolerância 0,01°).
2. Esfera: a frente da tela aponta para o centro (ângulo ≤ 0,01°), com e sem tilt.
3. `SetLayoutMode`: nenhuma posição de tela muda (tolerância 1 mm), **inclusive saindo de uma Esfera com tilt**;
   undo restaura o estado exato.
4. `CylinderMath.MoveVertically` mantém o raio horizontal (1 mm).
5. Limites de distância, elevação, tilt e passo sempre respeitados.
6. `SurfaceSnapper`: gruda dentro do limiar, não gruda fora, `Shift` (opção desligada) não gruda.
7. `RecenterLayout`: placements idênticos; todas as poses transladadas/giradas juntas.
8. Telas soltas: mantêm a pose no mundo em `SetLayoutMode`, `RecenterLayout` e mudanças de tilt (são livres por
   definição; para acompanhar o layout, o usuário as devolve à superfície); `ReturnScreenToSurface` escolhe o ponto
   mais próximo.
9. `CurvatureDistance`: Cilindro = `d · cos el`; Esfera = `d`.

## 8. Faseamento

| Parte | Fase |
|---|---|
| Domínio completo (modelo, `ToPose`, comandos, `CylinderMath`, `SurfaceSnapper`, migração de `VirtualScreen`/`CreateScreen`/views) + ADR 0005 + brief v1.3.2 | **Agora (Fase 1A.1), antes da 1B** |
| Arrastar/roda/`Alt` por controle e mãos, recentrar (atalho e evento do sistema), alinhar postura, guias | Fase 2 |
| Mesmo pelo mouse + atalhos `Ctrl+Alt+L/R/T/G` + menu do app | Fase 3 |
| Mapa angular do host (`SpatialMap`) usando a superfície | Fase 3 |

## 9. Fora do escopo

- Curvatura esférica (vertical) das telas: continua fora do MVP (§10.2).
- Outras formas (plano, caixa, cúpula): a regra de telas "soltas" cobre o caso do plano.
- Superfície seguindo a cabeça continuamente (rejeitado na decisão 1).
