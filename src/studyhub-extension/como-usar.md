# Como usar

## Como instalar no Chrome ou Edge

1. Extraia o ZIP em uma pasta no seu computador (ex: `C:\studyhub-sync\`)
2. Abra `chrome://extensions` (ou `edge://extensions`)
3. Ative o **Modo de desenvolvedor** (canto superior direito)
4. Clique em **Carregar sem compactação** e selecione a pasta extraída
5. O ícone do StudyHub Sync aparecerá na barra do navegador

## Como usar

- Na home do AVA → clique no ícone → exporta todas as disciplinas em andamento
- No Roteiro de Estudo de uma disciplina → exporta todos os módulos e aulas com progresso
- Na página de Avaliações → exporta as provas com datas e pesos

O resultado é um arquivo `.json` estruturado que o StudyHub pode importar, com campos como:

- `tipo`
- `disciplina`
- `modulos`
- `aulas`
- `url`
- `dataInicio`
- `dataFim`
- `peso`
