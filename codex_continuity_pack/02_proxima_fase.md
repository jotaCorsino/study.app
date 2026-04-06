# proxima fase — fase 4

## objetivo central
Implementar persistência local real com SQLite e Entity Framework Core, preservando integralmente a experiência visual e de navegação já aprovada.

## decisão de fase
A Fase 4 deve substituir gradualmente os serviços mockados por serviços persistentes, sem quebrar a UI atual.

## entregas obrigatórias
1. configurar SQLite local
2. configurar Entity Framework Core
3. criar contexto e mapeamentos iniciais
4. persistir cursos
5. persistir progresso
6. persistir roadmap interativo
7. persistir materiais complementares mockados
8. carregar os dados persistidos na abertura do app
9. manter o comportamento atual das páginas sem regressão visual

## escopo funcional mínimo
Ao concluir a fase, estes cenários devem funcionar:

### cenário 1
- usuário marca aula como concluída
- progresso muda
- app é fechado
- app é reaberto
- aula continua concluída

### cenário 2
- usuário marca checklists do roadmap
- progresso do roadmap é recalculado
- app é fechado
- app é reaberto
- progresso do roadmap continua igual

### cenário 3
- catálogo de cursos e materiais complementares continuam disponíveis após reinício do app

## estratégia recomendada
- não eliminar mocks de uma vez
- criar implementações persistentes paralelas
- trocar a injeção de dependência com segurança
- preservar contratos já usados pela UI
- adaptar o mínimo possível das páginas

## ordem de implementação sugerida
1. infraestrutura de persistência
2. entidades persistentes e DbContext
3. migração dos cursos
4. migração do progresso
5. migração do roadmap
6. migração dos materiais
7. validação de ciclo fechar/reabrir

## fora do escopo desta fase
- folder scanner real
- player de vídeo real
- Gemini real
- DeepSeek real
- YouTube real
