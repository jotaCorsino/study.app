## **NOTA IMPORTANTE** - Este projeto é 100% pessoal, 110% amador e 1000% criado apenas para satisfazer uma vontade particular de organizar os meus cursos pessoais de uma maneira que agrade 100% eu, não foi feito em padrão de mercado e provavelmente é macarronico porque eu não soube guiar a IA de maneira correta, pois sou um estudante e fiz esse app justamente para ME ajudar com os estudos, ponto final. 


# StudyHub

StudyHub e um app de estudos para Windows que transforma cursos em pastas locais em uma experiencia de aprendizagem com:

- catalogo de cursos
- pagina interna por curso
- progresso por aula/curso
- retomada do ponto onde voce parou

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

- executar diretamente `runtime\studyhub.app.exe`.

## Onde os dados ficam

Os dados do StudyHub ficam no computador do proprio usuario (localmente), incluindo:

- banco local do app
- progresso das aulas
- configuracoes
- arquivos de rotina e backup do app

O pacote de release nao inclui dados pessoais de quem publicou a release.

## Fluxo rapido de uso

1. Abra o app.
2. (Opcional) Configure chaves de API em Configuracoes.
3. Adicione um curso pela pasta local.
4. Entre no curso e abra uma aula.
5. Estude normalmente e acompanhe o progresso.
6. Feche e reabra quando quiser: o app preserva o estado salvo.

## Player de aulas (estado atual)

### Intro skip por curso (local + externo)

- configuracao por curso agora em controles compactos por popover no painel inferior da aula
- velocidade e intro skip ficam em botoes de icone (sem bloco inline estendido)
- permite ativar/desativar (`Intro skip enabled`) e definir segundos (`Intro skip seconds`)
- botao de intro skip recebe destaque visual quando a funcao esta ativa
- hotfix de layering/posicionamento: popovers de velocidade e intro skip permanecem totalmente visiveis e clicaveis (sem ficarem atras/cortados pelo video)
- regra de precedencia: se a aula tiver posicao salva de retomada (`LastPlaybackPosition > 0`), a retomada vence sempre
- o intro skip so entra quando a aula comeca do zero
- aplicacao unica por abertura/carregamento da aula (sem reaplicacao em play/pause, velocidade ou re-render)

### Menu lateral de cursos (indicador diario)

- cada curso no menu lateral esquerdo exibe um pequeno circulo indicador
- a cor do circulo espelha o estado/meta diaria do curso no dia atual
- o carregamento do indicador usa caminho em lote para evitar consulta individual por curso (sem N+1 no menu)

### Player local (video em arquivo no Windows)

- controle de velocidade com opcoes: `0.5x`, `1x`, `1.5x`, `2x`, `2.5x`
- play/pause por clique na area do video (tap-to-toggle local)
- os controles nativos de transporte continuam ativos
- intro skip inicial suportado com as regras acima

### Player externo (YouTube no host externo do app)

- usa a mesma selecao de velocidade da tela da aula
- aplicacao de velocidade por melhor esforco via bridge
- quando a taxa solicitada nao e suportada pelo YouTube (ex.: `2.5x`), o app reflete a taxa efetiva aplicada
- intro skip inicial suportado com as mesmas regras de precedencia do player local
- se o seek inicial externo nao puder ser aplicado pelo provider/runtime, a aula continua normalmente (falha controlada)
- nesta versao, **tap-to-toggle nao esta implementado no player externo**

## Limitacoes conhecidas do player (v1)

- no YouTube, a taxa efetiva pode divergir da taxa escolhida na UI, dependendo das taxas disponiveis para o video
- tap-to-toggle existe apenas no player local nesta versao

## Como adicionar cursos

### 1) Curso por pasta local

Use quando voce ja tem os videos no computador:

1. Clique em **Adicionar curso**.
2. Selecione a pasta raiz do curso.
3. Aguarde a importacao.
4. Abra o curso no catalogo.

### 2) Curso gerado pela extensao (StudyHub Sync)

Use quando a extensao montar a estrutura do curso a partir da plataforma de origem:

1. Gere/exporte o curso pela extensao.
2. Confirme que os arquivos foram organizados em uma pasta de curso.
3. No StudyHub, clique em **Adicionar curso** e selecione essa pasta gerada.

Se a extensao gerar um arquivo `studyhub-course.json`, mantenha esse arquivo junto da pasta do curso.

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

