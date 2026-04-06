# studyhub — instrução única para a fase 3

Leia esta instrução como complementar e prioritária para a execução da Fase 3.

## objetivo desta fase

O objetivo da Fase 3 é refinar a experiência de UX/UI do aplicativo, consolidar um sistema visual reutilizável, eliminar qualquer aparência nativa de navegador nos componentes e evoluir a experiência do Roadmap para um formato progressivo mais próximo do produto final.

Esta fase ainda não é sobre backend real, persistência real ou integração real com APIs. O foco é:

- consistência visual;
- feedback claro de estados da interface;
- refinamento de botões, inputs e formulários;
- experiência progressiva do roadmap;
- estrutura pronta para futura integração real com IA e serviços externos.

---

## decisão aprovada sobre a abordagem técnica

Aprovo a abordagem com **CSS Vanilla**, expandindo os arquivos `app.css` e `components.css`.

Não quero adotar MudBlazor nem outras bibliotecas visuais externas engessadas.

Quero manter:

- controle total do sistema visual;
- componentes reutilizáveis próprios;
- consistência visual construída dentro da base atual do projeto.

---

## diretriz principal da fase 3

Não trate esta fase como polimento superficial.

Quero que o app avance para uma aparência visual forte, madura e coerente com o produto final.

A experiência deve parecer cada vez menos um conjunto de telas mockadas e cada vez mais um produto real.

---

## open questions — respostas oficiais

### 1. simulações das chaves de API

Não quero que as três validações retornem sucesso.

Quero que nesta fase exista pelo menos um caso de erro mockado, para que eu consiga validar corretamente os estados visuais de carregando, sucesso e falha.

### definição oficial

- **Gemini:** sucesso mockado
- **YouTube:** erro mockado
- **DeepSeek:** sucesso mockado

### regras obrigatórias

- o comportamento deve ser **determinístico**, não aleatório;
- o objetivo é validar claramente a UI;
- deve existir validação client-side antes da simulação, caso o campo esteja vazio;
- a implementação deve ser fácil de substituir depois por testes reais.

---

### 2. roadmap interativo

Não quero apenas reaproveitar o mock atual de sprints de forma improvisada.

Quero que o Roadmap da Fase 3 já seja alimentado por um mock estruturado no formato mais próximo possível do contrato final que será usado futuramente com IA.

### decisão oficial

- o conteúdo atual de sprints pode servir como **base de conteúdo**, se for útil;
- porém, os dados devem ser reorganizados para o formato final esperado pela experiência do produto.

### formato desejado

- **Levels**
- **Stages**
- **Checklists**
- **Progress por Stage**
- **Progress por Level**
- **Progress geral**

### regra obrigatória

A página de Roadmap deve nascer desacoplada da origem dos dados.

Ou seja:
- agora ela usa um provider mock;
- depois deve ser possível trocar pelo provider real vindo da IA;
- essa troca futura não deve exigir reescrever a UI nem a lógica principal da página.

---

## diretrizes adicionais obrigatórias

### sistema visual

1. Nenhum botão do app deve manter aparência nativa de navegador ou HTML puro.
2. Nenhum input importante deve parecer cru ou padrão.
3. O sistema visual precisa ser consistente entre:
   - `Settings`
   - `AddCourse`
   - `CourseDetail`
   - `Materials`
   - `Roadmap`
4. Hover, focus, disabled, loading, success e error devem seguir linguagem visual consistente.

---

### estados reutilizáveis

Quero um padrão reutilizável para estados de interface:

- `idle`
- `loading`
- `success`
- `error`
- `disabled`

Spinner, mensagens de validação, badges de estado e feedback visual não devem ser hardcoded de maneira solta em cada página.

Sempre que fizer sentido, estruturar isso de forma reutilizável.

---

### settings

Na tela de configurações, quero:

- uso dos novos componentes de input;
- uso dos novos botões do sistema visual;
- validação client-side amigável;
- feedback visual claro para os testes das chaves;
- loading consistente;
- sucesso visualmente evidente;
- erro visualmente evidente.

---

### roadmap

No Roadmap, quero que a experiência já reflita a visão final do produto.

### estrutura esperada

- hero com visão geral do progresso;
- resumo do roadmap;
- actions no topo:
  - expandir tudo
  - recolher tudo
  - zerar progresso
- níveis;
- etapas;
- checklists;
- barras ou indicadores de progresso;
- recomputação imediata do progresso ao marcar itens.

### regra de comportamento

Ao marcar uma checkbox:
- o progresso da etapa deve ser recalculado;
- o progresso do nível deve ser recalculado;
- o progresso geral deve ser recalculado.

Tudo isso deve acontecer de forma clara e previsível.

---

## escopo prático da implementação desta fase

### 1. expandir o sistema visual reutilizável

Modificar `components.css` e demais arquivos necessários para consolidar:

- `.btn`
- `.btn-primary`
- `.btn-secondary`
- `.btn-outline`
- `.btn-icon`
- `.input-group`
- `.input-field`
- `.input-error`
- `.spinner`
- estilos de feedback visual e badges de estado

---

### 2. refinar a tela de settings

Modificar `Settings.razor` para:

- substituir inputs e botões pelos novos padrões;
- implementar validação client-side;
- implementar estados mockados determinísticos para as chaves;
- mostrar loading, success e error com boa clareza visual.

### comportamento esperado dos testes mockados

#### Gemini
- preenchido corretamente -> loading -> sucesso

#### YouTube
- preenchido corretamente -> loading -> erro mockado

#### DeepSeek
- preenchido corretamente -> loading -> sucesso

#### qualquer campo vazio
- erro client-side imediato sem rodar teste simulado

---

### 3. aplicar o sistema visual nas telas afetadas

Refinar e padronizar os componentes visuais restantes em:

- `AddCourse.razor`
- `CourseDetail.razor`
- `Materials.razor`
- demais áreas impactadas

Objetivo:
- eliminar botões brancos ou padrão;
- padronizar espaçamentos;
- melhorar coesão visual.

---

### 4. reconstruir a experiência do roadmap

Modificar `Roadmap.razor` e componentes relacionados para abandonar a timeline solta e adotar a experiência progressiva.

Criar ou ajustar componentes como:

- `RoadmapLevel.razor`
- `RoadmapStage.razor`

Esses componentes devem suportar:

- título;
- descrição;
- hierarquia de progressão;
- checklist;
- cálculo de progresso.

---

### 5. usar um mock provider mais próximo do contrato final

Quero que os dados do Roadmap usados na Fase 3 já sejam modelados no formato futuro do sistema.

Isso significa que a origem mock deve estar mais próxima possível do contrato que será usado depois pela camada de IA.

Não quero uma solução improvisada que precise ser descartada depois.

---

## regra de arquitetura para o roadmap

A UI do Roadmap não deve depender diretamente de um formato improvisado de mock.

Ela deve depender de uma estrutura estável e future-friendly.

A troca futura entre:
- provider mock
- provider real de IA

precisa ser simples.

---

## resultado esperado ao final da fase 3

Ao final desta fase, eu preciso ver:

1. um sistema visual muito mais consistente;
2. nenhum botão com aparência branca/nativa/padrão;
3. inputs mais maduros e coerentes com o app;
4. feedback visual realista para as chaves de API;
5. roadmap muito mais próximo da visão final do produto;
6. melhor sensação de produto real e menos sensação de mock cru.

---

## instrução final de execução

Pode seguir com a implementação da Fase 3 respeitando integralmente as decisões deste documento.

Ao final, me entregue:

- resumo do que foi implementado;
- lista de arquivos alterados;
- como validar manualmente;
- pontos que devo revisar visualmente;
- pontos que devo revisar funcionalmente.
