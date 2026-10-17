# Changelog

Todas as mudanças relevantes do OpenCleaner. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e as versões seguem o [Versionamento Semântico](https://semver.org/lang/pt-BR/).

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

[1.0.0]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.0.0
