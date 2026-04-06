# contratos de ia e json

## regra geral

Toda integração com IA deve usar JSON estruturado.

Não usar texto livre esperando parsing informal.

Cada operação deve ter seu próprio contrato de request e response.

## regra para Gemini

Para operações com Gemini, usar saída estruturada real com schema explícito.

A integração deve ser pensada para trabalhar com JSON válido e previsível.

## contrato 1 — estrutura detectada do curso

Este contrato é produzido pelo aplicativo, não pela IA.
Ele representa a base estrutural do curso.

```json
{
  "sourceType": "LocalFolder",
  "rawCourseTitle": "curso c# avançado",
  "rootPath": "C:/Cursos/Curso C# Avançado",
  "detectedModules": [
    {
      "rawTitle": "01 - LINQ",
      "rawPath": "01 - LINQ",
      "lessons": [
        {
          "rawTitle": "01 - Introducao ao LINQ.mp4",
          "rawFileName": "01 - Introducao ao LINQ.mp4",
          "relativePath": "01 - LINQ/01 - Introducao ao LINQ.mp4",
          "durationSeconds": 0
        }
      ]
    }
  ]
}
```

## contrato 2 — apresentação do curso

### request

```json
{
  "courseContext": {
    "sourceType": "LocalFolder",
    "rawCourseTitle": "curso c# avançado",
    "moduleCount": 6,
    "lessonCount": 42,
    "detectedModules": [
      {
        "rawTitle": "01 - LINQ",
        "lessonCount": 9
      }
    ]
  },
  "goal": "Organizar este curso em uma experiência clara e amigável dentro de um app pessoal de estudos."
}
```

### response

```json
{
  "courseTitle": "C# Avançado",
  "courseDescription": "Curso focado em aprofundamento prático de C# com ênfase em recursos avançados da linguagem e organização do código.",
  "displayModules": [
    {
      "rawTitle": "01 - LINQ",
      "displayTitle": "LINQ"
    }
  ]
}
```

## contrato 3 — roadmap

### request

```json
{
  "courseInformation": {
    "title": "C# Avançado",
    "description": "Curso focado em aprofundamento prático...",
    "durationMinutes": 2520,
    "moduleCount": 6,
    "lessonCount": 42,
    "modules": [
      {
        "title": "LINQ",
        "topicCount": 3,
        "lessonCount": 9
      }
    ]
  },
  "goal": "Criar um mapa de evolução de estudos dividido em 6 sprints essenciais focando em prática."
}
```

### response

```json
{
  "roadmap": [
    {
      "order": 1,
      "title": "Fundamentos Sólidos",
      "description": "Consolidação dos conceitos necessários para o restante do curso.",
      "skills": ["Sintaxe avançada", "LINQ básico"],
      "estimatedDurationHours": 8,
      "moduleReferences": ["LINQ"]
    }
  ]
}
```

## contrato 4 — materiais complementares

### request

```json
{
  "courseInformation": {
    "title": "C# Avançado",
    "description": "Curso focado em aprofundamento prático...",
    "modules": ["LINQ", "Delegates", "Async/Await"]
  },
  "curationGoal": "Encontrar materiais gratuitos complementares e relevantes para aprofundamento."
}
```

### response

```json
{
  "recommendedSearchQueries": [
    "C# LINQ curso completo",
    "C# async await tutorial",
    "C# advanced playlist"
  ],
  "curationNotes": [
    "Priorizar playlists completas",
    "Evitar vídeos muito rasos"
  ]
}
```

## contratos recomendados na camada de domínio

Criar contratos separados, por exemplo:

- `CourseStructureContracts.cs`
- `CoursePresentationContracts.cs`
- `CourseRoadmapContracts.cs`
- `CourseSupplementaryMaterialsContracts.cs`

## regra importante de implementação

Os contratos devem ser pequenos, específicos e independentes.

O app deve poder chamar apenas uma operação por vez, por exemplo:

1. detectar estrutura
2. gerar apresentação do curso
3. gerar roadmap
4. gerar materiais complementares

## observação sobre nomes internos

Mesmo quando existir `displayTitle`, o sistema deve manter o `rawTitle`.

Isso é obrigatório para:

- rastreabilidade
- debugging
- reindexação
- comparação futura
- reprocessamento
