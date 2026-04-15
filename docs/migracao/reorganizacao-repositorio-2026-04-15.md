# Reorganização do repositório em 2026-04-15

## Movimentações executadas

- `app_build/src` → `src/studyhub-web/src`
- `app_build/tests` → `src/studyhub-web/tests`
- `Extencao-Navegador` → `src/studyhub-extension`
- `img_ref` → `assets`
- `docs/runbook-windows.md` → `docs/estado-atual/runbook-windows.md`
- `docs/windows-runbook.md` → `docs/estado-atual/windows-runbook.md`

## Ajustes complementares

- Atualização do script `scripts/publish-windows-clean.ps1` para o novo caminho do projeto MAUI.
- Atualização dos runbooks e arquivos auxiliares que ainda referenciavam `app_build` e `img_ref`.

## Resultado

- O repositório passa a seguir a convenção pedida para `src`, `docs` e `assets`.
- A solução e os projetos preservam a mesma estrutura interna relativa, reduzindo o risco de impacto funcional.
