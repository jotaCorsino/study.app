# StudyHub — Documento de Handoff Técnico (Fase 3 Concluída)

Este documento reflete objetivamente o estado da base de código e da arquitetura até o fechamento da Fase 3. O projeto tem forte apelo visual, focado em frontend-first (Mocking robusto e Desing System customizado Vanilla CSS).

## 1. Visão Resumida do Produto
**StudyHub** é um aplicativo de desktop focado em produtividade para estutudantes, voltado para Windows, construído em **.NET MAUI + Blazor Hybrid**. Transforma diretórios locais cheios de vídeos e PDFs ou links do YouTube curados por IA em uma experiência unificada e moderna de educação contínua.

## 2. Estado Atual da Implementação
Concluímos a **Fase 3**. O aplicativo é navegável ponta a ponta de forma determinística (com dados simulados consistentes). Houve uma refatoração massiva da UX/UI recente. O backend ainda opera sem persistência (In-Memory/Mock).

## 3. Árvore de Projetos e Pastas
A Solution baseia-se num monorepo Clean Architecture enxuto:
- `studyhub.app`: Apresentação (Projeto principal MAUI + Blazor). Contém `Pages`, `Components`, `wwwroot/css`, e o `MauiProgram.cs` com Injeção de Dependência.
- `studyhub.application`: Logic layer contendo Interfaces (ex: `ICourseService`, `IProgressService`) e as implementações atuais (Pasta `MockServices`).
- `studyhub.domain`: Camada core contendo `Entities`, os enumeradores (`Enums`) e os contratos cruciais de comunicação que imitam os artefatos de IA (`AIContracts`).
- `studyhub.shared`: Modelos de transferência, Constantes globais ou Helpers.

## 4. Páginas e Componentes Já Implementados
**Pages**:
- `Home.razor`: Catálogo de cursos contendo a barra de busca e visualização das listagens.
- `AddCourse.razor`: Fluxo de inserção de curso (Local Folder vs IA YouTube).
- `CourseDetail.razor`: Espinha dorsal unificada, servindo o cabeçalho descritivo, as sanfonas dos módulos e a engrenagem lateral com estatísticas de progresso, botões do roadmap e material suplementar.
- `LessonPlayer.razor`: Simulador de visualizador da aula com botões Next/Check status inline.
- `Materials.razor`: Lista rica de links extras simulando busca externa.
- `Roadmap.razor`: Novo motor do roadmap progressivo interativo que renderiza a cascata de etapas da IA.
- `Settings.razor`: Painel de credenciais de AI.

**Components**:
- `CourseCard`, `ModuleAccordion`, `ProgressRing`, `Breadcrumb`, `StatusBadge`, `PlayerPlaceholder`.
- `RoadmapLevel` e `RoadmapStage`: Componentes responsáveis pela reatividade matemática dos checklists.

## 5. Rotas Existentes
- `/` -> Catálogo inicial
- `/add-course` -> Inclusão de um novo recurso
- `/course/{CourseId:guid}` -> Célula principal e estatísticas de um curso
- `/course/{CourseId:guid}/lesson/{LessonId:guid}` -> Visualização do arquivo (vídeo) e troca de status da respectiva aula.
- `/course/{CourseId:guid}/materials` -> Curadoria suplementar do YouTube.
- `/course/{CourseId:guid}/roadmap` -> Árvore de checklist estruturada.
- `/settings` -> Inputs e testes mockados simulando request REST validando credenciais.

## 6. Serviços Mockados Atuais (In-Memory)
- `MockCourseService`: Provê o banco genérico dos Cursos e seus sub-nós (Modules, Topics, Lessons).
- `MockProgressService`: Orquestra dados de conclusão, estrias de visualização diárias e porcentagem gerada sinteticamente no carregamento.
- `MockRoadmapService`: Provê a árvore sintética (`List<RoadmapLevel>`).
- `MockMaterialService`: Provê o retorno em formato card de vídeos avulsos classificados por AI.

*(Registrados via Injeção de Dependências em `MauiProgram.cs`)*.

## 7. Sistema Visual de Referência (CSS)
Temos total domínio sem frameworks externos volumosos (Nenhum Bootstrap / Zero Tailwind).
Arquivos no `wwwroot/css/`:
- **`app.css`**: Design tokens e globais (`--accent-primary`, animações, variáveis hex, tipografia via Root).
- **`components.css`**: Classes utilitárias agnósticas prontas para uso: `.btn`, `.btn-primary`, `.btn-secondary`, `.btn-outline`, `.input-group`, `.input-field`, `.input-error`, `.spinner`, `.feedback-badge`, etc.

## 8. Arquivos Mais Importantes Alterados Recentemente
- `CourseRoadmapContracts.cs` (Domain) e `RoadmapEntities.cs` (Domain): Atualizados inteiros para suportar o roadmap aninhado de 4 níveis de recursão.
- `RoadmapLevel.razor` e `RoadmapStage.razor`: Contêm lógicas de cálculo matemático via Linq interagindo com os DTOs do Roadmap que o `Roadmap.razor` ouve com instâncias `EventCallback`.
- `Settings.razor`: Exibe forte estrutura defensiva com tratamento client-side de simulação Async das requisições via Threads.

## 9. Funcionalidades Que Já Funcionam (Visualmente)
- Todo o fluxo de cliques e rolagem entre Telas funciona sem bugs na stack Blazor/Router.
- Cálculo interativo do percentual global e de barra de progresso no *Roadmap* acionável pelos Checkboxes.
- Acordeões de `CourseDetail` se retraindo/escondendo de acordo com o `ModuleNumber`.
- Teste dinâmico de chaves API (Renderiza Loading -> Retorna feedback de Validação simulado após timer local de 1.5s).

## 10. Funcionalidades Que Ainda São Mock
- Lógicas de Save (o componente muda a variável na memória instantânea, mas nada é escrito em banco de dados).
- Carregamentos longos (os mocks invocam `Task.Delay` artificiais).
- Todos os arquivos do usuário ("videos") são apenas representações em Grid baseados em listas instanciadas no backend via C#.

## 11. Funcionalidades Ainda Não Iniciadas
- Scanner de disco para detectar `.mp4`, `.pdf` no Windows.
- O Real "Video Player" do HTML5 lincado ao arquivo em disco ou link web.
- Integração real HTTP com as end-points das linguagens AI (Gemini HTTP API).
- SQLite via Entity Framework local.

## 12. Comandos Reais para Build e Execução (Windows)
Terminal do Windows, na raiz em que se encontra a Solution (`.sln`) ou dentro da `src/`:
**Build:** `dotnet build`
**Run MAUI Desktop WinUI:** `dotnet run --project "studyhub.app\studyhub.app.csproj" -f net10.0-windows10.0.19041.0`

## 13. Contratos e Modelos de IA e Roadmap
Existe uma simetria em `studyhub.domain\AIContracts\`. Tudo que vai ou vem de uma "Mente" Generativa deve seguir rígidos schemas. Exemplo forte de hierarquia imposta:
`RoadmapLevelContract` abriga `List<RoadmapStageContract>` que tem a `List<RoadmapChecklistBlockContract>` e finalizando na granularidade final de `RoadmapChecklistItemContract`. Essa cadeia é uma forte lei arquitetural implementada no ecossistema e transportada identicamente para as Entities do Blazor.

## 14. Open Questions Em Aberto
- Nenhuma bloqueando tecnicamente o ecossistema. 

## 15. Riscos e Dívidas Técnicas
- **Tipagem Parcial/Conflitante (Resolvido na F3):** Foi identificada uma sobreposição de nomes entre Entities de classes de negócio e Partial Classes nativas .razor do Blazor (ex: `RoadmapLevel`). Isso é amenizado escrevendo diretivas full-name de namespace explicitamente no `@code` interno do razor (Ex: `studyhub.domain.Entities.RoadmapStage`). Tenha cuidado com referências ambíguas na tela e no código.
- **Isolamento de Estado:** A árvore virtual do Mock carrega na memória, o que pode mascarar bugs relacionados ou estados perdidos no ciclo de vida Blazor no momento da transição iminente para SQLite.

## 16. Recomendação Objetiva Para a Próxima Fase (Fase 4)
A recomendação primordial é a **Implementação de Persistência Local**. 
A aplicação chegou a um estado sólido de UX/UI madura que se comunica sem gargalos em serviços de dados estáticos In-Memory. 
É natural migrar essa "casca" funcional injetando conectores autênticos ao SQLite local com `Entity Framework Core` no Windows. A Injeção de Dependências está pronta (apenas trocar `MockCourseService` para `EFCourseService`).

## 17. Critério de Aprovação da Próxima Fase
Se a persistência for abraçada, o critério é simples: Uma vez criando ou alterando propriedades do Curso (Exemplo, checando aulas como completas, rodando o percentual interativo) e fechando/matando o aplicativo, todas as propriedades deverão carregar idênticas provenientes de arquivo físico de banco `.db` na área do usuário ao reabrir a aplicação, sem gerar quebras de dependência na UI.
