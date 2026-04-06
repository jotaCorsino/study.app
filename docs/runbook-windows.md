# StudyHub — Runbook Windows

Guia de referência para validação, manutenção, backup e recuperação do StudyHub no Windows.

---

## 1. Publicar e rodar no Windows

```powershell
cd "C:\Users\Estudos\Desktop\Study App\app_build\src\studyhub.app"
dotnet build -f net10.0-windows10.0.19041.0
dotnet run -f net10.0-windows10.0.19041.0
```

Para publicar como executável standalone:

```powershell
dotnet publish -f net10.0-windows10.0.19041.0 -c Release --self-contained
```

---

## 2. Caminhos de armazenamento gerenciados

| Artefato | Caminho padrão |
|---|---|
| Banco de dados | `%LOCALAPPDATA%\studyhub.app\studyhub.db` |
| Sidecars SQLite (`-wal`, `-shm`, `-journal`) | Mesma pasta do banco |
| Backups | `%LOCALAPPDATA%\studyhub.app\backups\` |
| Rotina por curso | `%LOCALAPPDATA%\StudyHub\Routine\{CourseId}\` |

> **Nota:** `%LOCALAPPDATA%` normalmente é `C:\Users\<usuario>\AppData\Local`.

---

## 3. Validação — LocalFolder

1. Abra o StudyHub
2. Clique em **Adicionar curso** → selecione uma pasta com vídeos locais
3. Confirme que o curso aparece no catálogo com título, módulos e aulas
4. Navegue até uma aula e inicie a reprodução
5. Verifique que o player native do MAUI carrega o vídeo corretamente
6. Marque uma aula como concluída e navegue para outra
7. Feche o app, reabra e confirme que o progresso foi preservado

**Indicador de sucesso:** curso visível no catálogo, player funcional, progresso persistido após reinício.

---

## 4. Validação — OnlineCurated

> Requer chave de API Gemini e YouTube configuradas em **Configurações**.

1. Abra o StudyHub → **Configurações**
2. Insira as chaves de API e salve
3. Vá para **Catálogo** → **Adicionar curso** → aba **Online**
4. Digite o nome de um curso e inicie a criação
5. Acompanhe os passos de geração (validação, estrutura, apresentação, materiais)
6. Confirme que o curso aparece no catálogo ao final
7. Navegue até o curso e verifique estrutura de módulos e aulas

**Indicador de sucesso:** curso gerado com estrutura completa, sem erros de geração travados.

---

## 5. Validação — Falha de embed / estado quebrado

Para simular e recuperar um estado operacional quebrado:

1. Encerre o app abruptamente (fechar pelo X ou pelo Gerenciador de Tarefas) durante uma criação de curso online
2. Reabra o app
3. O `StudyHubDatabaseInitializer` executará `TryRecoverStartupStateAsync` automaticamente
4. Verifique nos logs de debug que steps com status `Running` foram marcados como `Failed`
5. Navegue para o curso afetado e tente a operação de manutenção: **Limpar estado operacional quebrado**

**Indicador de sucesso:** app inicia sem crash, curso aparece no catálogo (mesmo incompleto), estado `Running` normalizado para `Failed` sem perda de estrutura.

---

## 6. Validação — Regeneração e reprocessamento por curso

Para testar as operações de manutenção disponíveis pelo `CourseMaintenanceService`:

### Verificar via logs (Debug)
```
StudyHub course maintenance started. Operation: regenerate-presentation. CourseId: {guid}
StudyHub course maintenance completed. Operation: regenerate-presentation. CourseId: {guid}
```

### Operações disponíveis por SourceType

| Operação | LocalFolder | OnlineCurated |
|---|---|---|
| Regenerar apresentação | ✅ | ✅ (via retry stage) |
| Regenerar refinamento textual | ✅ | ✅ (via retry stage) |
| Regenerar materiais complementares | ✅ | ✅ |
| Ressincronizar pasta local | ✅ | ❌ |
| Revalidar curso online | ❌ | ✅ |
| Limpar estado operacional quebrado | ✅ | ✅ |

> **Regra de segurança:** regenerar apresentação **não apaga progresso**. Regenerar materiais **não destrói a estrutura do curso**. Ressincronizar LocalFolder **preserva enriquecimento** quando possível.

---

## 7. Backup manual

```powershell
# Localizar backups existentes
ls "$env:LOCALAPPDATA\studyhub.app\backups\"
```

O `AppBackupService` cria automaticamente backups de segurança antes de:
- `RestoreBackupAsync` → cria `pre-restore-safety-backup-{timestamp}`
- `ResetAppStateAsync` → cria `pre-reset-safety-backup-{timestamp}`

Cada backup contém:
- `database/studyhub.db` (e sidecars WAL/SHM)
- `routine/{CourseId}/routine_settings.json`
- `routine/{CourseId}/daily_records.json`
- `backup-manifest.json` com metadados

**Regra de segurança:** backups não sobrescrevem versões anteriores (timestamp único no nome do diretório).

---

## 8. Restore de backup

> O restore cria automaticamente um safety backup antes de restaurar.

Via código (a ser exposto na UI de Settings futuramente):

```csharp
var result = await backupService.RestoreBackupAsync(
    backupDirectory: @"C:\Users\...\AppData\Local\studyhub.app\backups\studyhub-backup-20260406-153045000",
    createSafetyBackup: true
);
```

Após o restore, **reinicie o app** para recarregar o estado em memória.

---

## 9. Reset do estado local

> **Atenção:** o reset apaga progresso, rotina e histórico de geração. Os arquivos físicos dos cursos (vídeos, pastas) não são afetados.

```csharp
var result = await backupService.ResetAppStateAsync(createSafetyBackup: true);
```

Sequence:
1. Cria safety backup
2. Limpa banco de dados e JSONs de rotina
3. Recria banco limpo via `InitializeAsync`

---

## 10. Verificar logs em tempo real (Debug)

O StudyHub usa `ILogger` em todos os serviços críticos. Em modo Debug, os logs aparecem no Output do Visual Studio ou via:

```powershell
dotnet run -f net10.0-windows10.0.19041.0 2>&1 | Select-String "StudyHub"
```

Padrões importantes nos logs:

| Padrão | Significado |
|---|---|
| `StudyHub database initialization started. Mode: existing` | App inicializou com banco existente |
| `StudyHub database initialization started. Mode: new` | Primeiro uso ou após reset |
| `StudyHub database startup recovered` | Recovery automático bem-sucedido |
| `StudyHub app backup completed` | Backup concluído com sucesso |
| `StudyHub app restore completed` | Restore concluído com sucesso |
| `StudyHub app reset completed` | Reset concluído com sucesso |
| `Course maintenance completed. Operation: {op}. CourseId: {guid}` | Manutenção de curso concluída |

---

## 11. Estrutura de conceitos: Restore vs Reset vs Recovery

| Conceito | Significado | Perda de dados? |
|---|---|---|
| **Restore** | Restaurar de um backup existente, sobrescrevendo o estado atual | Sim — estado atual é substituído pelo backup |
| **Reset** | Limpar estado local e recriar banco limpo | Sim — todo progresso/rotina é apagado |
| **Recovery** | Tentativa automática de recuperar startup sem destruir dados | Não — preserva dados se possível |

> O **Recovery** é executado automaticamente pelo `StudyHubDatabaseInitializer` quando o startup falha. Ele tenta reparar o banco antes de qualquer ação destrutiva. Somente em último caso (falha total de recovery) o banco seria recriado do zero.
