# StudyHub v1.1.0

## Metas diárias por tempo ou Aulas/Módulos

### Novidades

- Adicionada escolha entre meta diária por tempo de estudo ou meta por Aulas/Módulos concluídos.
- A rotina, o dashboard, o calendário e o menu lateral respeitam o modo escolhido.
- Uma Aula/Módulo só conta para a meta quando todos os vídeos dela forem concluídos.
- O histórico antigo baseado em tempo permanece compatível.
- O banco SQLite migra para o schema 10 com `completed_at_utc` em `topics`.
- O crédito de Aulas/Módulos é idempotente e evita duplicidade em `daily_records.json`.
- Não há reconstrução retroativa perfeita do histórico antigo por Aulas/Módulos.

### Persistência e compatibilidade

- Bancos existentes são migrados para o schema 10.
- `topics.completed_at_utc` guarda a primeira conclusão da Aula/Módulo.
- `daily_records.json` pode armazenar `CompletedStudyUnitIds`.
- JSON antigo sem `GoalMode` continua como meta por tempo.
- `CompletedStudyUnitIds` ausente ou `null` é tratado como lista vazia.

### Validação

- 107 testes automatizados aprovados.
- Build Windows aprovado.
- Smoke test do executável aprovado.

### Artefato publicado

- Release: https://github.com/jotaCorsino/study.app/releases/tag/v1.1.0
- Asset: `StudyHub-v1.1.0-windows-x64.zip`
- SHA256: `5DDFFA3244D7D4A08A76B72B65D1FA70113263CEB2D3426930A9FBEF87BBCD8F`
