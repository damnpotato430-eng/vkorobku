# Выпуск релиза

## Сборка portable-пакета

```powershell
./scripts/build-release.ps1 -Version 0.1.1
```

Скрипт создаёт в `artifacts/`:

- self-contained `vKOROBKU.exe`;
- скрытый UAC-модуль `vKOROBKU.Worker.exe`;
- ZIP-пакет Windows x64;
- SHA-256 checksum.

Пользователю необходимо держать оба EXE в одной папке. Установка .NET Runtime не требуется.

## GitHub Release

1. Создать тег `v<версия>` на проверенном коммите.
2. Создать GitHub Release для этого тега.
3. Прикрепить ZIP и `.sha256` из `artifacts/`.
4. Отметить prerelease, пока проект не прошёл широкое тестирование на играх.
