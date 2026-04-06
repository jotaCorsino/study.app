# fase atual do projeto

## resumo objetivo
A Fase 3 foi concluída. O aplicativo está visualmente forte, navegável ponta a ponta e opera de forma determinística com serviços mockados em memória.

## produto atual
StudyHub é um app desktop para estudos em .NET MAUI + Blazor Hybrid que transforma cursos locais e conteúdos curados em uma experiência unificada.

## o que já existe
- catálogo de cursos
- fluxo de adicionar curso
- página interna de curso
- simulador de player de aula
- materiais complementares mockados
- roadmap interativo progressivo
- configurações com testes mockados de chaves de API
- sistema visual próprio em CSS Vanilla

## rotas existentes
- `/`
- `/add-course`
- `/course/{CourseId:guid}`
- `/course/{CourseId:guid}/lesson/{LessonId:guid}`
- `/course/{CourseId:guid}/materials`
- `/course/{CourseId:guid}/roadmap`
- `/settings`

## serviços atuais
- `MockCourseService`
- `MockProgressService`
- `MockRoadmapService`
- `MockMaterialService`

## estado técnico real
- frontend refinado e aprovado visualmente
- dados ainda in-memory
- sem SQLite real
- sem player real
- sem scanner de disco
- sem integrações reais com IA ou YouTube

## arquivos e áreas sensíveis
- `studyhub.app/Pages`
- `studyhub.app/Components`
- `studyhub.app/wwwroot/css/app.css`
- `studyhub.app/wwwroot/css/components.css`
- `studyhub.domain/AIContracts`
- `studyhub.domain/Entities`
- `MauiProgram.cs`

## cuidado conhecido
Há histórico de conflito entre nomes de classes de domínio e partial classes Razor. Evite ambiguidade de namespace ao tocar componentes do roadmap.
