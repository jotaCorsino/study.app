# contexto de ia e contratos

## status atual
As integrações reais com IA ainda não começam nesta fase, mas a persistência deve ser preparada para recebê-las depois.

## principio central
O curso é a entidade central de contexto. Toda geração textual futura deve ser vinculada a um curso específico.

## regra estrutural
A estrutura base do curso não deve depender da IA:
- curso local: estrutura vem do sistema de arquivos
- curso online: estrutura vem da curadoria e agrupamento inicial

A IA entra depois para enriquecer:
- título amigável do curso
- descrição do curso
- títulos refinados de módulos e aulas
- roadmap
- materiais complementares sugeridos
- textos auxiliares

## providers previstos
- Gemini: tarefas semânticas mais importantes
- DeepSeek: tarefas textuais mais simples ou mais baratas
- Google/YouTube: descoberta e curadoria de vídeos

## contratos já existentes no projeto
Há uma base importante em `studyhub.domain/AIContracts`, especialmente para a hierarquia do roadmap.

## orientação para a fase 4
Mesmo sem chamar IA real agora, preserve espaço de persistência para:
- textos derivados por curso
- roadmap estruturado por curso
- materiais complementares vinculados ao curso
- metadados de origem dos conteúdos

## orientação futura para Gemini
Quando chegar a fase de integração real, preferir saída estruturada em JSON com schema explícito, não texto livre. A API Gemini suporta `application/json` com `response_json_schema`. ([ai.google.dev](https://ai.google.dev/gemini-api/docs/structured-output))

## lógica futura da curadoria
A lógica já definida para materiais complementares considera:
- aderência ao tema
- autoridade do canal
- existência de playlist
- completude percebida
- score final de curadoria

Essa lógica deve ser preservada quando a integração real chegar.
