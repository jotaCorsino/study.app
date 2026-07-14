# Importação e estrutura de cursos

## Estado atual

O fluxo ativo do StudyHub é curso local por pasta. A versão 1.2.0 em preparação acrescenta gerenciamento da origem física e sincronização incremental para cursos já importados.

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
- quando a raiz inteira mudar, use **Configurações → Cursos e armazenamento → Alterar localização**;
- rename ou move de conteúdo interno continua sendo interpretado como item ausente + item novo.

## Importação, localização e sincronização

Os três fluxos têm responsabilidades diferentes:

1. **Importar curso** cria o catálogo inicial a partir de uma pasta ainda não cadastrada.
2. **Alterar localização** valida a nova raiz do mesmo curso e atualiza `RootPath`, preservando IDs, progresso, retomada e histórico. Conteúdo novo não é incorporado por essa ação.
3. **Sincronizar conteúdo** compara a raiz atual com a árvore persistida, apresenta uma prévia e aplica apenas depois de confirmação.

A prévia usa as categorias `Unchanged`, `New` e `Missing`. Na aplicação:

- itens novos recebem identidades permanentes;
- itens ausentes permanecem persistidos com `IsAvailable = false`; não são apagados;
- itens que reaparecem no mesmo caminho relativo recuperam a mesma identidade;
- rename ou move interno continua como `Missing + New`.

A correlação usa `Lesson.RelativeFilePath`, `Module.SourceRelativePath` e `Topic.SourceRelativePath`. A raiz física fica em `Course.SourceMetadata.RootPath` e pode mudar sem substituir o `Course.Id` já persistido.

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
- `src/studyhub-web/src/studyhub.infrastructure/services/coursesourcemanagementservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/coursecontentsyncservice.cs`
- `src/studyhub-web/src/studyhub.application/Contracts/CourseSourceManagement/`
- `src/studyhub-web/src/studyhub.application/Contracts/CourseContentSync/`
- `src/studyhub-web/src/studyhub.infrastructure/persistence/studyhubdbcontext.cs`
