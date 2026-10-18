# Plano: menu Jogos

Status: etapas 1 a 7 implementadas; versão 1.1.0 pronta, ainda sem tag nem publicação. O núcleo está em `src/Core/Games.cs`, `GameLibrary.cs` e
`GpuPreference.cs`; a interface, em `src/UI/GamesPage.cs`, no item "Jogos" da barra lateral e num 4º
cartão na Verificação de saúde. Testes: `tests/RealTests.cs` e capturas em `tests/SmokeTest.cs`. Os quatro
cartões ficam sempre numa linha: duas linhas não cabem na altura mínima da janela.

Um menu novo, **Jogos**, que confere se o PC está bem configurado para jogar: desfaz o que
"otimizadores" e "game boosters" quebraram, põe os jogos na placa de vídeo dedicada em notebooks com duas
placas e mostra as configurações do próprio Windows que ajudam. Cada item é explicado e só muda se o
usuário marcar. O OpenCleaner continua sem otimizador: nada de
pacote de ajustes milagrosos aplicado de uma vez.

## Por que

Quem joga é o público-alvo dos boosters. Esses programas mudam dezenas de configurações sem mostrar quais,
e o efeito continua anos depois da desinstalação. Muitas vezes o resultado é o contrário do prometido.

Caso real: o **Nich FPS+ 2.0.1** (Electron 9, licença paga por chave), analisado por engenharia reversa do
`app.asar`. Com um clique e como administrador, ele roda cerca de 20 scripts PowerShell com
`-ExecutionPolicy Bypass`. Não mostra prévia, não deixa escolher item por item e não faz backup.

| Passo | O que faz | Efeito para quem joga |
|---|---|---|
| step-2 | `AutoGameModeEnabled=0` e `AllowAutoGameMode=0` | **Desliga o Modo de Jogo**, que existe justamente para dar prioridade ao jogo |
| step-2 | Remove os apps do Xbox, inclusive `XboxIdentityProvider` e `Xbox.TCUI` | Jogos do Game Pass e da Store (Minecraft, Forza, Halo) não fazem login |
| step-2 | Política `AllowGameDVR=0` em HKLM | A Xbox Game Bar não grava, e as Configurações dizem "gerenciado pela sua organização" |
| step-6 | Importa e ativa uma cópia do plano *Desempenho Máximo* | Em notebook: mais calor, menos bateria e, com isso, mais *thermal throttling* |
| step-3 | Desativa os serviços que atualizam o Chrome | O navegador para de receber correções de segurança |
| step-10-7 | Desliga os apps em segundo plano | Notificações do app Xbox e de apps da Store param |
| step-9 | Desinstala o Skype | Remove um programa sem perguntar |
| step-10-x, step-12 | Privacidade (telemetria, Cortana, localização) e efeitos visuais | Escolhas do usuário, sem efeito real em FPS |
| step-13 | Apaga temporários, esvazia a Lixeira, roda `cleanmgr` | O OpenCleaner já faz isso com prévia |

Além disso, a cada abertura ele envia um *hash* dos números de série da placa-mãe e do gabinete para um
servidor de licença no Heroku e se atualiza sozinho a partir de `update.nichpc.com.br`. O "Redefinir
configurações" grava valores que não são os padrões do Windows e tem um erro de sintaxe (`-neq`) que impede
a remoção do plano de energia. Hoje o app nem abre mais: o servidor de atualização não existe.

**Neste PC** (notebook com Steam), quase cinco anos depois, continuam ativos: Modo de Jogo desligado,
política `AllowGameDVR=0`, Xbox Identity Provider e Xbox TCUI ausentes, cópia do *Desempenho Máximo*
ativa e apps em segundo plano desligados.

## Princípios

1. **Três tipos de item, com três posturas:**
   - **Consertar:** o que um otimizador mudou e que prejudica (jogo, segurança ou bateria). Vem marcado. O
     OpenCleaner desfaz direto, com backup.
   - **Recomendado para jogos:** configurações do Windows que ajudam, mas que são escolha do usuário.
     Vem desmarcado. A ação abre a página certa das Configurações, e o usuário muda no lugar oficial.
   - **Desempenho × segurança:** o ganho existe, mas reduz a proteção (Integridade de Memória). Aparece
     com aviso, nunca tem ação de um clique e só abre a página do Windows.

   A exceção é a **GPU dos jogos**: o OpenCleaner grava a preferência direto, porque fazer isso pelas
   Configurações exige procurar o `.exe` de cada jogo. A escrita é a mesma das Configurações, só no HKCU.
2. **Só entra o que tem efeito comprovado.** Ficam de fora os ajustes populares em boosters sem ganho
   medível em jogos atuais: `NetworkThrottlingIndex`, `SystemResponsiveness`, Nagle/`TcpAckFrequency`,
   resolução do timer, "limpadores de RAM", *core parking*, desativar serviços em massa. Privacidade também
   fica de fora.
3. **Consertar = voltar ao padrão do Windows**, não a um valor inventado. Para políticas, o padrão é não
   ter o valor, então consertar apaga o valor.
4. **Catálogo em código, não em arquivo de regras.** Um `.ini` que grava valores em HKLM como
   administrador seria um vetor de ataque (ver `SECURITY.md`). Cada item é revisado no código.
5. **Backup antes de consertar.** Registro, tipo de inicialização de serviços e plano ativo ficam no
   registro, então um único `.reg` desfaz tudo.
6. **Verificar só lê, sem rede e sem segundo plano.** `/AUTO` não mexe em nada disto.

## Catálogo

A detecção não pede administrador. Consertar itens de HKLM e de serviços pede, pelo fluxo `RequireAdmin`
que já existe.

### Consertar (marcados)

| Id | Detecta | Conserta para | Neste PC |
|---|---|---|---|
| `game-mode-off` | `HKCU\Software\Microsoft\GameBar` `AutoGameModeEnabled=0` ou `AllowAutoGameMode=0` | Apaga os valores (padrão: ligado) | sim |
| `gamedvr-policy` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` `AllowGameDVR=0` | Apaga o valor | sim |
| `xbox-sign-in` | Faltam `Microsoft.XboxIdentityProvider` ou `Microsoft.Xbox.TCUI`, que vêm com o Windows; ou falta `Microsoft.GamingServices` com o app Xbox (`Microsoft.GamingApp`) instalado (lido em `HKCU\Software\Classes\Local Settings\...\AppModel\Repository\Packages`, sem PowerShell) | **Não reinstala sozinho**: abre na Microsoft Store a página do primeiro que falta: Identity Provider `9WZDNCRD1HKW`, TCUI `9NKNC0LD5NN6`, Gaming Services `9MWPM2CQNLHN` (confirmados em 17/10/2026). Só aponta se a Microsoft Store estiver instalada (no LTSC esses apps nunca vêm). Não mexe no registro, então não entra no backup | sim, os dois primeiros |
| `power-plan-copy` | Plano ativo fora dos GUIDs do Windows e com a descrição do *Desempenho Máximo* (`@%SystemRoot%\system32\powrprof.dll,-18`, visto no `.pow` do Nich FPS+), **em PC com bateria** | `powercfg /setactive` no Equilibrado. O plano copiado continua na lista | sim |
| `background-apps` | `GlobalUserDisabled=1` (HKCU) ou a política `LetAppsRunInBackground=2` | Apaga os valores. Se **todos** os apps tiverem `Disabled=1`, o padrão típico de booster, oferece limpar esses valores também | sim |
| `chrome-updates`, `edge-updates` | Serviços de atualização do Chrome (`gupdate`, `gupdatem`, `GoogleChromeElevationService` e os `GoogleUpdater*` com executável em `Program Files\Google`) ou do Edge (`edgeupdate`, `edgeupdatem`, `MicrosoftEdgeElevationService`) com `Start=4` | Pelo Gerenciador de Serviços (`ChangeServiceConfig`/`ChangeServiceConfig2`): `gupdate`/`edgeupdate` Automático atrasado, `GoogleUpdater*` Automático, os demais Manual (padrões conferidos neste PC) | não (o Chrome se reparou) |
| `windows-update` | `wuauserv`/`UsoSvc` desativados; políticas `NoAutoUpdate=1`, `DisableWindowsUpdateAccess=1` | `wuauserv` Manual, `UsoSvc` Automático atrasado; apaga as políticas. Com `WUServer` (WSUS) configurado, as políticas são da empresa e não são apontadas | não |
| `defender-policy` | Políticas `DisableAntiSpyware`, `DisableRealtimeMonitoring`, `DisableBehaviorMonitoring` = 1 | Apaga os valores. Com a Proteção contra Adulteração ligada, o Defender já ignora essas políticas e pode recusar a remoção: o item diz isso e vem desmarcado (`TamperProtection` 5 ou 1) | não |
| `uac-off` | `EnableLUA=0` | `EnableLUA=1` (exige reiniciar) | não |
| `cpu-mitigations` | `FeatureSettingsOverride=3` (outros valores, como 8 ou 72, ligam proteções extras e não são apontados) | Apaga os dois valores (exige reiniciar) | não |

Os itens de segurança (da tabela acima, de `chrome-updates` a `cpu-mitigations`) aparecem primeiro e em âmbar: boosters para jogos
costumam desligar isso tudo em nome de FPS.

### GPU dos jogos (notebooks com duas placas de vídeo)

Em notebooks com GPU integrada e dedicada, o Windows decide qual placa cada programa usa, e jogos às
vezes rodam na integrada: FPS muito abaixo do esperado, sem nenhum aviso. Corrigir isso pelas
Configurações exige achar e escolher o `.exe` de cada jogo à mão. Aqui o OpenCleaner encontra os jogos e
faz o mesmo ajuste que as Configurações fazem.

Este grupo é uma exceção ao "Recomendado não grava nada", e a exceção é estreita: a única escrita é o
valor `GpuPreference` em `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, que é o mesmo que a página
*Configurações > Sistema > Tela > Elementos gráficos* grava. Afeta só o usuário atual, não pede
administrador e vale a partir da próxima vez que o jogo abrir.

**Quando aparece:** só em PC com bateria e com duas ou mais placas PCI de vídeo presentes (lidas no WMI,
que ignora drivers antigos que ficaram no registro). Placas virtuais e o driver genérico da Microsoft não
contam. A memória dedicada não entra na regra: o WMI informa 1 GB para a Intel HD integrada deste PC, e
APUs da AMD reservam memória de vídeo, então ela não separa integrada de dedicada. Em PC de mesa, o
monitor costuma estar ligado direto na placa dedicada, e o grupo não aparece. Neste PC: Intel HD
Graphics + NVIDIA GeForce 820M.

**Como acha os jogos**, nesta ordem, juntando os resultados pela pasta do jogo:

1. **Registro de jogos do Windows:** `HKCU\System\GameConfigStore\Children\*`, valor `MatchedExeFullPath`.
   É o `.exe` que o Windows já reconheceu como jogo e viu rodar, de qualquer loja. Neste PC, é assim que
   o VALORANT (Riot) aparece.
2. **Epic Games:** `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON com `DisplayName`,
   `InstallLocation` e `LaunchExecutable`, o `.exe` exato). A leitura usa só esses três campos, por
   expressão regular, sem precisar de biblioteca de JSON.
3. **Steam:** `steamapps\libraryfolders.vdf` (as bibliotecas) e `appmanifest_*.acf` (`name` e
   `installdir`). A Steam não grava o `.exe` principal em texto, então vale o `.exe` do item 1 dentro
   da pasta. Se não houver, entram os `.exe` até três níveis abaixo, menos uma lista de auxiliares
   (`unins*`, `*CrashHandler*`, `*redist*`, `vc_redist*`, `dxsetup*`, `EasyAntiCheat*`, `BEService*`,
   `CefSharp*`, `QtWebEngineProcess*`). Marcar um auxiliar como alto desempenho não faz mal.

O nome mostrado é o da loja ou, para os achados só pelo item 1, o `ProductName` do `.exe` (ou o nome da
pasta). Launchers (Steam, Epic, Riot Client, Battle.net) não entram.

**O que mostra e o que faz:**

| Estado atual (`GpuPreference`) | Mostra | Vem marcado |
|---|---|---|
| ausente ou `0` | Windows decide | sim |
| `1` | Economia de energia (GPU integrada) | sim, em âmbar |
| `2` | Alto desempenho | não aparece como pendência |

- **Aplicar** grava `GpuPreference=2;` e **preserva os outros itens do valor** (por exemplo,
  `SwapEffectUpgradeEnable=1;`). Só troca o item `GpuPreference`.
- **Voltar para "Windows decide"** (menu de contexto) tira só o item `GpuPreference` e apaga o valor se
  não sobrar nada.
- O backup `.reg` geral inclui a chave `UserGpuPreferences` inteira. Importar um `.reg` não apaga
  valores criados depois, então desfazer um jogo que estava em "Windows decide" é pelo menu de contexto,
  não pelo backup.
- O texto do item explica o custo: na bateria, a placa dedicada gasta mais, e para jogar é isso mesmo o
  que se quer. Também diz que, em placas NVIDIA com Optimus, o driver pode já mandar o jogo para a
  dedicada. A prioridade entre a preferência do Windows e o painel do fabricante ainda precisa ser
  confirmada antes de virar texto da interface.
- Jogos da Microsoft Store e do Game Pass (pacotes em `WindowsApps`) ficam para depois: a preferência
  deles usa outro identificador.

### Recomendado para jogos (desmarcados; abre as Configurações)

| Id | Mostra | Abre | Por quê |
|---|---|---|---|
| `background-recording` | "Gravar o que aconteceu" ligado (`...\CurrentVersion\GameDVR` `HistoricalCaptureEnabled=1`) | `ms-settings:gaming-gamedvr` | Gravar em segundo plano custa desempenho; desligar se não usa |
| `hags` | Agendamento de GPU acelerado por hardware (`GraphicsDrivers` `HwSchMode`: 2 = ligado) | `ms-settings:display-advancedgraphics` | Exigido pelo *Frame Generation* da NVIDIA (DLSS 3). Aparece só com `HwSchMode=1` (desligado de propósito): sem o valor, não dá para saber se o driver oferece a opção |
| `windowed-optimizations` | Otimizações para jogos em janela (`DirectXUserGlobalSettings` `SwapEffectUpgradeEnable`), no Windows 11 | `ms-settings:display-advancedgraphics` | Menor latência e VRR em jogos em janela sem bordas. Aparece só com `SwapEffectUpgradeEnable=0` explícito, porque o padrão sem o valor muda entre versões do Windows 11 |
| ~~`power-mode`~~ | Fora da etapa 5: os GUIDs e nomes das sobreposições de energia mudam entre Windows 10 e 11 e não foram confirmados; num notebook o item nunca apareceria | — | Rever junto com um PC de mesa para testar |
| `mouse-precision` | "Aumentar a precisão do ponteiro" ligado (`Control Panel\Mouse` `MouseSpeed` 1 ou 2) | Propriedades do Mouse (`main.cpl`) | A aceleração muda a mira conforme a velocidade da mão; jogadores de tiro costumam desligar |

### Desempenho × segurança (só informação)

| Id | Mostra | Abre | Texto |
|---|---|---|---|
| `memory-integrity` | Integridade de Memória (HVCI) ligada (`Win32_DeviceGuard`, `SecurityServicesRunning` contém 2) | `windowsdefender://coreisolation` | A Microsoft cita ganho em alguns jogos ao desligar, em troca de menos proteção contra drivers maliciosos. O OpenCleaner não recomenda nem desliga |

### Limpeza: correção para quem joga

- A regra **Cache de shaders (DirectX/GPU)** vem marcada hoje (`rules/builtin.ini`, `Default=True`).
  Apagar esse cache obriga cada jogo a recompilar os shaders, e as primeiras partidas travam. Passa para
  `Default=False`, com o aviso "Os jogos vão recompilar os shaders e podem travar nas primeiras partidas".
  Vale para a Limpeza personalizada e para o `/AUTO`.

### Para depois

Feito depois da 1.1.0 (seção `[Unreleased]` do CHANGELOG):

- **Regras de limpeza para launchers:** cache e logs do Epic Games Launcher, Battle.net e EA app, logs do
  Riot Client. Sem cookies, sessões, bancos de dados e pastas que exigem administrador.
- **`boot-clock`:** `useplatformclock`, `useplatformtick` e `disabledynamictick` na entrada `{current}` do
  BCD. Só aparece como administrador (ler o BCD exige). Como o `bcdedit` traduz os valores ("Sim"), qualquer
  valor presente conta como fora do padrão; apagar é sempre seguro.
- **`power-mode`:** recomendação, só em notebook com o Equilibrado ativo, quando o modo na tomada
  (`ActiveOverlayAcPowerScheme`) não é `ded574b5…` (*Best performance*). GUIDs do artigo da Microsoft
  "Customize the Windows performance power slider".
- **PC Game Pass:** jogos em `X:\XboxGames\<jogo>\Content` entram na GPU dos jogos (executáveis comuns).

Ainda em aberto:

- **Jogos empacotados antigos da Store (`WindowsApps`):** a preferência de GPU deles não usa o caminho do
  `.exe`, e não achei fonte confiável para o formato do nome do valor. Confirmar empiricamente (escolher um
  app da Store em *Configurações > Elementos gráficos* e ler `UserGpuPreferences`) antes de implementar.

## Desenho

### Core: `src/Core/Games.cs`

Implementado na etapa 1 (no mesmo formato do `RegistryScanner`: `Scan`, `Backup`, `Fix`):

```
class RegEdit          Root, Key, Name, int? Value        // Value null = apagar o valor
class GameFinding      Id, Name, Why, Location, Current, Expected, NeedsAdmin, DefaultChecked,
                       List<RegEdit> Edits, Func<bool> Run  // Run: só a troca do plano de energia
class GameContext      RegistryKey Hklm, Hkcu; bool HasBattery; Func<string, bool> SetActivePowerScheme
static class GameScanner   Scan(GameContext) só lê; Backup(findings, .reg); Fix(finding)
```

- Itens desta etapa: `game-mode-off`, `gamedvr-policy`, `background-apps`, `background-apps-policy`
  (`LetAppsRunInBackground=2`) e `background-apps-bulk`: 80% ou mais de pelo menos 10 apps com
  `Disabled=1` e `DisabledByUser=1`. Esse item vem **desmarcado**, porque pode ter sido escolha do usuário.
  Neste PC são 68 de 72: os 4 restantes foram instalados depois do Nich.
- `game-mode-off` grava `AutoGameModeEnabled=1` (o que as Configurações gravam ao ligar) e apaga o antigo
  `AllowAutoGameMode`.
- `power-plan-copy` também aponta cópias do *Alto desempenho* (`powrprof.dll,-12`). Planos de fabricante
  têm descrição própria e não são apontados. A troca de plano não entra no backup `.reg`, porque o plano
  copiado continua na lista e dá para voltar a ele em Opções de Energia.

Para as etapas 4 a 6, entram o tipo (Consertar, Recomendado, Desempenho × segurança), `Security`,
`NeedsReboot` e `SettingsUri`, e o `GameContext` ganha serviços e pacotes.

- **GPU dos jogos** em arquivos próprios, porque é descoberta de jogos e não um item do catálogo:

  ```
  src/Core/GameLibrary.cs   class InstalledGame { Name, Store (Windows | Steam | Epic), Folder, List<string> Exes }
                            static List<InstalledGame> Find(GameSources)   // registro + Steam + Epic, só lê
  src/Core/GpuPreference.cs static bool IsHybridLaptop(IEnumerable<VideoAdapter>, bool hasBattery)
                            static int Get(RegistryKey hkcu, string exe)        // 0, 1 ou 2
                            static string Merge(string current, int pref)       // troca só GpuPreference=
                            static bool Set(RegistryKey hkcu, string exe, int pref)
  ```

  `GameSources` recebe as raízes do registro e as pastas da Steam e da Epic, para o teste usar uma
  sandbox. `VideoAdapter` (nome e `PNPDeviceID`) sai do WMI (`Win32_VideoController`).
- **Consertar só faz quatro coisas:** apagar um valor de registro, gravar um DWORD conhecido, mudar o tipo
  de inicialização de um serviço e ativar o plano Equilibrado por GUID fixo.
- **Recomendado e Desempenho × segurança nunca gravam nada**: só `Process.Start` de um URI fixo do
  catálogo (`ms-settings:`, `windowsdefender:`, `ms-windows-store:`).
- **Serviços:** leitura por `ServiceController.StartType` mais `DelayedAutostart`. Escrita por
  `ChangeServiceConfig` e `ChangeServiceConfig2` (P/Invoke em `advapi32`), nunca direto no registro. Os
  curingas (`GoogleUpdaterService*`) só aceitam serviços com `ImagePath` em `Program Files\Google\`.
- **Backup:** generalizar `RegistryScanner.Backup` para `Util.ExportRegistry(keys, target)`. As chaves de
  serviço e o plano ativo entram no mesmo `.reg`, salvo em
  `%AppData%\OpenCleaner\Backups\jogos-AAAAMMDD-HHMMSS.reg`.
- **Testabilidade:** `GameContext` recebe as raízes do registro, os serviços e os pacotes, então o teste
  usa `HKCU\Software\OpenCleanerTest\Games\...` e listas falsas.

### UI

- **Barra lateral:** "Jogos" logo abaixo de "Registro", com o ícone `gamepad-2` do Lucide (adicionar o SVG
  em `assets/icons`).
- **Página `GamesPage` (`src/UI/GamesPage.cs`)**, título "PC para jogos" e subtítulo "Confere o que pesa
  nos jogos e desfaz o que otimizadores mudaram.":
  - Ações: **Aplicar marcados** (principal) e **Verificar** (secundária).
  - Lista em quatro grupos: *Consertar*, *GPU dos jogos* (só em notebook com duas placas),
    *Recomendado para jogos* e *Desempenho × segurança*. Colunas: *Item*, *Agora → Esperado*, *Onde*.
    Na GPU dos jogos, cada linha é um jogo (nome, loja, "Windows decide → Alto desempenho") e o painel de
    baixo lista os `.exe`. Os itens dos dois últimos grupos não têm caixa de seleção: têm um link "Abrir
    nas Configurações".
  - O botão principal vira **Aplicar marcados**, porque agora cobre consertos e GPU.
  - Embaixo, um painel com o "Por que isso importa" do item selecionado.
  - Consertar: separa itens de usuário e de administrador (mesmo diálogo do Registro), oferece o backup
    `.reg`, aplica, verifica de novo e avisa se algum item pede reiniciar.
  - Estado vazio: "Tudo certo para jogar. A verificação só olha."
- **Verificação de saúde:** um 4º `ResultCard` "Jogos" com o número de itens a consertar mais os jogos
  que não estão na GPU dedicada (por exemplo, "5 · 1 jogo na GPU integrada"), em âmbar se houver algum
  item de segurança ou jogo em *Economia de energia*. Ação: "Revisar". A largura dos cartões passa de `/3` para
  `/Controls.Count` (quatro de 180 px cabem na janela mínima de 920 px; se apertar, grade 2×2).

## Testes

- `tests/RealTests.cs`, seção "Jogos":
  - registro na sandbox: cada valor "ruim" é detectado, consertar mexe só no esperado, vizinhos intactos,
    `.reg` restaura tudo;
  - **sem falso positivo**: padrão, valor ausente ou tipo errado não são apontados; o plano copiado não é
    apontado em PC sem bateria; o curinga rejeita serviço fora de `Program Files\Google`;
  - Recomendado e Desempenho × segurança: `Fix` é nulo e o URI está na lista fixa;
  - GPU dos jogos, numa sandbox com uma Steam falsa (`libraryfolders.vdf`, `appmanifest`, um `.exe` de
    jogo, `unins000.exe` e `UnityCrashHandler64.exe`), um manifesto falso da Epic e entradas falsas no
    registro de jogos: acha os três jogos, ignora os auxiliares e os launchers e junta pela pasta os
    resultados repetidos;
  - `Merge`: valor vazio, `GpuPreference=1;`, `SwapEffectUpgradeEnable=1;` e os dois juntos. Só o item
    `GpuPreference` muda, e "Windows decide" apaga o valor quando ele fica vazio;
  - `IsHybridLaptop`: Intel sem memória + NVIDIA 2 GB com bateria → sim; o mesmo sem bateria → não; só
    uma placa → não; Intel UHD + Intel Arc 8 GB → sim; placa dedicada + Microsoft Basic Display → não;
  - no PC real, só leitura: lista de jogos e detecção da placa sem gravar nada.
  - no PC real, só leitura: `Scan()` em menos de 1 s e o registro igual depois (snapshot das chaves do
    catálogo).
- `tests/SmokeTest.cs`: captura da página e do cartão nos dois idiomas.
- Manual, numa VM: rodar um `.reg` que imita o Nich FPS+, verificar, consertar, reiniciar e restaurar pelo
  backup. Conferir num jogo da Store que o login volta depois de instalar o Identity Provider.

## Documentação

- README (pt e en): seção "Jogos" com os três grupos, linhas novas em "O que cada ação altera" e na FAQ:
  "O OpenCleaner aumenta o FPS?" (não promete FPS; ele desfaz o que derruba o desempenho e mostra as
  opções do Windows) e "Por que não limpa mais o cache de shaders por padrão?".
- `SECURITY.md`: catálogo fixo em código, quatro tipos de ação, URIs fixos, curingas presos a uma pasta.
- `CHANGELOG.md`: entrada em `[Unreleased]`.

## Ordem de entrega

1. `Util.ExportRegistry` + `Games.cs` com `game-mode-off`, `gamedvr-policy`, `background-apps` e
   `power-plan-copy` (os que este PC tem) e os testes de sandbox.
2. GPU dos jogos: `GameLibrary.cs` e `GpuPreference.cs`, com os testes de sandbox.
3. `GamesPage`, item na barra lateral e cartão na Verificação de saúde.
4. `xbox-sign-in` (leitura dos pacotes e links da Store).
5. Recomendado e Desempenho × segurança (só leitura e URIs).
6. Itens de segurança: serviços (P/Invoke), Windows Update, Defender, UAC e mitigações.
7. Cache de shaders desmarcado, documentação, capturas e release 1.1.0.

As etapas 1 e 2 não dependem uma da outra e podem ser feitas em paralelo. A 3 junta as duas.

## Decisões em aberto

- Confirmar qual preferência de GPU vale quando o painel da NVIDIA e a do Windows divergem, antes de
  escrever o texto da interface.
- Jogos sem `.exe` principal conhecido (Steam, sem passagem pelo registro de jogos): aplicar a todos os
  `.exe` que sobram depois da lista de auxiliares (proposto) ou pedir que o usuário escolha.
