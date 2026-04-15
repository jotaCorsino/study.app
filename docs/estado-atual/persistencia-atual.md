# Persistência atual do StudyHub

## Visão geral

Hoje o StudyHub salva o estado principal do app em **SQLite** e complementa a persistência de rotina/streak em **arquivos JSON por curso**.

O progresso visível do curso **não é salvo como um objeto agregado próprio**. Ele é **recalculado** a partir do estado persistido do curso e das aulas.

## Chave(s) de armazenamento atual

### 1. Banco principal

- Caminho do banco: `FileSystem.AppDataDirectory\studyhub.db`
- Registro no startup:
  `MauiProgram.cs` monta o caminho com `Path.Combine(FileSystem.AppDataDirectory, "studyhub.db")`
- Configuração:
  `ServiceCollectionExtensions.AddStudyHubPersistence(...)` registra o SQLite via `UseSqlite`

### 2. Tabelas relevantes para progresso

- `courses`
  Chave principal lógica do curso: `Id`
  Campos de progresso relevantes:
  `CurrentLessonId`, `LastAccessedAt`, `AddedAt`
- `lessons`
  Chave principal lógica da aula: `Id`
  Campos de progresso relevantes:
  `Status`, `WatchedPercentage`, `last_playback_position_seconds`, `DurationMinutes`

Observação importante:
  O schema atual mistura colunas em convenção do EF (`Id`, `CurrentLessonId`, `WatchedPercentage`) com colunas explicitamente nomeadas em snake_case (`last_playback_position_seconds`, `source_type`, `raw_title`).

### 3. Persistência complementar de rotina

- Diretório base:
  `%LOCALAPPDATA%\StudyHub\Routine\`
- Chave de isolamento:
  cada curso usa uma pasta própria com o nome do `CourseId`
- Arquivos por curso:
  `routine_settings.json`
  `daily_records.json`

### 4. Outras chaves persistidas no app

Essas chaves não guardam progresso de curso, mas fazem parte da persistência atual do app:

- `studyhub.integration.gemini_api_key`
- `studyhub.integration.youtube_api_key`

Essas duas chaves são salvas em `SecureStorage`.

## Estrutura dos dados

### Estrutura do curso persistido

O curso persistido é hierárquico:

- `courses`
- `modules`
- `topics`
- `lessons`

Os relacionamentos são:

- `modules.CourseId -> courses.Id`
- `topics.ModuleId -> modules.Id`
- `lessons.TopicId -> topics.Id`

### Campos relevantes em `courses`

- `Id`
- `Title`
- `Description`
- `SourceType`
- `SourceMetadataJson`
- `TotalDurationMinutes`
- `AddedAt`
- `LastAccessedAt`
- `CurrentLessonId`

### Campos relevantes em `lessons`

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

### Estrutura do progresso calculado em memória

Quando a UI pede progresso, o app monta um objeto `Progress` com:

- `CourseId`
- `TotalLessons`
- `CompletedLessons`
- `InProgressLessons`
- `OverallPercentage`
- `TotalWatchTime`
- `LastStudiedAt`
- `CurrentStreak`
- `LastLessonId`
- `LastLessonTitle`

Esse objeto é derivado das tabelas `courses` + `lessons` e do `RoutineService`; ele não tem tabela própria.

### Estrutura do JSON de rotina

#### `routine_settings.json`

- `DailyGoalMinutes`
- `SelectedDaysOfWeek`
- `LastUpdatedAt`

#### `daily_records.json`

Lista de registros diários com:

- `CourseId`
- `Date`
- `MinutesStudied`
- `NonLessonMinutesStudied`
- `LessonCredits`
- `DailyGoalMinutesAtTheTime`
- `Status`

Cada item de `LessonCredits` contém:

- `LessonId`
- `MinutesCredited`

## Como o curso é identificado

### Identificador principal

O curso é identificado principalmente por `courses.Id` (`Guid`).

### Cursos locais

Para `LocalFolder`, o `CourseId` é **determinístico**:

- ele é gerado a partir do caminho absoluto normalizado da pasta raiz
- a regra atual é `CreateDeterministicGuid("course|" + rootToken)`
- `rootToken` é o caminho normalizado em minúsculas, com separadores padronizados

Na prática:

- mesma pasta física tende a gerar o mesmo `CourseId`
- mover a pasta, trocar letra de drive ou alterar o caminho raiz tende a gerar outro `CourseId`

### Cursos online

Para `OnlineCurated`, o `CourseId` nasce como:

- `Guid.NewGuid()` quando o request ainda não traz um id
- depois esse mesmo id é reaproveitado nas atualizações do curso

### Identificadores de módulos, tópicos e aulas

Eles também são relevantes para preservar progresso:

- cursos locais:
  ids determinísticos com base no caminho relativo
- cursos online:
  ids derivados de `CourseId + ordem + source key`

Isso é importante porque a preservação do progresso depende fortemente desses ids continuarem iguais.

## Como aulas assistidas e última aula são salvas

## Última aula / aula atual

O app salva a aula corrente no campo:

- `courses.CurrentLessonId`

Esse campo é atualizado quando:

- o usuário abre uma aula (`OpenLessonAsync`)
- o progresso percentual muda (`UpdateLessonProgressAsync`)
- a reprodução avança (`UpdateLessonPlaybackAsync`)
- a aula é marcada como concluída (`MarkLessonCompletedAsync`)

Junto com isso, o app também atualiza:

- `courses.LastAccessedAt`

## Aula assistida / progresso por aula

O progresso por aula é salvo principalmente em `lessons`:

- `Status`
  valores como `NotStarted`, `InProgress`, `Completed`
- `WatchedPercentage`
  percentual assistido
- `last_playback_position_seconds`
  última posição de reprodução
- `DurationMinutes`
  duração usada para cálculo de watch time e crédito de rotina

### Regras atuais de atualização

- `OpenLessonAsync`
  salva apenas `CurrentLessonId` e `LastAccessedAt`
- `UpdateLessonProgressAsync`
  salva `Status` e `WatchedPercentage`
  se houve interação, também atualiza `CurrentLessonId` e `LastAccessedAt`
- `UpdateLessonPlaybackAsync`
  salva `last_playback_position_seconds`
  recalcula `WatchedPercentage`
  marca `Completed` quando a aula chega perto do fim (`>= 98%`) ou quando a conclusão é forçada
  também atualiza `CurrentLessonId` e `LastAccessedAt`
- `MarkLessonCompletedAsync`
  força:
  `Status = Completed`
  `WatchedPercentage = 100`
  `last_playback_position_seconds = duração total em segundos` quando a duração existe

## Como a UI decide a “última aula”

Na leitura do progresso:

- o `BuildProgress(...)` usa `courses.CurrentLessonId` como fonte prioritária de `LastLessonId`
- se esse id existir e apontar para uma aula válida, ele domina o resultado exibido
- o `CourseResumeService` usa `LastLessonId` + `Status` das aulas para decidir a retomada:
  aula em progresso primeiro
  senão próxima da última concluída
  senão a primeira aula

## Relação com rotina e streak

Além do SQLite, o app credita minutos estudados no `RoutineService`:

- por curso: pasta `%LOCALAPPDATA%\StudyHub\Routine\<CourseId>\`
- por aula: `LessonCredits[].LessonId`

Ou seja:

- o avanço da aula vive no SQLite
- a contabilidade diária de minutos e streak vive em JSON separado

## Preservação atual em reimportações e rebuilds

Hoje o app tenta preservar progresso quando a estrutura é reconstruída:

- `LocalFolderCourseBuilder` reaplica estado anterior por `LessonId`
- `LocalCourseImportService` reaproveita `AddedAt`, `LastAccessedAt`, `CurrentLessonId` e snapshots de aula
- `CoursePersistenceHelper.UpsertCourseAsync(...)` reaplica:
  `Status`
  `WatchedPercentage`
  `LastPlaybackPositionSeconds`
  `DurationMinutes`
  e mantém `CurrentLessonId` se a aula ainda existir

Resumo:
  a preservação depende de **ids estáveis**.

## Riscos de quebra em uma futura migração

### 1. Mudança na geração de ids

Esse é o maior risco.

Se a futura migração alterar a forma de gerar:

- `CourseId`
- `ModuleId`
- `TopicId`
- `LessonId`

o app pode perder:

- última aula
- progresso por aula
- posição de reprodução
- créditos de rotina
- streak atual

### 2. Cursos locais dependem do caminho da pasta

Hoje o `CourseId` local depende do caminho absoluto normalizado.

Se a migração:

- mover a base de dados para outra máquina
- reimportar cursos a partir de outro caminho
- mudar a regra de normalização do caminho

o mesmo curso local pode passar a parecer um curso novo.

### 3. O progresso agregado não existe como tabela própria

`Progress` é derivado de `courses` + `lessons` + `RoutineService`.

Se uma migração copiar apenas um agregado calculado e não levar os dados-base, pode haver divergência futura na UI.

### 4. Parte do progresso está em SQLite e parte em JSON

Hoje a persistência está dividida:

- SQLite para estado do curso/aula
- JSON para rotina, minutos e streak

Uma migração incompleta que leve só o banco ou só os JSONs perde consistência.

### 5. `CurrentLessonId` é o elo principal da retomada

Se a migração preservar aulas concluídas, mas perder `CurrentLessonId`, o comportamento de retomada pode mudar mesmo sem perda total de progresso.

### 6. Reimportação depende de correspondência por `LessonId`

Se nomes de arquivos, caminhos relativos ou a regra de hash mudarem, o app deixa de reconhecer a aula antiga como a mesma aula.

Nessa situação, o progresso anterior não é reaplicado.

### 7. Schema atual tem nomes mistos de colunas

Há mistura entre convenção padrão do EF e colunas nomeadas manualmente.

Uma migração automática baseada em convenções pode:

- mapear nome de coluna errado
- duplicar campo com nome diferente
- perder dados em colunas explicitamente renomeadas

## Referências no código

- `src/studyhub-web/src/studyhub.app/MauiProgram.cs`
- `src/studyhub-web/src/studyhub.infrastructure/servicecollectionextensions.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/storagepathsservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/persistence/studyhubdbcontext.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/persistedprogressservice.cs`
- `src/studyhub-web/src/studyhub.application/Services/progresscalculator.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/courseresumeservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/RoutineService.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/localcoursescanner.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/localcourseimportservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/coursepersistencehelper.cs`
- `src/studyhub-web/src/studyhub.app/services/securestorageintegrationsettingsservice.cs`
