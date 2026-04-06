# plano de implementação fase 2

## objetivo desta fase

Transformar a base visual já construída em uma base arquitetural pronta para o funcionamento real do aplicativo, com foco em:

- criação de cursos
- preparação de integrações
- contratos de IA
- reestruturação do fluxo de navegação
- persistência futura orientada por curso

## ajuste no plano anterior

Os open questions já estão resolvidos por decisão de produto.

### decisão 1 — seleção de pastas
Ao importar um curso local, o aplicativo deve procurar todos os vídeos automaticamente e construir a árvore base do curso a partir da hierarquia real de pastas e arquivos.

A IA entra depois apenas para enriquecer essa estrutura.

### decisão 2 — chaves de API
Quando a persistência real for implementada, usar SecureStorage para as chaves de API.

## implementação recomendada nesta fase

### bloco A — fluxo de criação de curso
Criar ou ajustar:
- `AddCourseDialog.razor` ou equivalente
- via local
- via curadoria inteligente

Preparar UX para:
- selecionar pasta
- digitar tema do curso por curadoria
- visualizar tipo de criação

### bloco B — settings e gerenciamento de credenciais
Criar:
- `Settings.razor`
- campos de YouTube API
- Gemini API
- DeepSeek API

Nesta fase, pode permanecer mockado ou sem persistência real, mas a arquitetura já deve separar claramente credenciais sensíveis de preferências gerais.

### bloco C — contratos de domínio
Criar contratos reais na camada de domínio ou application para:

- estrutura do curso
- apresentação do curso
- roadmap
- materiais complementares

### bloco D — camada de orquestração de IA
Criar a arquitetura e interfaces, mesmo que parcialmente mockadas:

- `IGenerativeProvider`
- `GeminiProvider`
- `DeepSeekProvider`
- `IAOrchestrator`
- serviços de aplicação por operação

### bloco E — reestruturação do curso como entidade central
Ajustar modelos e fluxo para que:

- dashboard seja interno ao curso
- roadmap seja interno ao curso
- materiais complementares sejam internos ao curso
- tudo seja tratado como dados associados a um curso específico

### bloco F — curadoria preparada para realidade
Transformar a curadoria do YouTube em arquitetura preparada para:
- busca
- scoring
- persistência
- reuso por curso

## o que não fazer nesta fase

- não implementar chamada monolítica de IA
- não acoplar tela diretamente à API
- não usar texto livre como contrato principal
- não depender da IA para criar a estrutura base do curso
- não apagar dados brutos detectados do curso

## resultado esperado ao final

Ao final desta fase, o projeto deve estar pronto para evoluir de mock para real com segurança, mantendo:

- curso como centro do sistema
- IA modular por operação
- persistência futura bem definida
- fluxo de criação de cursos bem encaminhado
- providers preparados
- contratos claros
