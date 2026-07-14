# StudyHub v1.2.0

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

### Validação da release

- Build MAUI Windows aprovado.
- 302 testes automatizados aprovados.
- 0 testes ignorados.
- Staging limpo e ZIP final validados contra a manifestação da Tarefa 13.
- Release pública validada após um novo download do asset.
- Tamanho e SHA-256 do download público idênticos ao ZIP local enviado.
- Artefato público extraído e executado com sucesso a partir da pasta QA.

### Artefato publicado

- Data: `14/07/2026`.
- Release: [StudyHub v1.2.0](https://github.com/jotaCorsino/study.app/releases/tag/v1.2.0).
- Tag: `v1.2.0`.
- Commit do binário: `a7d277649da8f3401f555c1eceb4b15c237c2900`.
- Asset: `studyhub-windows-x64.zip`.
- Tamanho: `70.921.059 bytes`.
- SHA-256: `E3C7F6D1621B21F67D011BC781DE6F430B13D1D10D684AB6A5D44571F8A1399D`.
