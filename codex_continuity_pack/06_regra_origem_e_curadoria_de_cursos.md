# Regra de Domínio — Origem e Curadoria de Cursos

## objetivo

Esta regra define como o StudyHub deve tratar a origem dos cursos e como a curadoria de vídeos online deve funcionar.

O objetivo é garantir que, independentemente da origem, todo curso dentro do app tenha a mesma estrutura principal de experiência:

- curso
- módulos
- tópicos
- aulas
- ordem sequencial
- progresso
- continuidade
- roadmap associado
- materiais complementares associados

A origem muda a fonte dos dados, mas não muda a experiência principal do produto.

---

## tipos oficiais de origem de curso

Todo curso no StudyHub deve possuir uma origem explícita.

### 1. LocalFolder

Curso criado a partir de uma pasta local no computador.

#### regra

- a estrutura base do curso vem do sistema de arquivos
- a pasta raiz representa o curso
- subpastas representam módulos e/ou tópicos
- arquivos de vídeo representam aulas
- a hierarquia base deve ser construída de forma determinística pelo app
- a IA não deve criar a estrutura primária do curso local
- a IA pode enriquecer a estrutura depois

### 2. OnlineCurated

Curso criado sem upload de pasta local, a partir de vídeos gratuitos online, principalmente YouTube.

#### regra

- o curso deve ser montado a partir de vídeos e playlists gratuitos
- esses vídeos devem ser organizados como uma grade real de curso
- o resultado final precisa ser equivalente à experiência de um curso local
- a origem online não pode virar apenas uma lista solta de links

---

## regra principal de paridade

Independentemente da origem, todo curso deve ser transformado internamente em uma estrutura padronizada.

Isso significa que:

- um curso vindo do PC
- e um curso montado a partir do YouTube

devem ser consumidos pela aplicação com o mesmo modelo principal de navegação e progresso.

A diferença deve existir na fonte e nos metadados de origem, não na experiência principal do usuário.

---

## regra para cursos online

Quando um curso não for criado a partir de pasta local, ele deve ser montado a partir de vídeos gratuitos online.

No caso do YouTube, isso significa:

- selecionar vídeos e playlists relevantes
- organizar o conteúdo em sequência lógica
- transformar isso em módulos, tópicos e aulas dentro do app
- permitir progresso e continuidade como em qualquer outro curso

### observação importante

O curso online não é material complementar.
Ele é o curso principal naquele contexto.

---

## separação entre curso principal e material complementar

O domínio do projeto precisa distinguir duas coisas diferentes:

### A. criação de curso principal

Usado quando o usuário está criando um curso no app.

Pode ser:

- LocalFolder
- OnlineCurated

### B. materiais complementares

Usado para enriquecer um curso já existente.

Exemplos:

- vídeos extras
- playlists adicionais
- leituras
- links úteis

### regra

Nunca confundir:

- builder de curso principal
  com
- gerador de material complementar

---

## regras da curadoria de vídeos online

A curadoria dos vídeos online deve seguir regras consistentes de qualidade e aderência ao curso.

### critérios mínimos obrigatórios

#### 1. aderência ao tema do curso

O canal, playlist ou vídeo precisa estar claramente relacionado ao tema do curso.

#### 2. relevância do canal

O tamanho e a autoridade do canal devem ser considerados.
O sistema deve levar em conta o peso do canal como sinal de confiança e profundidade.

#### 3. uso de playlist quando fizer sentido

Se existir playlist relevante, ela deve ser fortemente considerada.
A playlist não deve ser exibida apenas como um link bruto.
Ela deve ser quebrada e organizada vídeo por vídeo dentro do app.

#### 4. montagem da grade do curso

Os vídeos curados devem ser reorganizados dentro do StudyHub para formar uma sequência lógica de estudo.

#### 5. combinação de múltiplas fontes

Pode acontecer de playlists ou vídeos de canais diferentes se complementarem.

Isso é permitido e desejável quando fizer sentido pedagógico.

Nesse caso:

- o app pode montar um curso com várias fontes diferentes
- essas fontes devem ser organizadas em sequência lógica
- a progressão do curso deve ser respeitada
- o resultado final deve parecer um curso coeso e não uma coleção desorganizada

---

## regra de organização pedagógica

O curso online deve respeitar progressão lógica.

Isso significa que os vídeos devem ser organizados considerando:

- nível de entrada
- sequência de complexidade
- base antes de aprofundamento
- agrupamento por temas
- continuidade entre blocos

O app deve preferir organização por progressão de aprendizagem, e não apenas ordem cronológica ou ordem original da playlist.

---

## responsabilidades da IA na origem OnlineCurated

A IA pode participar da montagem do curso online.

### responsabilidades permitidas

- analisar o tema desejado
- sugerir queries de busca
- avaliar aderência semântica dos vídeos e playlists
- ajudar a ordenar módulos e aulas
- gerar títulos de apresentação
- gerar descrição do curso
- gerar roadmap
- gerar textos auxiliares

### responsabilidades não permitidas

- inventar conteúdo sem base nas fontes selecionadas
- ignorar a curadoria real dos vídeos
- montar uma estrutura arbitrária sem relação com os materiais encontrados

---

## regra de construção do curso local

No fluxo LocalFolder, a IA não é responsável por construir a estrutura base.

A sequência correta é:

1. o app lê a pasta local
2. detecta módulos, tópicos e vídeos
3. cria a estrutura base do curso
4. só depois a IA pode enriquecer o curso

Exemplos de enriquecimento:

- título amigável
- descrição
- refinamento de nomes
- roadmap
- textos auxiliares

---

## regra de construção do curso online

No fluxo OnlineCurated, a sequência correta é:

1. o app recebe o objetivo do curso
2. a camada de curadoria encontra vídeos e playlists candidatos
3. o sistema filtra e classifica as fontes
4. o sistema monta uma sequência lógica
5. o app transforma isso em curso com módulos, tópicos e aulas
6. a IA pode enriquecer a apresentação e o roadmap

---

## implicações de domínio

O domínio precisa refletir explicitamente a origem do curso.

### curso

Cada curso deve conter algo equivalente a:

- SourceType
- SourceMetadata

#### SourceType

Valores esperados:

- LocalFolder
- OnlineCurated

#### SourceMetadata

Exemplos:

##### para LocalFolder

- RootPath
- ImportDate
- ScanVersion

##### para OnlineCurated

- Provider
- SearchQueries
- SourceUrls
- PlaylistIds
- ImportDate

---

## implicações para aulas

Cada aula também precisa suportar diferentes tipos de origem.

### possíveis tipos de aula

- LocalFile
- ExternalVideo

### campos esperados

- LessonSourceType
- LocalFilePath
- ExternalUrl
- Provider

### regra

A UI não deve depender diretamente da origem.
A UI deve consumir uma abstração de aula, enquanto a infraestrutura resolve se a aula vem de arquivo local ou de link externo.

---

## builders recomendados

A arquitetura deve prever builders separados.

### builders principais

- LocalFolderCourseBuilder
- OnlineCuratedCourseBuilder

### serviço separado

- SupplementaryMaterialsService

### regra

O builder de curso principal não deve ser confundido com o serviço de materiais complementares.

---

## regra de implementação imediata

A prioridade atual continua sendo o fluxo LocalFolder real.

Ou seja:

- scanner de pasta
- estrutura real do curso
- player real
- persistência real
- progresso real

Ao mesmo tempo, a arquitetura já deve ser preparada para suportar OnlineCurated sem retrabalho estrutural.

---

## regra de preservação da UI

A incorporação dessa regra de domínio não autoriza mudanças visuais no frontend aprovado.

Ao refletir essas decisões no projeto:

- preservar integralmente a UI aprovada
- não alterar layout
- não redesenhar páginas
- não refatorar visualmente a aplicação

A adaptação deve acontecer no domínio, na aplicação, na infraestrutura e nos contratos, preservando o frontend atual.

---

## resultado esperado

Depois que essa regra estiver incorporada corretamente, o StudyHub deverá conseguir evoluir para:

### fluxo 1

criar curso a partir de pasta local e tratar esse curso como entidade persistente real

### fluxo 2

criar curso a partir de vídeos gratuitos online e tratar esse curso como entidade persistente real

### em ambos os casos

o usuário deve enxergar a mesma experiência principal:

- catálogo
- curso
- módulos
- tópicos
- aulas
- progresso
- roadmap
- continuidade
