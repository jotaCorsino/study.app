# abono mensal de meta diaria

## Objetivo da feature

Permitir que horas estudadas acima da meta diaria dentro do mes vigente gerem credito mensal para abonar dias recentes do mesmo mes que nao bateram a meta.

O objetivo e melhorar a leitura do desempenho mensal sem apagar o historico bruto de estudo. O app deve continuar registrando exatamente o que aconteceu em cada dia, mas tambem deve reconhecer quando horas extras de dias acima da meta compensam dias pendentes do mesmo mes.

## Regra funcional

### Conceitos base

- Meta diaria do dia:
  `DailyGoalMinutesAtTheTime` do `DailyStudyRecord` para um dia planejado.
- Minutos estudados no dia:
  `MinutesStudied` bruto do `DailyStudyRecord`.
- Minutos extras:
  `max(0, MinutesStudied - DailyGoalMinutesAtTheTime)` em dias planejados.
- Minutos faltantes:
  `max(0, DailyGoalMinutesAtTheTime - MinutesStudied)` em dias planejados.
- Credito mensal disponivel:
  soma dos minutos extras do mes menos os minutos consumidos pelos abonos aplicados na mesma avaliacao mensal.

### Dias elegiveis para abono

Um dia pode receber abono quando todas as regras abaixo forem verdadeiras:

- pertence ao mesmo ano e mes da apuracao;
- nao e dia futuro dentro da apuracao atual;
- e um dia planejado, ou seja, nao esta com `Status = Unplanned`;
- possui `DailyGoalMinutesAtTheTime > 0`;
- nao bateu a meta bruta do dia, portanto possui `Minutos faltantes > 0`.

Dias `Unplanned` nunca entram na fila de abono e nunca consomem credito mensal.

### Formacao do credito mensal

- O credito mensal nasce apenas de dias do mesmo mes em que `MinutesStudied > DailyGoalMinutesAtTheTime`.
- O credito e isolado por mes.
- O credito de um mes nao pode ser usado em outro mes.
- Nao existe transferencia manual ou automatica entre meses.

### Distribuicao do abono

- A distribuicao deve acontecer de tras para frente.
- A ordem e: dia pendente mais recente para o dia pendente mais antigo dentro do mesmo mes.
- Nao deve haver salto para um dia mais antigo enquanto um dia mais recente ainda estiver na frente da fila.
- O credito mensal e consumido nessa ordem.
- Um dia so passa a ser considerado abonado quando seu faltante estiver totalmente coberto pelo credito acumulado na avaliacao daquele mes.

### Escopo temporal

- A regra de abono sempre trabalha com um recorte mensal fechado por `ano + mes`.
- O mes atual usa somente os dados do proprio mes atual.
- Meses anteriores podem ser recalculados de forma independente para leitura historica, mas nunca podem receber credito de outro mes.

## Regra visual

- A cor original do quadradinho nao muda.
- O quadradinho continua representando o status bruto do dia.
- Se o dia for abonado, ele recebe apenas um circulo vazado com borda verde.
- O circulo indica recuperacao por horas extras.
- O status bruto continua sendo a fonte da cor.
- O marcador visual de abono deve ser um ornamento adicional, nunca uma troca de cor.

## Regra de calculo

- O dia abonado passa a contar como meta batida para a media mensal.
- O status bruto do dia nao deve ser sobrescrito.
- A media mensal deve considerar o status efetivo do dia:
  meta batida originalmente ou meta batida por abono.
- Para efeito de media mensal, um dia abonado deve contribuir como 100 por cento de cumprimento.
- Dias nao abonados continuam contribuindo com seu percentual bruto atual.
- Dias `Unplanned` continuam fora da media, mantendo a regra atual.

### Separacao entre bruto e efetivo

- Status bruto:
  estado persistido atualmente no `DailyStudyRecord`.
- Status efetivo:
  estado derivado em leitura para calculos mensais e marcacao visual de abono.

O modelo tecnico deve manter essa separacao para evitar que o app perca o historico real do que foi estudado em cada dia.

## Proposta tecnica

### Servico e regra de negocio

O ponto natural da implementacao e `RoutineService`, porque ele ja concentra:

- leitura e escrita de `routine_settings.json`;
- leitura e escrita de `daily_records.json`;
- normalizacao de `MinutesStudied`;
- definicao de `Status` bruto por `ApplyStatus(...)`;
- montagem de recortes diarios e mensais via `GetDailyRecordAsync(...)`, `GetDailyRecordsAsync(...)` e `GetMonthlyRecordsAsync(...)`;
- calculo de streak via `GetCurrentStreakAsync(...)`.

Recomendacao tecnica para esta feature:

- manter `ApplyStatus(...)` como regra de status bruto;
- adicionar uma camada derivada de avaliacao mensal, separada da persistencia bruta;
- evitar regravar `Status` bruto apenas para representar o abono;
- concentrar a distribuicao do credito em um helper interno do `RoutineService`, aplicado sobre uma colecao mensal ja normalizada.

### Modelo de dados

Recomendacao inicial:

- nao persistir o abono como verdade primaria nos JSONs existentes;
- preservar `DailyStudyRecord` como registro bruto;
- calcular o abono em tempo de leitura, por mes, a partir de `MinutesStudied`, `DailyGoalMinutesAtTheTime` e `Status` bruto.

Se for necessario expor a feature de forma limpa para a UI, a melhor direcao e criar um modelo derivado especifico, por exemplo um read model mensal, contendo campos como:

- `RawStatus`
- `IsAbonado`
- `MissingMinutes`
- `ExtraMinutes`
- `EffectiveMetGoal`
- `EffectiveCompliancePercentage`

Essa abordagem reduz risco de retrocompatibilidade em `daily_records.json`.

### Contrato de servico

`IRoutineService` provavelmente precisara expor uma forma de retorno mensal enriquecida para nao misturar:

- dado bruto persistido;
- dado efetivo usado para media mensal;
- sinal visual de abono.

Direcao recomendada:

- manter os metodos brutos atuais para compatibilidade;
- adicionar um novo metodo de leitura mensal derivada, em vez de redefinir silenciosamente o significado de `GetMonthlyRecordsAsync(...)`.

### Componentes visuais afetados

- `GoalsDashboard.razor`
  Usa `GetMonthlyRecordsAsync(...)` para renderizar o calendario mensal do curso. E o principal candidato para desenhar o circulo vazado verde sem alterar a cor base do quadradinho.
- `MonthlyExpandedView.razor`
  Hoje calcula a media mensal por `Average(record => record.CompliancePercentage)`. Deve passar a usar o percentual efetivo do mes.
- `NavMenu.razor`
  Hoje mostra apenas o indicador diario bruto. Como nao ha regra visual pedindo ornamento de abono nessa superficie, a recomendacao inicial e manter esse indicador bruto, salvo nova decisao de produto.

### Testes necessarios

Os testes mais importantes devem ficar em `RoutineServiceTests.cs` e cobrir:

- geracao correta de credito mensal a partir de dias acima da meta;
- aplicacao de tras para frente, do pendente mais recente para o mais antigo;
- bloqueio de transferencia de credito entre meses;
- exclusao de dias `Unplanned` da fila e da media;
- preservacao do `Status` bruto original;
- calculo da media mensal com status efetivo;
- comportamento em virada de mes;
- estabilidade com `DailyGoalMinutesAtTheTime` historico;
- compatibilidade com JSONs antigos sem novos campos.

## Riscos

- Media mensal:
  risco de regressao porque hoje a media usa `CompliancePercentage` bruto.
- Streak:
  o `GetCurrentStreakAsync(...)` hoje considera dias com estudo bruto maior que zero. E preciso decidir explicitamente se dia abonado sem estudo suficiente altera ou nao a streak.
- Calendario:
  risco de confundir cor bruta com estado efetivo se a UI nao separar bem os dois conceitos.
- Retrocompatibilidade dos JSONs:
  adicionar campos persistidos em `daily_records.json` aumenta risco em leituras antigas e em dados ja salvos.
- Virada de mes:
  o credito precisa zerar no limite do mes sem reaproveitar minutos extras do mes anterior.
- Duplicidade entre status bruto e status efetivo:
  se os dois conceitos nao forem nomeados de forma clara, a feature pode gerar bugs de leitura, media, legenda e manutencao futura.

## Decisoes recomendadas para implementacao futura

- O abono deve ser tratado como projecao mensal derivada, nao como substituicao do registro bruto.
- O calendario mensal deve continuar mostrando a cor do estado bruto.
- O marcador de abono deve ser adicional e discreto.
- A media mensal deve usar o estado efetivo.
- O mes deve ser a unidade maxima de credito.
