# Runbook Windows do StudyHub

Este documento é um guia rápido de validação e manutenção do StudyHub no Windows.

O runbook principal de build, publish e release fica em:

- `docs/estado-atual/windows-runbook.md`

## Fluxo ativo validado na v1.1.0

O fluxo ativo atual é:

1. abrir o StudyHub;
2. importar um curso por pasta local;
3. abrir um vídeo no player local;
4. acompanhar progresso por vídeo, Aula/Módulo e curso;
5. configurar rotina por tempo ou por Aulas/Módulos;
6. validar calendário/histórico local;
7. usar backup, restore ou reset quando necessário.

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
