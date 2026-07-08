# Importação e estrutura de cursos

## Estado atual

Na v1.1.0, o fluxo ativo do StudyHub é curso local por pasta.

Cursos externos, cursos online, IA, roadmaps e materiais complementares não fazem parte do fluxo ativo atual. Existe base técnica preparada para importações externas, mas ela deve ser tratada como histórico/futuro até existir uma tela completa no app.

## Hierarquia para o usuário

```text
Curso
  > Disciplina/Módulo
    > Aula/Módulo
      > Vídeo
```

Exemplo de pasta local:

```text
Curso/
  Disciplina ou Módulo/
    Aula 01/
      01 - Introdução.mp4
      02 - Continuação.mp4
    Aula 02/
      01 - Tema.mp4
```

Regras práticas:

- a pasta raiz vira o curso;
- pastas internas agrupam o conteúdo;
- vídeos devem ter nomes numerados para manter a ordem correta;
- evitar mover ou renomear arquivos de cursos já importados, principalmente com o app aberto.

## Hierarquia técnica interna

```text
Course
  > Module
    > Topic
      > Lesson
```

Relação entre termos:

- `Course` = Curso
- `Module` = Disciplina/Módulo
- `Topic` = Aula/Módulo
- `Lesson` = Vídeo

Na documentação voltada para usuário, use Aula/Módulo. Use `Topic` apenas quando for necessário explicar manutenção técnica.

## Relação com metas por Aulas/Módulos

A meta por Aulas/Módulos usa `Topic` como unidade interna.

Uma Aula/Módulo só conta para a rotina quando todos os vídeos dela foram concluídos. O app guarda a primeira conclusão em `topics.completed_at_utc` e registra o crédito diário em `CompletedStudyUnitIds`.

## Base técnica para importação externa

Já existe uma base para receber cursos externos em JSON versionado:

- `CourseSourceType.ExternalImport`
- parser de JSON externo versionado;
- importador de JSON externo;
- tabelas `external_course_imports` e `external_assessments`.

Essa base preserva o objetivo de não quebrar cursos locais, mas ainda não é o fluxo principal da UI.

## O que continua fora do fluxo ativo

- tela para subir JSON externo;
- curadoria online como experiência principal;
- geração de cursos por IA;
- roadmaps e materiais complementares;
- agenda de avaliações externas;
- cruzamento de avaliações externas com rotina diária.

## Referências centrais

- `src/studyhub-web/src/studyhub.domain/Entities/coursesource.cs`
- `src/studyhub-web/src/studyhub.application/Contracts/ExternalImport/`
- `src/studyhub-web/src/studyhub.infrastructure/services/externalcoursejsonparser.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/externalcourseimportservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/localcourseimportservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/persistence/studyhubdbcontext.cs`
