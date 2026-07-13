# Выпуск релиза

## Сборка portable-пакета

```powershell
./scripts/build-release.ps1 -Version 0.1.2
```

Скрипт создаёт в `artifacts/`:

- self-contained `vKOROBKU.exe`;
- скрытый UAC-модуль `vKOROBKU.Worker.exe`;
- ZIP-пакет Windows x64;
- SHA-256 checksum;
- Markdown-фрагмент `<пакет>-checksum.md` для release notes.

Пользователю необходимо держать оба EXE в одной папке. Установка .NET Runtime не требуется.

## GitHub Release

1. Создать тег `v<версия>` на проверенном коммите.
2. Создать GitHub Release для этого тега.
3. Прикрепить ZIP и `.sha256` из `artifacts/`.
4. Добавить содержимое `<пакет>-checksum.md` в release notes, чтобы SHA-256 был виден на странице релиза.
5. Отметить prerelease, пока проект не прошёл широкое тестирование на играх.
