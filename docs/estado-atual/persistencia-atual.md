# Persistência atual do StudyHub

## Visão geral

O StudyHub salva o estado principal em **SQLite** e complementa a rotina de estudos em **arquivos JSON por curso**.

O app é local/offline para cursos em pastas locais. Os dados ficam no computador do usuário.

## Banco principal

- Caminho do banco: `FileSystem.AppDataDirectory\studyhub.db`
- Registro no startup: `MauiProgram.cs`
- Configuração SQLite: `ServiceCollectionExtensions.AddStudyHubPersistence(...)`
- Schema atual: `13`

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
- `SourceMetadataJson`, incluindo `RootPath` e `LastScannedAtUtc` para cursos locais
- `TotalDurationMinutes`
- `AddedAt`
- `LastAccessedAt`
- `CurrentLessonId`

### `modules`

Representa agrupamentos dentro do curso, exibidos para o usuário como Disciplina/Módulo.

Campos de origem relevantes:

- `source_relative_path`: identidade física portátil relativa à raiz do curso;
- `is_available`: indica se o módulo ainda foi encontrado na última sincronização aplicada.

### `topics`

Representa a Aula/Módulo de estudo.

Campo introduzido no schema 10:

- `completed_at_utc`

`completed_at_utc` guarda a primeira data/hora em UTC em que a Aula/Módulo foi considerada concluída, ou seja, quando todos os vídeos dela foram concluídos. Esse valor é usado para conciliar o crédito diário de Aulas/Módulos sem depender de reconstrução retroativa.

Campos de origem atuais:

- `source_relative_path`: identidade física portátil relativa à raiz do curso;
- `is_available`: indica se o tópico ainda foi encontrado na última sincronização aplicada.

### `lessons`

Representa os vídeos.

Campos importantes:

- `Id`
- `TopicId`
- `Order`
- `Title`
- `SourceType`
- `LocalFilePath`
- `RelativeFilePath`
- `ExternalUrl`
- `Provider`
- `DurationMinutes`
- `Status`
- `WatchedPercentage`
- `last_playback_position_seconds`
- `IsAvailable`

Para aulas locais, o player resolve primeiro `SourceMetadata.RootPath + Lesson.RelativeFilePath`. `LocalFilePath` e `FilePath` permanecem apenas como fallbacks legados.

## Gerenciamento de origem e sincronização incremental

A identidade persistida do curso é independente de sua localização atual:

- `Course.Id` continua sendo a identidade autoritativa depois da importação;
- `SourceMetadata.RootPath` guarda a raiz física mutável;
- alterar a localização valida a pasta candidata e atualiza a raiz sem trocar os IDs persistidos;
- IDs transitórios produzidos pelo scanner não são usados para substituir a identidade de conteúdo existente.

Módulos, tópicos e aulas são correlacionados por caminhos relativos normalizados. A prévia classifica cada item como:

- `Unchanged`: existe no banco e na pasta;
- `New`: existe apenas na pasta detectada;
- `Missing`: permanece persistido, mas não foi encontrado.

A aplicação faz uma nova detecção imediatamente antes de abrir a transação. Dentro dela, recarrega a árvore persistida e recalcula o plano contra a estrutura detectada. Itens `New` recebem IDs permanentes; itens `Missing` não são apagados e passam a `IsAvailable = false`. Se reaparecerem no mesmo caminho relativo, recuperam o mesmo ID e voltam a ficar disponíveis.

Rename ou move interno ainda é tratado como `Missing + New`; não existe detecção automática de rename/move.

Após uma sincronização bem-sucedida:

- `LastScannedAtUtc` e a duração total conhecida são atualizados;
- o snapshot de importação é reconstruído a partir da árvore persistida autoritativa, incluindo itens ausentes;
- permanecem preservados IDs, status, percentual assistido, posição de playback, `CompletedAtUtc`, `CurrentLessonId`, rotina e histórico.

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

O initializer atualiza bancos SQLite antigos de forma incremental e conservadora:

- schema 11: adiciona `Lesson.RelativeFilePath` e só deriva o caminho relativo a partir de `LocalFilePath` ou `FilePath` absolutos quando a correlação com a raiz é segura;
- schema 12: adiciona `Module.SourceRelativePath` e `Topic.SourceRelativePath`, usando snapshot ou inferência segura;
- schema 13: adiciona `IsAvailable` a módulos, tópicos e aulas.

Backfills inseguros permanecem vazios em vez de inventar identidades. O manifest legado e a reidratação preservam progresso e seleção atual.

O app mantém compatibilidade com JSON antigo:

- ausência de `GoalMode` em `routine_settings.json` significa `TimeMinutes`;
- ausência de `DailyGoalStudyUnits` usa valor padrão seguro;
- ausência de `CompletedStudyUnitIds` em `daily_records.json` vira lista vazia;
- `CompletedStudyUnitIds: null` também vira lista vazia;
- histórico antigo baseado em tempo continua sendo avaliado pelo modo e pela meta que valiam na época.

Não há reconstrução retroativa perfeita do histórico antigo por Aulas/Módulos. A rotina por Aulas/Módulos começa a registrar conclusões a partir da versão que possui esse recurso.

## Cursos locais e IDs

Na primeira importação, o builder ainda pode gerar um ID determinístico a partir da raiz selecionada. Depois que o curso é persistido, porém, `Course.Id` é a identidade autoritativa e não muda quando `RootPath` é alterado pelo fluxo de relocalização.

IDs de `Module`, `Topic` e `Lesson` permanecem estáveis por correlação com suas identidades relativas. Isso preserva progresso, retomada, conclusão e créditos de rotina mesmo quando a raiz física do curso muda.

## Relação com rotina e progresso

- O progresso de vídeo vive em `lessons`.
- A conclusão de Aula/Módulo vive em `topics.completed_at_utc`.
- O crédito diário por tempo vive em `LessonCredits`.
- O crédito diário por Aulas/Módulos vive em `CompletedStudyUnitIds`.
- O dashboard, calendário e menu lateral leem a avaliação diária conforme o `GoalMode`.

## Riscos de manutenção

- Mudar a geração de ids pode quebrar retomada, progresso e rotina.
- Alterar a raiz fora do fluxo **Alterar localização** pode deixar o curso temporariamente indisponível.
- Renomear ou mover conteúdo interno é interpretado como `Missing + New` e não preserva automaticamente a identidade do item renomeado.
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
- `src/studyhub-web/src/studyhub.infrastructure/services/coursesourcemanagementservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/coursecontentsyncservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/coursecontentsyncplanner.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/coursecontentsyncapplier.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/locallessonfilepathresolver.cs`
