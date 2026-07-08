# StudyHub Windows Runbook

## Escopo atual

StudyHub v1.1.0 é um app Windows para cursos locais em pastas.

Fluxos ativos:

- importação de curso local por pasta;
- player local de vídeos;
- progresso por vídeo, Aula/Módulo e curso;
- rotina por tempo ou por Aulas/Módulos;
- calendário/histórico local;
- status Ativo, Pausado e Concluído;
- edição manual de nome e descrição;
- backup, restore e reset dos dados locais do app.

Fluxos antigos de IA, roadmaps, cursos online e vídeos externos não fazem parte do fluxo ativo da release atual.

## Dados locais

- Banco: `FileSystem.AppDataDirectory\studyhub.db`
- Sidecars SQLite: `studyhub.db-wal`, `studyhub.db-shm`, `studyhub.db-journal`
- Backups: `FileSystem.AppDataDirectory\backups\studyhub-backup-<timestamp>\`
- Rotina: `%LOCALAPPDATA%\StudyHub\Routine\`

O pacote publicado não deve conter banco SQLite, JSONs de rotina, backups ou cursos do usuário.

## Build Windows

Build do app:

```powershell
dotnet build .\src\studyhub-web\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 --no-restore -v minimal
```

Publish validado:

```powershell
dotnet publish .\src\studyhub-web\src\studyhub.app\studyhub.app.csproj -f net10.0-windows10.0.19041.0 -c Release --self-contained false -v minimal
```

Script de distribuição limpa:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-windows-clean.ps1
```

Saída limpa:

```text
dist\windows\studyhub-windows-x64\
```

Essa pasta contém:

- `runtime\`
- `abrir-studyhub.cmd`
- `como-abrir.txt`

## Empacotamento

Zipar a pasta wrapper limpa:

```text
dist\windows\studyhub-windows-x64\
```

Nome público do asset:

```text
StudyHub-v<versao>-windows-x64.zip
```

Asset atual:

```text
StudyHub-v1.1.0-windows-x64.zip
```

SHA256 publicado:

```text
5DDFFA3244D7D4A08A76B72B65D1FA70113263CEB2D3426930A9FBEF87BBCD8F
```

## GitHub Release

Página de releases:

- https://github.com/jotaCorsino/study.app/releases

Release atual:

- https://github.com/jotaCorsino/study.app/releases/tag/v1.1.0

Fluxo recomendado:

1. Validar testes e build.
2. Gerar distribuição limpa.
3. Gerar ZIP Windows x64.
4. Validar SHA256.
5. Fazer smoke test do executável.
6. Publicar o ZIP como asset de GitHub Release.

## Smoke test

Executar:

```text
dist\windows\studyhub-windows-x64\runtime\studyhub.app.exe
```

Validar:

- o executável abre;
- há janela principal;
- o processo responde;
- não há erro fatal no startup;
- a versão do executável corresponde à release esperada.

## Instalação local

Para atualizar uma instalação local:

1. Encerrar `studyhub.app.exe`, se estiver aberto.
2. Validar o hash do ZIP publicado.
3. Fazer backup apenas da pasta do app.
4. Extrair o ZIP em pasta temporária.
5. Substituir a pasta do app pela pasta extraída.
6. Não apagar `%LOCALAPPDATA%\StudyHub`.

## Backup e restore

Backups do app devem conter:

- `database\studyhub.db`
- sidecars SQLite quando existirem;
- `routine\...` com JSONs por curso;
- `backup-manifest.json`.

Reset e restore operam apenas no estado local do StudyHub. Os arquivos físicos dos cursos do usuário não devem ser apagados.
