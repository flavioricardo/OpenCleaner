# Changelog

[English](CHANGELOG.md) · **Português (Brasil)**

Todas as mudanças relevantes do OpenCleaner. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e as versões seguem o [Versionamento Semântico](https://semver.org/lang/pt-BR/). A versão principal deste
arquivo é a [em inglês](CHANGELOG.md); esta é a tradução.

## [Não lançado]

## [1.2.0] - 2026-10-18

### Adicionado

- Limpeza personalizada: cache e logs do **Epic Games Launcher**, do **Battle.net** e do **EA app**, e logs e
  relatórios de falha do **Riot Client**. Cookies, sessões e bancos de dados ficam, para ninguém ser
  desconectado; pastas que exigiriam administrador ficam de fora.
- Jogos: **relógio do sistema forçado na configuração de inicialização** (`useplatformclock`,
  `useplatformtick`, `disabledynamictick`, um ajuste comum de boosters). Só aparece com o app como
  administrador, porque ler o BCD exige; aplicar apaga só esses valores e pede para reiniciar.
- Jogos: recomendação do **modo de energia na tomada** em notebooks no plano Equilibrado quando o modo na
  tomada não é *Melhor desempenho* (só leitura; abre *Configurações > Sistema > Energia*).
- Jogos: os **jogos do PC Game Pass** instalados pelo app Xbox (`XboxGames\<jogo>\Content`) são encontrados e
  podem ir para a placa de vídeo dedicada; o `gamelaunchhelper.exe` é ignorado.
- Changelog também em português (este arquivo), com o inglês como versão principal.
- Um workflow manual, *Rewrite release notes*, refaz as notas de uma release já publicada.

### Alterado

- O changelog e as notas das releases passam a ser em inglês.

### Corrigido

- As notas das releases saíam com acentos quebrados ("sÃ³") e um BOM sobrando: o workflow de release lia o
  modelo como ANSI no Windows PowerShell 5.1. Agora as notas são lidas e gravadas em UTF-8 sem BOM.

### Segurança

- Só os três valores de relógio podem virar argumento do `bcdedit`; nenhuma outra configuração de
  inicialização é apontada como problema nem apagada.

## [1.1.0] - 2026-10-17

### Adicionado

- **Jogos** (novo item na barra lateral e cartão na Verificação de saúde): encontra e desfaz o que "game
  boosters" deixam no Windows, com backup `.reg` antes de aplicar.
  - Ajustes deixados por otimizadores: Modo de Jogo desligado, política que bloqueia a Xbox Game Bar, apps
    em segundo plano proibidos, cópia do Desempenho Máximo ativa num notebook e apps do Xbox ausentes
    (Xbox Identity Provider, Xbox TCUI, Gaming Services), que só abrem a página na Microsoft Store.
  - Segurança: atualização automática do Chrome e do Edge desativada, Windows Update desligado, Defender
    desligado por política, UAC desligado e proteções contra Spectre/Meltdown desligadas.
  - Placa de vídeo dos jogos: em notebooks com duas placas, encontra os jogos (Steam, Epic Games e os que o
    Windows já viu rodar) e os põe em *Alto desempenho*, a mesma opção das Configurações.
  - Recomendado para jogos (só leitura, abre a página do Windows): gravação em segundo plano, agendamento de
    GPU, otimizações para jogos em janela e precisão do ponteiro. Desempenho × segurança: Integridade de
    Memória, só como informação.

### Alterado

- O cache de shaders (DirectX, NVIDIA, AMD) vem desmarcado na Limpeza personalizada, com aviso: apagá-lo faz
  os jogos recompilarem os shaders e travarem nas primeiras partidas.
- Verificação de saúde com quatro cartões e textos mais curtos.

### Segurança

- Descoberta de jogos endurecida contra manifestos e registros maliciosos: `installdir` da Steam só como nome
  de pasta (um caminho absoluto fazia `C:\Windows` virar pasta de jogo), `LaunchExecutable` da Epic preso à
  pasta do jogo, executáveis da pasta do Windows ignorados, caminhos normalizados antes de comparar, pasta de
  jogo que é junção não é vasculhada e o limite de 30 executáveis vale dentro de cada pasta.
- Serviços `GoogleUpdater*` com nome que o Gerenciador de Serviços não aceita (espaço, `&`, aspas) nem
  chegam a ser oferecidos.

### Corrigido

- Parágrafo duplicado e quebrado (`testsun-real.ps1`) nos READMEs.

### Testes

- `tests/run-real.ps1`: sandboxes de registro, serviços e bibliotecas de jogos para cada item da página
  Jogos, inclusive falsos positivos, backup, conserto e restauração; ataques (manifestos com caminho
  absoluto, `..` e barras, junção para o System32, valores de tipo errado, nomes de serviço com `&`, 60
  rodadas de registro aleatório) e desempenho (300 jogos, 1000 entradas no registro de jogos, 2000 apps em
  segundo plano, árvore funda de executáveis).
- `tests/run-smoke.ps1`: capturas da página Jogos, com um jogo de teste e uma recomendação selecionada, e
  *Aplicar marcados* de ponta a ponta contra uma sandbox do registro (com e sem administrador, backup e
  cancelamento).
- `tests/run-admin-tests.ps1`: troca real do tipo de inicialização de um serviço de teste temporário pelo
  Gerenciador de Serviços (rodar como administrador).

## [1.0.1] - 2026-10-17

### Segurança

- Antes de apagar cada arquivo, confere se alguma pasta entre ele e a pasta-base virou junção ou link
  depois da análise (impede que uma limpeza como administrador apague arquivos do sistema).
- Caminhos das regras passam a ser canônicos (`..`, `.`, barras duplicadas, ponto final, nomes 8.3 e
  prefixos `\\?\`) antes de comparar com as pastas protegidas.
- Recusa regras "apagar tudo" logo abaixo do perfil (Downloads, OneDrive, projetos), da raiz da unidade
  ou de pastas protegidas (`%AppData%\*`); Downloads, OneDrive e os backups do próprio app viram protegidos.
- Exclusão segura não sobrescreve arquivos com mais de um link físico.
- O Localizador de duplicados ignora arquivos só na nuvem e offline, em vez de baixá-los.
- App e instalador só carregam DLLs do System32 (proteção contra DLL plantada).
- IDs do winget e chaves de registro são validados antes de virarem argumento de linha de comando.
- `SECURITY.md` com o modelo de ameaças e como reportar vulnerabilidades.

### Testes

- `tests/run-real.ps1`: todas as regras executadas numa sandbox, ataques ao motor, registro, inicialização,
  desinstalador, disco, duplicados, winget, drivers, `/AUTO`, robustez e desempenho.
- `tests/run-installer-tests.ps1`: instalação, atualização com o app aberto, DLL plantada e desinstalação.

## [1.0.0] - 2026-10-17

Primeira versão pública.

### Adicionado

- **Verificação de saúde** com medidor de espaço do disco do sistema (usado, pode ser liberado e livre)
  e resumo de registro, inicialização e atualizações.
- **Limpeza personalizada** com mais de 70 regras embutidas (Windows, navegadores Chromium e Firefox,
  aplicativos), detecção dos programas instalados, aviso de programas abertos, pastas personalizadas,
  exclusões, opção de manter temporários com menos de 24 horas, exclusão segura e suporte a `winapp2.ini`.
- **Registro**: sete categorias de entradas órfãs, com backup `.reg` antes de corrigir.
- **Atualizar programas** pelo winget e **Atualizar drivers** pelo Windows Update, com ponto de
  restauração antes de instalar drivers.
- **Desinstalar programas**, **Inicialização**, **Analisador de disco** e **Localizador de duplicados**.
- Modo silencioso `/AUTO` para limpeza agendada.
- Instalador por usuário (sem administrador) com desinstalação em *Configurações > Apps*, versão
  portátil e somas SHA-256.
- Interface com navegação por teclado, foco visível, contraste AA e escala por DPI.
- App, instalador e desinstalador em português e inglês, com o idioma do Windows por padrão, seletor no instalador
  e em *Opções*; as seleções de limpeza salvas valem nos dois idiomas.

[Não lançado]: https://github.com/flavioricardo/OpenCleaner/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.2.0
[1.1.0]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.1.0
[1.0.1]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.0.1
[1.0.0]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.0.0
