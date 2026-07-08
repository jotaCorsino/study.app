# StudyHub 1.1.0 RC local

## Metas diárias por tempo ou Aulas/Módulos

- Permite escolher metas diárias por tempo de estudo ou por Aulas/Módulos.
- Aulas/Módulos correspondem ao modelo `Topic`.
- Um `Topic` só conta para a meta quando todos os seus vídeos estão concluídos.
- Rotina, dashboard, calendário e sidebar respeitam o modo de meta configurado.
- O histórico antigo baseado em tempo continua compatível.
- Bancos SQLite existentes migram para o schema 10 com `completed_at_utc`.
- O crédito de Study Unit é idempotente.
- Não há reconstrução retroativa nem backfill do histórico antigo.
