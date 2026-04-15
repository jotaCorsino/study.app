# Base atual para importacao de cursos externos

## Objetivo desta etapa

Registrar o que ja foi preparado no StudyHub para receber cursos externos em JSON versionado sem quebrar o fluxo atual dos cursos locais.

## Ja preparado para a nova arquitetura

### Dominio

- `CourseSourceType.ExternalImport` agora existe como terceiro tipo de origem.
- `CourseSourceMetadata` agora aceita metadados de importacao externa:
  - sistema externo
  - `externalCourseId`
  - `externalCourseSlug`
  - ids de disciplinas externas
  - fingerprint do payload
  - versao do schema
  - tipo de origem do import

### Parser e importador

- existe um parser dedicado para payload externo versionado:
  - aceita apenas `schemaVersion` `1.x.x`
  - valida campos minimos obrigatorios
  - ignora campos extras sem quebrar o parse
  - gera fingerprint deterministico do payload
- existe um importador dedicado para JSON externo:
  - constroi um `Course` do dominio com `SourceType = ExternalImport`
  - preserva o fluxo atual de persistencia do app
  - reaproveita `CoursePersistenceHelper.UpsertCourseAsync`
  - mantem o progresso salvo quando os ids internos permanecem estaveis

### Estabilidade de ids

- ids internos do curso, das disciplinas internalizadas, das aulas e das avaliacoes agora podem ser derivados de chaves externas estaveis.
- isso reduz risco de perda de progresso em reimportacoes e futuras migracoes.

### Persistencia nova

- `external_course_imports`
  guarda o payload bruto, provider, sistema, schema e fingerprint por `CourseId`
- `external_assessments`
  guarda avaliacoes externas em tabela dedicada, sem depender da UI atual

### Compatibilidade com o progresso atual

- a preservacao de progresso continua baseada em `LessonId`.
- ao reimportar o mesmo curso externo, o importador usa ids deterministas e o helper de persistencia mantem:
  - `lessons.Status`
  - `lessons.WatchedPercentage`
  - `lessons.LastPlaybackPositionSeconds`
  - `courses.CurrentLessonId` quando a aula ainda existir

### Preparacao para agenda de estudos

- o bloco `assessments` do JSON agora tem persistencia dedicada.
- isso deixa a base pronta para futuras telas/servicos de agenda, calendario e alertas sem reabrir o contrato de importacao.

### Servicos adjacentes

- roadmap e materiais complementares agora diferenciam `ExternalImport` de `LocalFolder`.
- manutencao de apresentacao/refinamento nao tenta mais tratar `ExternalImport` como curso local.

## O que continua legado por enquanto

### Entrada do fluxo

- a UI atual continua oferecendo apenas:
  - importacao local por pasta
  - curadoria online interna
- ainda nao existe tela, acao ou wizard para subir um JSON externo pelo app.

### Runtime de aulas

- o player atual continua suportando apenas:
  - `LessonSourceType.LocalFile`
  - `LessonSourceType.ExternalVideo`
- itens externos sem fonte reproduzivel continuam preservados apenas no payload bruto e ficam fora da estrutura reproduzivel atual.

### Enriquecimento e manutencao

- o pipeline de enriquecimento local continua legado e focado em `LocalFolder`.
- `ExternalImport` ainda nao tem:
  - refinamento textual dedicado
  - regeneracao de apresentacao dedicada
  - rotina operacional especifica como a existente para `OnlineCurated`

### UI e leitura de avaliacoes

- as avaliacoes externas ja sao persistidas, mas ainda nao possuem:
  - pagina/listagem no app
  - timeline de agenda
  - lembretes
  - cruzamento com rotina diaria

## Decisao de seguranca desta etapa

Para nao quebrar o runtime atual:

- o importador so internaliza como aulas ativas itens que o app ja sabe reproduzir hoje
- o payload bruto completo continua salvo em storage dedicado
- avaliacoes externas ja ficam preservadas para a proxima fase da arquitetura

## Arquivos centrais desta base

- `src/studyhub-web/src/studyhub.domain/Entities/coursesource.cs`
- `src/studyhub-web/src/studyhub.application/Contracts/ExternalImport/`
- `src/studyhub-web/src/studyhub.application/Interfaces/iexternalcoursejsonparser.cs`
- `src/studyhub-web/src/studyhub.application/Interfaces/iexternalcourseimportservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/externalcoursejsonparser.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/externalcourseimportservice.cs`
- `src/studyhub-web/src/studyhub.infrastructure/persistence/studyhubdbcontext.cs`
- `src/studyhub-web/src/studyhub.infrastructure/persistence/studyhubdatabaseinitializer.cs`
