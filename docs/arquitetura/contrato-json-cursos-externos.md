# Contrato JSON para cursos externos

## Objetivo

Definir um contrato JSON versionado para importar cursos externos no StudyHub sem acoplar o dominio ao formato atual de uma unica integracao.

Este contrato precisa:

- suportar evolucao por versao
- distinguir origem local vs origem externa
- permitir import de um curso completo ou de um pacote com varias disciplinas
- preservar ids externos estaveis
- aceitar campos extras sem quebrar consumidores futuros

## Escopo

Este documento propoe o **contrato canonico de importacao**.

Ele nao implementa:

- UI de importacao
- parser real
- validacao em runtime
- persistencia nova

## Principios de compatibilidade

### 1. Versionamento

- `schemaVersion` deve ser uma string em formato semver, por exemplo: `"1.0.0"`
- mudanca de `major` indica quebra de compatibilidade
- mudanca de `minor` indica extensao aditiva
- mudanca de `patch` indica ajuste editorial, validacao ou clarificacao sem quebrar payloads validos

### 2. Evolucao segura

- consumidores devem ignorar campos desconhecidos
- produtores nao devem remover campos obrigatorios dentro da mesma `major`
- arrays devem preferir `[]` em vez de `null`
- objetos opcionais devem ser omitidos ou enviados como objeto vazio, nao com estrutura ambigua

### 3. Metadados livres

Para manter o schema evoluivel sem explodir o numero de campos canonicos, os objetos principais devem aceitar:

- `metadata`

Esse objeto e reservado para dados especificos do provedor externo.

## Proposta de dominio

## Estado atual

Hoje o dominio diferencia cursos por:

- `CourseSourceType.LocalFolder`
- `CourseSourceType.OnlineCurated`

Isso ainda nao separa corretamente um curso vindo de **JSON externo importado**.

## Proposta

Adicionar um terceiro tipo de origem no dominio:

```csharp
public enum CourseSourceType
{
    LocalFolder = 0,
    OnlineCurated = 1,
    ExternalImport = 2
}
```

### Motivacao

`ExternalImport` e semanticamente diferente de:

- `LocalFolder`: estrutura nasce do disco local
- `OnlineCurated`: estrutura nasce de curadoria interna do StudyHub

Ja o contrato aqui proposto nasce de uma **fonte externa serializada**, com ids, status e semantica proprios.

## Observacao sobre aulas

O dominio atual de aula diferencia:

- `LessonSourceType.LocalFile`
- `LessonSourceType.ExternalVideo`

Para o schema v1, a recomendacao e:

- quando existir URL reproduzivel, mapear para `ExternalVideo`
- quando existir apenas referencia academica sem URL reproduzivel, manter o dado no contrato e tratar isso como necessidade futura de extensao do dominio

Evolucao recomendada para fase futura:

```csharp
public enum LessonSourceType
{
    LocalFile = 0,
    ExternalVideo = 1,
    ExternalReference = 2
}
```

Nao e necessario implementar isso agora para o contrato existir.

## Estrutura canonica do payload

```json
{
  "schemaVersion": "1.0.0",
  "source": {},
  "course": {},
  "disciplines": []
}
```

## Semantica dos blocos

- `schemaVersion`
  versao do contrato StudyHub
- `source`
  descreve quem produziu o arquivo e de onde ele veio
- `course`
  descreve o curso importavel como agregado StudyHub
- `disciplines`
  lista de disciplinas ou trilhas academicas contidas no pacote

## Regras de cardinalidade

- `schemaVersion`: obrigatorio
- `source`: obrigatorio
- `course`: obrigatorio
- `disciplines`: obrigatorio, com pelo menos 1 item

## Schema v1 proposto

## 1. Envelope

```json
{
  "schemaVersion": "1.0.0",
  "source": {
    "kind": "external-platform-export",
    "system": "studyhub-sync",
    "provider": "univirtus",
    "providerVersion": "1.0.0",
    "exportedAt": "2026-04-15T18:00:00Z",
    "originUrl": "https://ava.univirtus.com.br/...",
    "locale": "pt-BR",
    "pageType": "discipline-detail",
    "metadata": {}
  },
  "course": {
    "externalId": "univirtus:course:123456",
    "slug": "banco-de-dados-i",
    "title": "Banco de Dados I",
    "description": "Curso importado de fonte externa.",
    "sourceType": "external-import",
    "category": "Curso Externo",
    "language": "pt-BR",
    "thumbnailUrl": null,
    "coverImageUrl": null,
    "provider": "univirtus",
    "tags": ["univirtus", "externo"],
    "metadata": {}
  },
  "disciplines": [
    {
      "externalId": "univirtus:discipline:123456",
      "code": "123456",
      "title": "Banco de Dados I",
      "description": "Disciplina do AVA.",
      "status": "in-progress",
      "period": {
        "label": "2026/1",
        "startAt": null,
        "endAt": null
      },
      "modules": [
        {
          "externalId": "univirtus:discipline:123456:module:1",
          "order": 1,
          "title": "Aula 1 - Modelo Relacional",
          "description": "",
          "lessons": [
            {
              "externalId": "univirtus:discipline:123456:lesson:1",
              "order": 1,
              "title": "Videoaula introdutoria",
              "description": "",
              "type": "video",
              "status": "completed",
              "durationSeconds": 1800,
              "progress": {
                "watchedPercentage": 100,
                "lastPositionSeconds": 1800,
                "completedAt": null
              },
              "source": {
                "kind": "external-video",
                "provider": "univirtus",
                "url": null,
                "filePath": null,
                "externalRef": "conteudo-1"
              },
              "metadata": {}
            }
          ],
          "metadata": {}
        }
      ],
      "assessments": [
        {
          "externalId": "univirtus:discipline:123456:assessment:apol-1",
          "type": "apol",
          "title": "APOL 1",
          "description": "",
          "status": "scheduled",
          "weightPercentage": 20,
          "availability": {
            "startAt": "2026-04-20T00:00:00Z",
            "endAt": "2026-04-27T23:59:59Z"
          },
          "grade": null,
          "metadata": {}
        }
      ],
      "metadata": {}
    }
  ]
}
```

## 2. Campos canonicos

### `schemaVersion`

Tipo:

- `string`

Regra:

- obrigatorio
- semver textual

Exemplo:

- `"1.0.0"`

### `source`

Campos propostos:

- `kind`
- `system`
- `provider`
- `providerVersion`
- `exportedAt`
- `originUrl`
- `locale`
- `pageType`
- `metadata`

#### Semantica

- `kind`
  tipo de origem do arquivo
  Exemplo: `external-platform-export`
- `system`
  sistema/exportador que gerou o JSON
  Exemplo: `studyhub-sync`
- `provider`
  plataforma academica ou provedor externo
  Exemplo: `univirtus`
- `providerVersion`
  versao do exportador
- `exportedAt`
  timestamp ISO-8601 UTC
- `originUrl`
  URL da pagina original quando existir
- `locale`
  cultura do dado de origem
- `pageType`
  origem funcional da captura
  Exemplo: `catalog`, `discipline-detail`, `assessments`

### `course`

Campos propostos:

- `externalId`
- `slug`
- `title`
- `description`
- `sourceType`
- `category`
- `language`
- `thumbnailUrl`
- `coverImageUrl`
- `provider`
- `tags`
- `metadata`

#### Semantica

- `externalId`
  id estavel na origem
- `slug`
  identificador textual amigavel
- `sourceType`
  para v1 deste contrato, usar `external-import`

### `disciplines`

Lista de disciplinas importadas no pacote.

Cada disciplina contem:

- `externalId`
- `code`
- `title`
- `description`
- `status`
- `period`
- `modules`
- `assessments`
- `metadata`

#### Status sugeridos

- `not-started`
- `in-progress`
- `completed`
- `archived`
- `unknown`

### `modules`

Cada modulo contem:

- `externalId`
- `order`
- `title`
- `description`
- `lessons`
- `metadata`

### `lessons`

Cada aula contem:

- `externalId`
- `order`
- `title`
- `description`
- `type`
- `status`
- `durationSeconds`
- `progress`
- `source`
- `metadata`

#### Tipos sugeridos de aula

- `video`
- `reading`
- `exercise`
- `live-session`
- `reference`
- `other`

#### Status sugeridos de aula

- `not-started`
- `in-progress`
- `completed`
- `blocked`
- `unknown`

#### Objeto `progress`

Campos:

- `watchedPercentage`
- `lastPositionSeconds`
- `completedAt`

Observacoes:

- `watchedPercentage` deve ser `0..100`
- `lastPositionSeconds` deve ser inteiro >= 0
- `completedAt` e opcional

#### Objeto `source` da aula

Campos:

- `kind`
- `provider`
- `url`
- `filePath`
- `externalRef`

Valores sugeridos para `kind`:

- `local-file`
- `external-video`
- `external-reference`

Regra:

- pelo menos um entre `url`, `filePath` ou `externalRef` deve existir

### `assessments`

Cada avaliacao contem:

- `externalId`
- `type`
- `title`
- `description`
- `status`
- `weightPercentage`
- `availability`
- `grade`
- `metadata`

#### Tipos sugeridos

- `apol`
- `exam`
- `assignment`
- `quiz`
- `project`
- `other`

#### Status sugeridos

- `scheduled`
- `open`
- `closed`
- `completed`
- `graded`
- `unknown`

#### Objeto `availability`

Campos:

- `startAt`
- `endAt`

Formato:

- timestamp ISO-8601 UTC

## Regras de mapeamento para o dominio do StudyHub

## Curso

### Quando o import virar um curso StudyHub

Mapeamento recomendado:

- `course.title` -> `Course.Title`
- `course.description` -> `Course.Description`
- `course.category` -> `Course.Category`
- `course.sourceType = "external-import"` -> `CourseSourceType.ExternalImport`

### Metadados recomendados em `CourseSourceMetadata`

Sem quebrar o shape atual, o import externo pode popular:

- `Provider` com `source.provider`
- `ImportedAt` com `source.exportedAt`
- `ScanVersion` com `schemaVersion`
- `SourceUrls` com URLs relevantes encontradas

Evolucao recomendada futura para o metadata:

- `ExternalSystem`
- `ExternalCourseId`
- `ExternalDisciplineIds`
- `ImportPayloadFingerprint`

## Modulos e aulas

### Modulos

Cada item de `disciplines[].modules[]` pode virar:

- um `Module` por modulo externo

### Aulas

Cada item de `lessons[]` pode virar:

- `LessonSourceType.ExternalVideo` quando houver `source.url`
- `LessonSourceType.LocalFile` apenas se o payload vier de fato com `source.filePath`
- aulas sem URL reproduzivel devem ser preservadas no contrato mesmo que o runtime atual ainda nao trate playback delas

## Avaliacoes

O dominio atual do curso nao tem uma entidade persistida propria para `assessments`.

Para o schema v1, a recomendacao e:

- manter `assessments` no contrato
- persistir esse bloco futuramente em storage dedicado ou em metadados especificos
- nao perder o bloco no import, mesmo que a primeira implementacao ainda nao o use na UI

## Estrategia de identificacao

## Regra geral

Todo objeto importavel deve ter `externalId` opaco e estavel.

Exemplos:

- curso: `univirtus:course:123456`
- disciplina: `univirtus:discipline:123456`
- modulo: `univirtus:discipline:123456:module:1`
- aula: `univirtus:discipline:123456:lesson:1`
- avaliacao: `univirtus:discipline:123456:assessment:apol-1`

## Recomendacao para geracao de ids internos

O importer do StudyHub deve evitar derivar ids internos apenas do titulo visivel.

Prioridade recomendada:

1. `externalId`
2. `code` ou referencia estavel do provedor
3. fallback deterministico composto por origem + caminho academico + ordem

Isso reduz risco de perda de progresso em migracoes futuras.

## Regras de validacao minima para v1

Um payload v1 deve ser considerado valido quando:

- `schemaVersion` existir
- `source.provider` existir
- `course.externalId` existir
- `course.title` existir
- `disciplines` tiver ao menos 1 item
- cada disciplina tiver `externalId` e `title`
- cada modulo tiver `externalId`, `order` e `title`
- cada aula tiver `externalId`, `order` e `title`

`assessments` pode ser vazio.

## Compatibilidade futura

## Mudancas aditivas permitidas em `minor`

Exemplos:

- adicionar `instructors`
- adicionar `attachments`
- adicionar `resources`
- adicionar `disciplineGroup`
- adicionar novos tipos de `lesson.type`
- adicionar novos tipos de `assessment.type`

## Mudancas que exigem `major`

Exemplos:

- trocar nome de campo obrigatorio
- mudar semantica de `externalId`
- substituir `disciplines[]` por outra raiz
- mudar formato de data de ISO-8601 para outro padrao

## Recomendacao de extensibilidade

Para evitar quebra:

- manter `metadata` em `source`, `course`, `discipline`, `module`, `lesson` e `assessment`
- tratar enums textuais como conjuntos abertos
- ignorar campos desconhecidos no import

## Forma resumida do contrato

```json
{
  "schemaVersion": "1.0.0",
  "source": {
    "kind": "external-platform-export",
    "system": "studyhub-sync",
    "provider": "univirtus",
    "providerVersion": "1.0.0",
    "exportedAt": "2026-04-15T18:00:00Z",
    "originUrl": "https://...",
    "pageType": "discipline-detail",
    "metadata": {}
  },
  "course": {
    "externalId": "provider:course:abc",
    "title": "Curso externo",
    "description": "",
    "sourceType": "external-import",
    "category": "Curso Externo",
    "provider": "univirtus",
    "metadata": {}
  },
  "disciplines": [
    {
      "externalId": "provider:discipline:abc",
      "title": "Disciplina",
      "modules": [],
      "assessments": [],
      "metadata": {}
    }
  ]
}
```

## Decisoes recomendadas para a primeira implementacao futura

- aceitar apenas `schemaVersion` `1.x.x`
- tratar `course` como agregado StudyHub e `disciplines` como conteudo importado
- introduzir `CourseSourceType.ExternalImport`
- preservar `externalId` original em metadados
- importar `assessments` desde o contrato, mesmo que a UI inicial ainda nao exiba esse bloco

## Referencias internas

- `src/studyhub-web/src/studyhub.domain/Entities/coursesource.cs`
- `src/studyhub-web/src/studyhub.domain/Entities/Course.cs`
- `src/studyhub-web/src/studyhub.domain/Entities/Lesson.cs`
- `src/studyhub-web/src/studyhub.infrastructure/services/onlinecuratedcoursebuilder.cs`
- `src/studyhub-extension/studyhub-sync/content.js`
