## Nota importante

Este projeto é 100% pessoal, 110% amador e 1000% criado para satisfazer uma vontade particular: organizar meus cursos de um jeito que funcione para mim. Não foi feito em padrão de mercado e pode ter decisões simples ou improvisadas. Sou estudante e construo este app "vibe codando" com Codex, para me ajudar a estudar.

# StudyHub

StudyHub é um app Windows para organizar cursos locais em pastas. Ele transforma uma pasta de vídeos em um catálogo de cursos com player local, progresso e rotina de estudos.

Hoje o app oferece:

- catálogo de cursos locais;
- página interna por curso;
- player local de vídeos;
- progresso por vídeo, Aula/Módulo e curso;
- retomada do ponto onde você parou;
- rotina de estudos;
- metas diárias por tempo ou por Aulas/Módulos concluídos;
- histórico local;
- backup e restauração local;
- status de curso: Ativo, Pausado e Concluído;
- edição manual de nome e descrição do curso.

O app atual é focado em cursos locais/offline. Os dados ficam no computador do próprio usuário.

## Novidades da v1.1.0

- Adicionada escolha entre meta diária por tempo de estudo ou por Aulas/Módulos concluídos.
- Rotina, dashboard, calendário e menu lateral respeitam o modo escolhido.
- Uma Aula/Módulo conta apenas quando todos os vídeos dela são concluídos.
- Histórico antigo por tempo permanece compatível.
- Crédito de Aulas/Módulos evita duplicidade.

Release publicada:

- [StudyHub v1.1.0](https://github.com/jotaCorsino/study.app/releases/tag/v1.1.0)

## Download da release

Baixe sempre pela página oficial de releases:

- [https://github.com/jotaCorsino/study.app/releases](https://github.com/jotaCorsino/study.app/releases)

Arquivo principal para Windows:

- `StudyHub-v<versao>-windows-x64.zip`

Exemplo atual:

- `StudyHub-v1.1.0-windows-x64.zip`

## Como instalar e abrir no Windows

1. Baixe o `.zip` da versão mais recente na página de releases.
2. Extraia o arquivo em qualquer pasta do computador.
3. Abra `abrir-studyhub.cmd`.

Alternativa:

- execute diretamente `runtime\studyhub.app.exe`.

## Onde os dados ficam

Os dados do StudyHub ficam localmente no computador do próprio usuário, incluindo:

- banco local do app;
- progresso dos vídeos;
- rotina, metas e histórico;
- backups locais do app;
- referências para os cursos locais importados.

O pacote de release não inclui dados pessoais de quem publicou a release.

## Fluxo rápido de uso

1. Abra o app.
2. Adicione um curso pela pasta local.
3. Entre no curso e abra uma Aula/Módulo.
4. Estude normalmente e acompanhe o progresso.
5. Configure uma rotina por tempo ou por Aulas/Módulos.
6. Pause, reative, conclua ou edite nome/descrição do curso quando precisar.
7. Feche e reabra quando quiser: o app preserva o estado salvo.

## Como adicionar cursos

Use quando você já tem os vídeos no computador:

1. Clique em **Adicionar curso**.
2. Selecione a pasta raiz do curso.
3. Aguarde a importação.
4. Abra o curso no catálogo.

## Organização e progresso

O StudyHub entende a organização do curso de forma simples:

```text
Curso
  > Disciplina/Módulo
    > Aula/Módulo de estudo
      > Vídeos
```

Na prática, o app acompanha:

- progresso por curso;
- progresso por Disciplina/Módulo;
- progresso por Aula/Módulo;
- progresso por vídeo.

O catálogo e a sidebar mostram cursos locais importados. A sidebar separa cursos em Ativos, Pausados e Concluídos.

A página do curso permite alterar manualmente nome e descrição sem mudar a pasta original.

O histórico usa períodos de vigência para manter metas antigas corretas. Assim, se você muda a rotina depois, os dias antigos continuam sendo avaliados pela meta que valia naquela época.

## Rotina e metas de estudo

A rotina diária pode funcionar de dois jeitos.

### Modo Tempo

Você escolhe uma quantidade diária de estudo, por exemplo `1h 30m`.

Quando um vídeo é concluído, a duração dele soma tempo no dia. Esse é o comportamento antigo do app e continua compatível com o histórico já existente.

### Modo Aulas/Módulos

Você escolhe quantas Aulas/Módulos quer concluir por dia.

Uma Aula/Módulo só conta quando todos os vídeos dela forem concluídos.

Exemplo:

Se a meta for `1 Aula/Módulo por dia`, assistir apenas parte dos vídeos mantém o dia em `0/1`. Ao concluir todos os vídeos daquela Aula/Módulo, o dia fica `1/1`.

## Estrutura recomendada da pasta do curso

Organização recomendada:

```text
Curso/
  Disciplina ou Módulo/
    Aula 01/
      01 - Introdução.mp4
      02 - Continuação.mp4
    Aula 02/
      01 - Tema.mp4
```

Como o app interpreta:

- a pasta raiz vira o curso;
- pastas internas agrupam o conteúdo;
- vídeos devem ter nomes numerados para manter a ordem correta.

Boas práticas:

- use numeração como `01`, `02`, `03`;
- evite nomes genéricos repetidos como `aula1.mp4` em várias pastas;
- evite mover ou renomear arquivos de cursos já importados.

## Player local de aulas

- o player local usa vídeos do próprio curso importado por pasta;
- controle de velocidade com opções: `0.5x`, `1x`, `1.5x`, `2x`, `2.5x`;
- velocidade e intro skip ficam em botões compactos no painel inferior;
- cada botão abre uma janelinha compacta para configuração;
- intro skip é configurado por curso;
- se a aula tiver posição salva de retomada, a retomada vence sempre;
- o intro skip só entra quando a aula começa do zero;
- play/pause funciona por clique direto na área do vídeo local.

## Limitações atuais

- o app é focado em cursos locais;
- recursos antigos de IA, roadmaps e cursos online não fazem parte do fluxo ativo;
- a rotina por Aulas/Módulos começa a registrar conclusões a partir da versão que possui esse recurso;
- não há reconstrução retroativa perfeita do histórico antigo por Aulas/Módulos;
- ainda não há fluxo completo de desconclusão/reversão de crédito de Aula/Módulo;
- evitar mover ou renomear arquivos de cursos já importados.
