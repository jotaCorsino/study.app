# Runbook Windows do StudyHub

Este documento é um guia rápido de validação e manutenção do StudyHub no Windows.

O runbook principal de build, publish e release fica em:

- `docs/estado-atual/windows-runbook.md`

## Fluxo ativo na v1.2.0

A release pública atual é a v1.2.0. O fluxo ativo é:

1. abrir o StudyHub;
2. importar um curso por pasta local;
3. abrir um vídeo no player local;
4. acompanhar progresso por vídeo, Aula/Módulo e curso;
5. configurar rotina por tempo ou por Aulas/Módulos;
6. validar calendário/histórico local;
7. gerenciar localização e sincronização em **Configurações → Cursos e armazenamento**;
8. usar backup, restore ou reset quando necessário.

Cursos online, IA, roadmaps e materiais externos não fazem parte do fluxo ativo da release atual.

## Validação de curso local

1. Abra o StudyHub.
2. Clique em **Adicionar curso**.
3. Selecione uma pasta com vídeos locais.
4. Confirme que o curso aparece no catálogo.
5. Abra uma Aula/Módulo e inicie a reprodução.
6. Marque vídeos como concluídos.
7. Feche e reabra o app.
8. Confirme que progresso e retomada foram preservados.

Indicador de sucesso: curso visível no catálogo, player funcional e progresso persistido após reinício.

## Validação de origem e sincronização

1. Abra **Configurações → Cursos e armazenamento**.
2. Confirme que o curso local mostra o estado de sua pasta atual.
3. Use **Alterar localização** com uma cópia equivalente e confirme que IDs, progresso, retomada e histórico permanecem.
4. Gere uma prévia de sincronização e confirme que nada é aplicado automaticamente.
5. Aplique conteúdo novo somente depois da confirmação.
6. Remova temporariamente um item na cópia de teste e confirme que ele permanece no catálogo como indisponível.
7. Restaure o item no mesmo caminho relativo e confirme que recupera a mesma identidade.

Não use cursos reais do usuário nessa validação. Rename ou move interno continua sendo tratado como item ausente + item novo.

## Validação da rotina v1.1.0

### Meta por tempo

1. Configure uma meta diária por tempo.
2. Conclua vídeos.
3. Confirme que a duração dos vídeos concluídos soma no dia.
4. Confirme que calendário e menu lateral mostram o progresso por tempo.

### Meta por Aulas/Módulos

1. Configure uma meta diária por Aulas/Módulos.
2. Conclua apenas parte dos vídeos de uma Aula/Módulo.
3. Confirme que o progresso permanece em `0/1`.
4. Conclua todos os vídeos da Aula/Módulo.
5. Confirme que o progresso passa para `1/1`.

Uma Aula/Módulo corresponde tecnicamente a `Topic`, mas na interface deve aparecer como Aula/Módulo.

## Dados locais

Não apagar manualmente:

- `%LOCALAPPDATA%\StudyHub`
- `studyhub.db`
- JSONs de rotina;
- backups do app;
- pastas de cursos do usuário.

## Reset, restore e recovery

- **Reset** limpa o estado local do app, mas não apaga os arquivos físicos dos cursos.
- **Restore** substitui o estado atual por um backup escolhido.
- **Recovery** tenta recuperar o startup sem destruir dados quando possível.

Antes de reset ou restore, crie ou confirme um backup.
