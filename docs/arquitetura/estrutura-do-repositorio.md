# Estrutura do repositório

## Pastas principais

- `src/studyhub-web`
  Contém a solução principal do StudyHub em `src/` e os testes automatizados em `tests/`.
- `src/studyhub-extension`
  Contém a extensão de navegador e seus arquivos auxiliares. Ela não faz parte do pacote Windows do StudyHub.
- `docs/estado-atual`
  Centraliza runbooks e documentação operacional do estado vigente do projeto.
- `docs/arquitetura`
  Centraliza documentação de organização e decisões estruturais do repositório.
- `docs/migracao`
  Centraliza registros de reorganizações e migrações de estrutura.
- `docs/release-notes.md`
  Registra as notas da release atual publicada no GitHub.
- `assets`
  Centraliza ícones e outros arquivos visuais de referência compartilhados.

## Artefatos locais

Artefatos de build e release local ficam fora do controle de versão:

- `dist\`
- `production_artifacts\`

O ZIP público de Windows x64 segue o padrão:

```text
StudyHub-v<versao>-windows-x64.zip
```

Exemplo atual:

```text
StudyHub-v1.1.0-windows-x64.zip
```

## Observações

- A solução principal permanece em `src/studyhub-web/src/studyhub.slnx`.
- O app Windows é focado em cursos locais/offline.
- A extensão em `src/studyhub-extension` deve ser tratada como área separada e não entra no pacote Windows.
- Scripts e runbooks devem apontar para os caminhos atuais do repositório.
