## **NOTA IMPORTANTE** - Este projeto e 100% pessoal, 110% amador e 1000% criado apenas para satisfazer uma vontade particular de organizar meus cursos de um jeito que funcione para mim. Nao foi feito em padrao de mercado e pode ter decisoes simples ou improvisadas. Sou estudante e construo este app para me ajudar a estudar.

# StudyHub

StudyHub e um app de estudos para Windows que transforma cursos em pastas locais em uma experiencia de aprendizagem com:

- catalogo de cursos
- pagina interna por curso
- progresso por aula/curso
- retomada do ponto onde voce parou
- roadmap e materiais complementares dentro do proprio app

## Download da release

Baixe sempre pela pagina oficial de releases:

- Releases: [https://github.com/jotaCorsino/study.app/releases](https://github.com/jotaCorsino/study.app/releases)

Arquivo principal (Windows):

- `studyhub-app-v<versao>-windows-x64.zip`

## Como instalar e abrir no Windows

1. Baixe o `.zip` da versao mais recente na pagina de releases.
2. Extraia o arquivo em qualquer pasta do computador.
3. Abra `abrir-studyhub.cmd`.

Alternativa:

- execute diretamente `runtime\studyhub.app.exe`.

## Onde os dados ficam

Os dados do StudyHub ficam no computador do proprio usuario (localmente), incluindo:

- banco local do app
- progresso das aulas
- configuracoes
- arquivos de rotina e backup do app

O pacote de release nao inclui dados pessoais de quem publicou a release.

## Fluxo rapido de uso

1. Abra o app.
2. (Opcional) Configure as chaves de integracao em Configuracoes.
3. Adicione um curso pela pasta local.
4. Entre no curso e abra uma aula.
5. Estude normalmente e acompanhe o progresso.
6. Feche e reabra quando quiser: o app preserva o estado salvo.

## Como adicionar cursos

### Curso por pasta local

Use quando voce ja tem os videos no computador:

1. Clique em **Adicionar curso**.
2. Selecione a pasta raiz do curso.
3. Aguarde a importacao.
4. Abra o curso no catalogo.

## Estrutura recomendada da pasta do curso

Organizacao recomendada:

- pasta principal = nome do curso
- subpastas = modulos
- sub-subpastas = materias/aulas
- videos com nomes legiveis e consistentes

Exemplo:

```text
Curso/
  Modulo 01/
    Materia 01/
      video-01-nome-da-aula.mp4
      video-02-outra-aula.mp4
```

Boas praticas:

- evitar nomes genericos como `aula1.mp4` para todos os modulos
- manter padrao de numeracao (`01`, `02`, `03`) para facilitar ordem
- evitar mover/renomear arquivos com o app aberto

## Player local de aulas (estado real hoje)

- o player local usa videos do proprio curso importado por pasta
- controle de velocidade com opcoes: `0.5x`, `1x`, `1.5x`, `2x`, `2.5x`
- velocidade e intro skip ficam em botoes compactos de icone no painel inferior
- cada botao abre uma janelinha (popover) compacta para configuracao
- os popovers abrem **abaixo** dos botoes/painel inferior e nao devem empurrar o layout
- intro skip e configurado por curso com:
  - ativar/desativar
  - segundos de inicio
- o botao de intro skip fica destacado quando a funcao esta ativa
- regra de precedencia: se a aula tiver posicao salva de retomada (`LastPlaybackPosition > 0`), a retomada vence sempre
- o intro skip so entra quando a aula comeca do zero
- o skip e aplicado uma unica vez por abertura da aula
- play/pause por clique direto na area do video local

## Materiais extras (estado atual)

- na tela do curso existe o botao **Videos Gratuitos**, que abre a pagina de **Materiais extras**
- essa area tenta montar curadoria de conteudo complementar gratuito para o curso
- ha acao para gerar/atualizar materiais quando necessario
- os resultados dependem das integracoes configuradas e da disponibilidade de dados no momento
