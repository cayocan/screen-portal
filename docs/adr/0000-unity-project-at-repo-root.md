# ADR 0000 — Projeto Unity na raiz do repositório

- **Status:** Aceito (27/09/2026)
- **Contexto:** o brief (§7.2) coloca o projeto Unity em `headset-unity/`. Quando o repositório
  foi iniciado, o projeto Unity 6.3 LTS já existia na raiz desta pasta, com os pacotes da §6.4
  instalados (Meta XR 207.0.0, AI Assistant 2.20.0-pre.1, Pipeline 0.8.0-exp.1) e o Editor aberto.
  Mover exigiria fechar o Editor e reimportar a `Library/`.
- **Decisão:** manter o projeto Unity na raiz. As demais pastas do monorepo (`host/`,
  `headset-native/`, `protocol/`, `docs/`, `driver/`, `tools/`) ficam ao lado de `Assets/`.
  O código do app vive em `Assets/_Project/` com a mesma subdivisão do brief.
- **Consequências:**
  - Onde o brief diz `headset-unity/...`, ler `./...`. Batchmode usa `-projectPath .`.
  - A Unity ignora pastas fora de `Assets/`, `Packages/` e `ProjectSettings/`, então os outros
    projetos não entram no import. Os `.csproj`/`.slnx` gerados pela Unity na raiz ficam fora do Git.
  - O host tem o próprio `host/Host.sln`; nunca usar a solução da raiz para o host.
  - Reversível: mover para `headset-unity/` depois é um commit de `git mv` com o Editor fechado.
