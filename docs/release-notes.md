# StudyHub v1.2.0 (em preparação)

## Gerenciamento de origem e sincronização incremental

### Novidades

- Gerenciamento da pasta física dos cursos em **Configurações → Cursos e armazenamento**.
- Relocalização segura após mudança de unidade ou diretório, com validação antes da aplicação.
- Prévia de sincronização com conteúdo inalterado, novo e ausente.
- Aplicação incremental de conteúdo novo sem reconstruir o curso.
- Indicadores de disponibilidade na página do curso, na árvore de conteúdo e no player.

### Segurança de dados

- IDs existentes permanecem estáveis durante relocalização e sincronização.
- Progresso, retomada, conclusão, aula atual e histórico de rotina são preservados.
- Conteúdo ausente não é apagado: permanece persistido e marcado como indisponível.
- Quando um item reaparece no mesmo caminho relativo, recupera a mesma identidade.

### Compatibilidade

- Bancos existentes são atualizados automaticamente para o schema 13.
- Caminhos relativos de aulas e identidades estruturais são preenchidos de forma conservadora.
- Caminhos absolutos antigos continuam disponíveis apenas como fallback de compatibilidade.

### Limitação conhecida

- Rename ou move de módulo, tópico ou vídeo continua sendo interpretado como item ausente + item novo.

### Validação da preparação

- Build MAUI Windows aprovado.
- 302 testes automatizados aprovados.
- 0 testes ignorados.
