# studyhub — instruções para codex

## stack oficial
- .NET MAUI
- Blazor Hybrid
- C#
- Clean Architecture enxuta
- SQLite + Entity Framework Core na fase atual

## objetivo atual
Continuar o projeto a partir da Fase 3 concluída e implementar a Fase 4: persistência local real.

## regra máxima
Não altere a UI/frontend visual do app sem autorização explícita do usuário.

## regra crítica de preservação da UI
O frontend atual está aprovado e deve ser tratado como protegido.

Sem autorização explícita do usuário, o Codex não pode alterar, refatorar, substituir, reorganizar ou “melhorar” visualmente nenhum aspecto da interface.

Isso inclui, sem limitar:
- layout
- estrutura visual das páginas
- componentes visuais
- design system
- CSS
- classes visuais
- spacing
- tipografia
- cores
- estados visuais
- hierarchy visual
- aparência de botões, inputs, cards, badges, painéis e navegação

## o que pode fazer sem pedir autorização
- conectar dados reais à UI existente
- implementar persistência local
- ajustar bindings
- corrigir lógica
- corrigir navegação
- corrigir estados internos
- adicionar serviços, banco, contratos e integrações
- criar novas funcionalidades respeitando rigorosamente o padrão visual já existente
- adicionar código estrutural necessário para suportar persistência, scanner local, player real e integrações futuras

## o que não pode fazer sem autorização explícita
- redesenhar telas
- refatorar aparência
- trocar componentes visuais
- mudar estilos
- “melhorar” UX por conta própria
- reorganizar layout
- alterar identidade visual
- reescrever CSS da interface
- alterar estrutura de páginas que já estejam visualmente aprovadas
- trocar a stack visual por bibliotecas externas

## regra para novas funcionalidades
Se alguma nova funcionalidade exigir UI nova:
1. seguir o padrão visual existente
2. minimizar impacto no frontend aprovado
3. não alterar telas já aprovadas sem autorização explícita

Em caso de dúvida: preservar a UI original.

## prioridade da fase atual
1. persistência local real
2. manutenção da navegação e UX/UI já aprovadas
3. troca gradual dos serviços mockados por serviços persistentes
4. salvar e recarregar estado do app entre sessões

## o que não fazer agora
- não introduzir backend remoto
- não implementar Gemini real ainda
- não implementar DeepSeek real ainda
- não implementar YouTube real ainda
- não reescrever o design system
- não trocar a stack visual por bibliotecas externas

## convenções
- usar nomes de arquivos em letras minúsculas quando novos arquivos forem criados
- manter separação clara entre domain, application, infrastructure e app
- evitar lógica de banco dentro da UI
- evitar acoplamento entre páginas Razor e detalhes do SQLite
- preservar os arquivos de frontend já aprovados sempre que possível

## definição de pronto da fase
A fase está pronta quando o usuário puder:
- abrir o app
- alterar dados relevantes do curso e progresso
- fechar completamente o app
- reabrir o app
- encontrar os dados idênticos, vindos de persistência real em `.db`

## comandos esperados
- build: `dotnet build`
- run windows: `dotnet run --project "studyhub.app\studyhub.app.csproj" -f net10.0-windows10.0.19041.0`

## modo de trabalho
Para mudanças grandes:
- planeje rapidamente
- execute por blocos pequenos
- preserve integralmente a UI aprovada
- ao final, reporte apenas:
  1. o que mudou
  2. arquivos alterados
  3. como validar
  4. o próximo passo recomendado
