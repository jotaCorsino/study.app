# decisões fechadas

## 1. origem da estrutura do curso

A estrutura base do curso **não deve ser criada pela IA**.

A estrutura base do curso deve vir primeiro de forma determinística.

### curso local
A estrutura base deve ser criada a partir do sistema de arquivos:

- pasta raiz do curso
- subpastas
- nomes dos arquivos de vídeo
- caminhos relativos
- contagens
- duração quando disponível

### curso online / curadoria
A estrutura base deve ser criada a partir da seleção e agrupamento dos vídeos do YouTube, com lógica de curadoria e organização pelo aplicativo.

## 2. papel da IA

A IA entra **depois** para enriquecer a estrutura já detectada.

A IA pode:

- gerar título amigável do curso
- gerar descrição do curso
- refinar nomes de módulos
- refinar nomes de aulas
- gerar roadmap detalhado
- gerar textos auxiliares
- sugerir foco e progressão
- ajudar na curadoria complementar

A IA não deve apagar nem substituir a estrutura bruta detectada.

## 3. preservação de dados brutos e dados refinados

O sistema deve manter separados:

- `rawTitle`
- `rawPath`
- `rawFileName`

e também:

- `displayTitle`
- `displayDescription`
- `refinedLabels`

A estrutura bruta detectada é a fonte primária.
A estrutura refinada é uma camada de apresentação e enriquecimento.

## 4. chamadas de IA devem ser separadas

Cada necessidade deve ter sua própria chamada.

Não usar uma única chamada para:

- estruturar curso
- gerar título
- gerar descrição
- gerar roadmap
- gerar materiais complementares
- gerar tudo ao mesmo tempo

Essas responsabilidades devem ser separadas.

## 5. divisão entre provedores

### Gemini
Deve ser usado para tarefas mais importantes semanticamente, onde qualidade textual e coerência geral são mais relevantes.

Exemplos:
- título e descrição do curso
- roadmap detalhado
- textos centrais da experiência
- sugestões de progressão
- explicações mais nobres

### DeepSeek
Pode ser usado para tarefas mais simples, auxiliares ou mais baratas.

Exemplos:
- normalização de títulos
- limpeza de nomes de arquivo
- resumos curtos
- transformações textuais leves
- fallback barato para tarefas não críticas

## 6. chaves de API

Quando a persistência real for implementada:

- chaves sensíveis devem ficar em `SecureStorage`
- preferências não sensíveis podem ficar em `Preferences`
- dados persistentes do curso, roadmap e materiais complementares devem ficar no banco local

As chaves não devem ser salvas em texto simples no banco nem em Preferences.

## 7. curso deve ser persistente como entidade viva

Depois de criado, cada curso deve manter:

- estrutura detectada
- progresso
- roadmap
- materiais complementares
- dados enriquecidos por IA
- histórico de geração
- estado geral do curso

Fechar e reabrir o aplicativo não pode apagar esse ecossistema do curso.
