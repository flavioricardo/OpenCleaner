# FreeCleaner

Limpador de sistema para Windows inspirado no CCleaner: limpeza de arquivos temporários e de
navegadores, limpador de registro, desinstalador, gerenciador de inicialização, analisador de
disco e localizador de duplicados. Sem telemetria, sem anúncios.

Veja em [docs/ANALISE.md](docs/ANALISE.md) como o CCleaner funciona e o que foi reproduzido.

## Compilar

Não precisa instalar nada: usa o `csc.exe` do .NET Framework 4.x que já vem no Windows 10/11.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Gera `bin\FreeCleaner.exe` (ícone em `assets\`, gerado por `tools\make-icon.ps1`).

## Testar

```powershell
powershell -ExecutionPolicy Bypass -File tests\run-smoke.ps1
```

O teste analisa (sem apagar) todas as regras detectadas, faz uma limpeza real numa pasta
sandbox em `%TEMP%`, varre o registro em modo leitura e salva capturas de cada tela.

## Estrutura

```
src/Core/   motor: regras (formato winapp2), limpeza, registro, ferramentas, configurações
src/UI/     interface WinForms (uma página por seção)
rules/      builtin.ini — regras embutidas (editáveis, compiladas como recurso)
tests/      teste de fumaça
```

## Uso

* **Limpeza personalizada**: marque os itens, clique em *Analisar* para ver o que sai, depois em
  *Executar limpeza*. Clique duplo num resultado lista os arquivos.
* **winapp2.ini**: coloque o arquivo em `%AppData%\FreeCleaner\` para ganhar milhares de regras extras.
* **Linha de comando**: `FreeCleaner.exe /AUTO` limpa silenciosamente com os itens marcados e grava
  `%AppData%\FreeCleaner\auto.log` — útil no Agendador de Tarefas.
* Itens de `HKLM` (registro, inicialização de todos os usuários) exigem *Reiniciar como administrador*.

## Segurança

* Pastas do sistema e do perfil (Windows, System32, Arquivos de Programas, Documentos, Área de
  Trabalho, a raiz do perfil…) nunca são varridas recursivamente, nem por regras do winapp2.
* Junções e links simbólicos não são seguidos.
* Senhas, cookies, histórico e formulários vêm desmarcados e pedem confirmação.
* O limpador de registro só aponta entradas cujo arquivo comprovadamente não existe e oferece
  backup `.reg` antes de corrigir.
* Duplicados e arquivos do analisador de disco vão para a Lixeira.
