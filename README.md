# OpenCleaner

Limpador de sistema para Windows inspirado no CCleaner: limpeza de arquivos temporários e de
navegadores, limpador de registro, atualizador de programas e de drivers, desinstalador,
gerenciador de inicialização, analisador de disco e localizador de duplicados.
Sem telemetria, sem anúncios.

Veja em [docs/ANALISE.md](docs/ANALISE.md) como o CCleaner funciona e o que foi reproduzido.

## Compilar

Não precisa instalar nada: usa o `csc.exe` do .NET Framework 4.x que já vem no Windows 10/11.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Gera `bin\OpenCleaner.exe`. O ícone do app (`assets\OpenCleaner.ico`) é gerado por
`tools\make-icon.ps1` a partir do ícone Lucide `brush-cleaning`.

## Testar

```powershell
powershell -ExecutionPolicy Bypass -File tests\run-smoke.ps1
```

O teste analisa (sem apagar) todas as regras detectadas, faz uma limpeza real numa pasta
sandbox em `%TEMP%`, consulta o winget, renderiza todos os ícones, varre o registro em modo
leitura e salva capturas de cada tela. Com `-Drivers`, também consulta o Windows Update (lento).

## Estrutura

```
src/Core/     motor: regras (formato winapp2), limpeza, registro, atualizadores, ferramentas, configurações
src/UI/       interface WinForms (uma página por seção) e renderizador dos ícones Lucide
rules/        builtin.ini — regras embutidas (editáveis, compiladas como recurso)
assets/icons/ ícones Lucide em SVG, embutidos como recurso
tests/        teste de fumaça
tools/        gerador do ícone do app
```

## Uso

* **Limpeza personalizada**: marque os itens, clique em *Analisar* para ver o que sai, depois em
  *Executar limpeza*. Clique duplo num resultado lista os arquivos.
* **Atualizar programas**: lista o que tem versão nova no winget e atualiza os marcados. Se o
  winget não estiver instalado, o app leva à página do *Instalador de Aplicativo* na Microsoft Store.
* **Atualizar drivers**: procura drivers no Windows Update e instala os marcados (exige
  administrador). Um ponto de restauração é criado antes.
* **winapp2.ini**: coloque o arquivo em `%AppData%\OpenCleaner\` para ganhar milhares de regras extras.
* **Linha de comando**: `OpenCleaner.exe /AUTO` limpa silenciosamente com os itens marcados e grava
  `%AppData%\OpenCleaner\auto.log` — útil no Agendador de Tarefas.
* Itens de `HKLM` (registro, inicialização de todos os usuários) e drivers exigem *Reiniciar como administrador*.
* Configurações de quando o app se chamava FreeCleaner são migradas automaticamente.

## Segurança

* Pastas do sistema e do perfil (Windows, System32, Arquivos de Programas, Documentos, Área de
  Trabalho, a raiz do perfil…) nunca são varridas recursivamente, nem por regras do winapp2.
* Junções e links simbólicos não são seguidos.
* Senhas, cookies, histórico e formulários vêm desmarcados e pedem confirmação.
* O limpador de registro só aponta entradas cujo arquivo comprovadamente não existe e oferece
  backup `.reg` antes de corrigir.
* Duplicados e arquivos do analisador de disco vão para a Lixeira.
* Drivers vêm desmarcados; atualizações só de fontes oficiais (winget e Windows Update).

## Créditos

Ícones: [Lucide](https://lucide.dev) v1.58.0, licença ISC — ver `assets/icons/LICENSE`.
