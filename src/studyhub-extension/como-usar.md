# Como usar

## Como instalar no Chrome ou Edge

1. Extraia o ZIP em uma pasta no seu computador (ex: `C:\studyhub-sync\`)
2. Abra `chrome://extensions` (ou `edge://extensions`)
3. Ative o **Modo de desenvolvedor**
4. Clique em **Carregar sem compactacao** e selecione a pasta extraida
5. O icone do StudyHub Sync aparecera na barra do navegador

## Fluxo atual

Nesta etapa, a extensao usa apenas a pagina **Central de Midia** da disciplina para montar um **Curso por pasta**.

O fluxo e:

1. Abra a disciplina no Univirtus
2. Entre na **Central de Midia**
3. Abra o popup da extensao
4. Clique em **Escanear Central**
5. Revise disciplina, aulas e videos detectados
6. Clique em **Baixar curso por pasta**

Os downloads vao para a pasta padrao de **Downloads** do navegador com caminhos relativos como:

- `<Nome do Curso>/Aula 01/videos/video-01-...`
- `<Nome do Curso>/Aula 01/videos/pratica-01.mp4`
- `<Nome do Curso>/studyhub-course.json` (opcional)

Depois disso, voce pode mover a pasta manualmente para outro local e usa-la no StudyHub pelo fluxo existente de **curso por pasta**.
