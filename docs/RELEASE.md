# Выпуск релиза

## Автоматический выпуск (основной путь)

1. Обновить `<Version>`, `<FileVersion>` и `<InformationalVersion>` в `src/vKOROBKU.App/vKOROBKU.App.csproj` и `src/vKOROBKU.Worker/vKOROBKU.Worker.csproj`, а также версию по умолчанию в `scripts/build-release.ps1`.
2. Написать `docs/release-notes-v<версия>.md` — без этого файла workflow остановится.
3. Закоммитить изменения и запушить в `main`.
4. Создать и запушить тег:

   ```powershell
   git tag -a v<версия> -m "vKOROBKU v<версия>"
   git push origin v<версия>
   ```

Workflow `.github/workflows/release.yml` прогонит тесты, соберёт self-contained пакет, вычислит SHA-256 и опубликует GitHub Release с ZIP, checksum-файлом и release notes. Версии `0.*` автоматически помечаются prerelease.

## Ручная сборка (запасной путь)

```powershell
./scripts/build-release.ps1 -Version <версия>
```

Скрипт создаёт в `artifacts/`:

- self-contained `vKOROBKU.exe`;
- скрытый UAC-модуль `vKOROBKU.Worker.exe`;
- ZIP-пакет Windows x64;
- SHA-256 checksum;
- Markdown-фрагмент `<пакет>-checksum.md` для release notes.

Пользователю необходимо держать оба EXE в одной папке. Установка .NET Runtime не требуется.

При ручной публикации: создать тег `v<версия>`, создать GitHub Release, прикрепить ZIP и `.sha256`, добавить содержимое `<пакет>-checksum.md` в release notes и отметить prerelease, пока проект не прошёл широкое тестирование на играх.
