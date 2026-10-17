# Análise do CCleaner instalado

Análise feita sobre `C:\Program Files\CCleaner` (versão **6.41.0.11567**), apenas por
observação: estrutura de arquivos, metadados de versão, arquivos de configuração em texto,
chaves de registro do próprio usuário e strings legíveis do executável. Nenhum código do
CCleaner foi descompilado ou copiado; o OpenCleaner é uma implementação própria do mesmo
comportamento.

## Componentes

| Arquivo | Papel |
|---|---|
| `CCleaner.exe` / `CCleaner64.exe` (40–47 MB) | Aplicativo principal (32/64 bits). A interface é desenhada com **Sciter** (motor HTML/CSS embarcado; ~300 referências no binário). |
| `CCleanerPerformanceOptimizer.dll` + `...Service.exe` | "Otimizador de desempenho": serviço que coloca programas em segundo plano para "dormir". |
| `CCleanerDU.dll` | Atualizador de drivers (backend `driver-updater.ff.avast.com`). |
| `libwa*.dll`, `wa_3rd_party_host_*.exe` | **OPSWAT MDES SDK** — inventário de software instalado e vulnerabilidades, usado pelo Atualizador de software. |
| `CCleanerReactivator.*` | Reativa a licença/assinatura. |
| `CCUpdate.exe`, `ccleaner_update_helper.exe`, `ccupdate*.exe` | Atualizador e instaladores baixados. |
| `CCleanerBugReport.exe`, `CCleanerCrashDump.exe` | Relatório de erros/dumps. |
| `Lang\lang-XXXX.dll` | 60 DLLs de recursos de idioma (uma por LCID). |
| `Setup\config.def` | Configuração remota ("Shepherd"): URLs de telemetria (`analytics.ff.avast.com`), licenciamento (`alpha-*.ff.avast.com`), ofertas de produtos AVG/Avast. |
| `Data\` | Estado do atualizador de drivers, histórico, cache e backups. |

## Onde ficam as configurações

Tudo em `HKCU\Software\Piriform\CCleaner`. Cada item da Limpeza Personalizada é um valor
`(App)<Nome do item> = True/False` — foi daí que saiu a lista completa de regras
(Edge/Chrome: cache, histórico, cookies, sessão, senhas…; Windows: Lixeira, temporários,
área de transferência, despejos de memória, Chkdsk, logs, Relatório de Erros, cache DNS,
Otimização de Entrega, Prefetch, miniaturas, listas de atalhos; aplicativos: Discord, Teams,
Zoom, Office, OneDrive, NVIDIA, 7-Zip, Paint, Regedit, Área de Trabalho Remota, WhatsApp…).
Outros valores relevantes: `CookiesToSave` (lista de cookies preservados), `SkipUAC`,
`WipeFreeSpaceDrives`, `LastCleaned`, `AmountToClean`, caches JSON do atualizador de
software/drivers.

## Comportamentos identificados

* **Limpeza baseada em regras**: o binário referencia `winapp2.ini`/`winreg.ini` — o mesmo
  formato público de regras (`FileKey`, `RegKey`, `Detect`, `ExcludeKey`) que a comunidade usa.
* **Itens especiais** via API do Windows: `SHEmptyRecycleBin` (Lixeira) e
  `DnsFlushResolverCache` (DNS).
* **Limpador de registro** com categorias como "Missing Shared DLLs", "Unused File Extensions",
  "ActiveX and Class Issues", aplicativos, fontes, instaladores, inicialização e software obsoleto,
  com backup em `.reg` antes de corrigir.
* **Ferramentas**: Desinstalar, Inicialização (Run/pasta Inicializar/tarefas/menu de contexto),
  Plugins de navegador, Analisador de disco, Localizador de duplicados, Restauração do sistema,
  Limpador de unidade (Drive Wiper), Atualizador de software e de drivers.
* **Verificação de saúde** (Health Check): resumo de lixo, privacidade, inicialização e espaço.
* **Linha de comando** `/AUTO` para limpeza silenciosa.

## O que o OpenCleaner reproduz

| Recurso do CCleaner | OpenCleaner |
|---|---|
| Verificação de saúde | ✔ arquivos a limpar, registro, inicialização, atualizações e disco |
| Atualizador de software | ✔ via **winget** (catálogo oficial da Microsoft, instaladores verificados por hash) em vez do SDK da OPSWAT |
| Atualizador de drivers | ✔ via **Windows Update Agent** (drivers certificados pelos fabricantes) em vez do backend da Avast, com ponto de restauração antes |
| Limpeza personalizada (Windows/Aplicativos) | ✔ 76 regras embutidas + suporte a `winapp2.ini` |
| Detecção de programas instalados | ✔ `Detect`/`DetectFile`, só mostra o que existe |
| Aviso de navegador aberto / fechar programas | ✔ |
| Pastas personalizadas e exclusões | ✔ |
| Apenas temporários com mais de 24 h | ✔ |
| Exclusão segura | ✔ 1 passada com zeros |
| Limpador de registro com backup .reg | ✔ 7 categorias conservadoras |
| Desinstalar programas | ✔ desinstalar, excluir entrada, abrir local, exportar |
| Inicialização | ✔ ativar/desativar (mesmo mecanismo `StartupApproved` do Gerenciador de Tarefas), excluir |
| Analisador de disco | ✔ por categoria, maiores arquivos |
| Localizador de duplicados | ✔ tamanho + MD5, envia para a Lixeira |
| `/AUTO` | ✔ grava `auto.log` |

## O que ficou de fora (e por quê)

* **Otimizador de desempenho** — depende de um serviço residente e de perfis de aplicativos na
  nuvem da Gen Digital.
* **Cookies a manter** — exige editar os bancos SQLite dos navegadores; o OpenCleaner apaga
  cookies por inteiro e deixa esse item desmarcado por padrão.
* **Histórico do Firefox** — fica em `places.sqlite` junto com os favoritos; apagar o arquivo
  levaria os favoritos junto.
* **Limpador de unidade (Drive Wiper)** e **Restauração do sistema** — fora do escopo inicial.
* Telemetria, licenciamento e ofertas de outros produtos — propositalmente ausentes.
