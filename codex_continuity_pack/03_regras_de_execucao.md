# regras de execucao para a fase 4

## regra 1 — preservar a interface
A UI atual é considerada base aprovada. Alterações visuais só devem acontecer se forem necessárias para suportar persistência, erro real ou carregamento real.

## regra 2 — desacoplamento
A UI não deve conhecer SQLite diretamente. Pages e Components devem continuar falando com services/interfaces.

## regra 3 — troca gradual
Não remova os mocks antes de ter a versão persistente equivalente pronta.

## regra 4 — sem salto para integrações externas
Persistência local vem antes de IA real, YouTube real, scanner de disco e player real.

## regra 5 — persistir o que já existe conceitualmente
Persistir pelo menos:
- cursos
- módulos
- tópicos
- aulas
- status de conclusão
- progresso derivado ou insumos para cálculo
- roadmap
- checklist do roadmap
- materiais complementares

## regra 6 — evitar retrabalho futuro
Modele a persistência para suportar depois:
- curso local
- curso curado por YouTube
- textos gerados por IA por curso
- múltiplos providers de IA

## regra 7 — validação
Toda mudança deve ser verificável com build e teste manual de reinício do app.

## regra 8 — resposta final do ciclo
Ao final de cada bloco de implementação, reportar apenas:
- mudanças principais
- arquivos alterados
- como validar
- próximos passos
