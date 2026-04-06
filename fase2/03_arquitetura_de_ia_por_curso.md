# arquitetura de ia por curso

## objetivo

A camada de IA do StudyHub deve ser orientada por curso e por operação.

Cada operação deve ter:

- um contexto claro de entrada
- um contrato de request
- um contrato de response
- um provedor definido
- um resultado persistível

## fluxo macro

### etapa 1 — ingestão estrutural
O app detecta ou monta a estrutura base do curso.

#### local
- lê pasta raiz
- lê subpastas
- detecta vídeos
- cria árvore inicial

#### online
- executa curadoria de vídeos
- agrupa itens
- cria árvore inicial

### etapa 2 — normalização básica
O app ou um provider barato limpa dados estruturais.

Exemplos:
- remover prefixos técnicos
- limpar números duplicados
- converter títulos brutos em labels mais legíveis
- identificar contagens e durações

### etapa 3 — enriquecimento de apresentação
A IA gera textos e labels centrais para o curso.

Exemplos:
- título amigável
- descrição
- títulos de módulos refinados
- títulos de aulas refinados
- resumo geral

### etapa 4 — roadmap
A IA gera roadmap detalhado com base no curso já entendido.

### etapa 5 — curadoria complementar
O sistema sugere materiais extras e persistentes para aquele curso.

## regra de orquestração

Crie uma camada desacoplada, por exemplo:

- `IAOrchestrator`
- `IGenerativeProvider`
- `GeminiProvider`
- `DeepSeekProvider`

E serviços de aplicação como:

- `CourseEnrichmentService`
- `CourseRoadmapService`
- `CourseCurationService`
- `CoursePresentationService`

## regra de persistência por operação

Cada operação deve poder ser salva separadamente.

Exemplo:

- `CoursePresentationData`
- `CourseRoadmapData`
- `CourseSupplementaryMaterialsData`

Assim o app pode:

- regenerar só uma parte
- reusar resultados
- evitar chamadas desnecessárias
- manter custo sob controle

## regra de UI

As telas não devem chamar providers diretamente.

A UI conversa com serviços de aplicação.
Os serviços chamam o orquestrador.
O orquestrador escolhe o provider e o contrato.

## regra de falha

Se uma operação falhar:

- o curso continua existindo
- a estrutura base continua válida
- o restante do curso não quebra
- o usuário pode tentar regenerar apenas aquela parte
