# Persistência atual do StudyHub

## Visão geral

O StudyHub salva o estado principal em **SQLite** e complementa a rotina de estudos em **arquivos JSON por curso**.

O app é local/offline para cursos em pastas locais. Os dados ficam no computador do usuário.

## Banco principal

- Caminho do banco: `FileSystem.AppDataDirectory\studyhub.db`
- Registro no startup: `MauiProgram.cs`
- Configuração SQLite: `ServiceCollectionExtensions.AddStudyHubPersistence(...)`
- Schema atual: `10`

O progresso agregado não é salvo como uma tabela única. Ele é recalculado a partir das entidades persistidas e dos registros de rotina.

## Hierarquia persistida

Hierarquia técnica:

```text
Course
  > Module
    > Topic
      > Lesson
```

Termos usados para o usuário:

```text
Curso
  > Disciplina/Módulo
    > Aula/Módulo
      > Vídeo
```

Relações principais:

- `modules.CourseId -> courses.Id`
- `topics.ModuleId -> modules.Id`
- `lessons.TopicId -> topics.Id`

## Tabelas relevantes

### `courses`

Campos importantes:

- `Id`
- `Title`
- `Description`
- `SourceType`
- `SourceMetadataJson`
- `TotalDurationMinutes`
- `AddedAt`
- `LastAccessedAt`
- `CurrentLessonId`

### `modules`

Representa agrupamentos dentro do curso, exibidos para o usuário como Disciplina/Módulo.

### `topics`

Representa a Aula/Módulo de estudo.

Campo novo no schema 10:

- `completed_at_utc`

`completed_at_utc` guarda a primeira data/hora em UTC em que a Aula/Módulo foi considerada concluída, ou seja, quando todos os vídeos dela foram concluídos. Esse valor é usado para conciliar o crédito diário de Aulas/Módulos sem depender de reconstrução retroativa.

### `lessons`

Representa os vídeos.

Campos importantes:

- `Id`
- `TopicId`
- `Order`
- `Title`
- `SourceType`
- `LocalFilePath`
- `ExternalUrl`
- `Provider`
- `DurationMinutes`
- `Status`
- `WatchedPercentage`
- `last_playback_position_seconds`

## Rotina em JSON

Diretório base:

```text
%LOCALAPPDATA%\StudyHub\Routine\<CourseId>\
```

Arquivos por curso:

- `routine_settings.json`
- `daily_records.json`

### `routine_settings.json`

Campos atuais:

- `GoalMode`
- `DailyGoalMinutes`
- `DailyGoalStudyUnits`
- `SelectedDaysOfWeek`
- `LastUpdatedAt`
- `PlanPeriods`

`GoalMode` define o modo da rotina:

- `TimeMinutes`: meta por tempo de estudo.
- `StudyUnits`: meta por Aulas/Módulos concluídos.

`DailyGoalMinutes` continua preservado por compatibilidade e histórico. Mesmo quando o usuário usa meta por Aulas/Módulos, o campo antigo não deve ser apagado sem necessidade.

`DailyGoalStudyUnits` guarda quantas Aulas/Módulos o usuário quer concluir por dia.

### `daily_records.json`

Lista de registros diários com:

- `CourseId`
- `Date`
- `MinutesStudied`
- `NonLessonMinutesStudied`
- `LessonCredits`
- `CompletedStudyUnitIds`
- `DailyGoalMinutesAtTheTime`
- `Status`

`CompletedStudyUnitIds` guarda os ids das Aulas/Módulos creditadas naquele dia. O crédito é idempotente: a mesma Aula/Módulo não deve ser contada duas vezes no mesmo dia.

Cada item de `LessonCredits` contém:

- `LessonId`
- `MinutesCredited`

## Compatibilidade com dados antigos

O app mantém compatibilidade com JSON antigo:

- ausência de `GoalMode` em `routine_settings.json` significa `TimeMinutes`;
- ausência de `DailyGoalStudyUnits` usa valor padrão seguro;
- ausência de `CompletedStudyUnitIds` em `daily_records.json` vira lista vazia;
- `CompletedStudyUnitIds: null` também vira lista vazia;
- histórico antigo baseado em tempo continua sendo avaliado pelo modo e pela meta que valiam na época.

Não há reconstrução retroativa perfeita do histórico antigo por Aulas/Módulos. A rotina por Aulas/Módulos começa a registrar conclusões a partir da versão que possui esse recurso.

## Cursos locais e ids

Para cursos locais, o `CourseId` é determinístico:

- ele é gerado a partir do caminho absoluto normalizado da pasta raiz;
- a regra usa `CreateDeterministicGuid("course|" + rootToken)`;
- mover a pasta, trocar letra de drive ou alterar o caminho raiz pode gerar outro `CourseId`.

Ids de `Module`, `Topic` e `Lesson` também precisam permanecer estáveis para preservar progresso, retomada e créditos de rotina.

## Relação com rotina e progresso

- O progresso de vídeo vive em `lessons`.
- A conclusão de Aula/Módulo vive em `topics.completed_at_utc`.
- O crédito diário por tempo vive em `LessonCredits`.
- O crédito diário por Aulas/Módulos vive em `CompletedStudyUnitIds`.
- O dashboard, calendário e menu lateral leem a avaliação diária conforme o `GoalMode`.

## Riscos de manutenção

- Mudar a geração de ids pode quebrar retomada, progresso e rotina.
- Mover/renomear cursos locais já importados pode fazer o app enxergar outro curso.
- Copiar apenas o SQLite sem os JSONs de rotina perde parte do histórico.
- Copiar apenas os JSONs sem o SQLite perde o vínculo com cursos, aulas e vídeos.

## Referências no código

- `src/studyhub-web/src/studyhub.app/MauiProgram.cs`
- `src/studyhub-web/src/studyhub.infrastructure/servicecollectionextensions.cs`
- `src/studyhub-web/src/studyhub.infrastructure/persistence/studyhubdbcontext.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/persistedprogressservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/RoutineService.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/localcourseimportservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/coursepersistencehelper.cs`
