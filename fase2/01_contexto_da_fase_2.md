# contexto da fase 2

## estado atual

As seguintes etapas já foram concluídas:

- bloco 1: scaffold da solução e sistema de design
- bloco 2: shell, layout e navegação
- bloco 3: home / catálogo de cursos
- bloco 4: página interna do curso + navegação por módulos/tópicos/aulas
- bloco 5: área do player + estados de progresso
- bloco 6: dashboard de progresso
- bloco 7: roadmap
- bloco 8: materiais complementares
- bloco 9: refinamento visual final e consistência

O aplicativo já possui uma base funcional e visualmente representativa do produto.

## objetivo desta fase

Agora o foco não é mais apenas estrutura visual e navegação.

O foco desta fase é preparar o aplicativo para começar a sair do mock e caminhar para o funcionamento real, com especial atenção para:

- fluxo de criação de cursos
- curso local por pasta
- curso montado por curadoria YouTube
- gerenciamento de chaves de API
- contratos de IA
- estruturação correta de requests e responses
- organização da camada de orquestração de IA
- persistência futura dos dados gerados por curso

## princípio central desta fase

O curso é a entidade central do sistema.

Toda informação gerada ou enriquecida por IA deve ser produzida dentro do contexto de um curso específico.

Isso vale para:

- título do curso
- descrição do curso
- nomes refinados de módulos
- nomes refinados de aulas
- roadmap
- textos auxiliares das páginas
- materiais complementares
- recomendações de estudo

## problema que estamos evitando

Não queremos um sistema que envia uma solicitação gigante para a IA pedindo tudo de uma vez.

Não queremos:

- uma chamada monolítica
- respostas grandes demais
- parsing frágil
- desperdício de tokens
- desperdício de custo
- acoplamento exagerado entre tela e modelo de IA

Queremos um sistema modular, econômico e confiável.
