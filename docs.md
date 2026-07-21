# vKOROBKU — памятка по проекту

Шпаргалка для быстрого входа в контекст (для нового диалога с ассистентом или нового участника). Актуальна на v0.1.9 (июль 2026).

## Что это

Русско/англоязычное WPF-приложение (.NET 8, Windows) для прозрачного сжатия установленных игр штатным NTFS-механизмом WOF (compact.exe, алгоритмы XPRESS4K/8K/16K и LZX). Игры продолжают работать — меняется только способ хранения файлов. Репозиторий: `damnpotato430-eng/vkorobku`, лицензия GPL-3.0.

## Структура решения

```
src/
  vKOROBKU.App/      — WPF-приложение (UI, сканеры, анализ, наблюдение, очередь)
  vKOROBKU.Worker/   — отдельный процесс с повышением прав (requireAdministrator), гоняет compact.exe
  WorkerProtocol.cs         ┐
  FileSystemPrimitives.cs   ├ общие linked-файлы (Compile Include в обоих csproj), namespace vKOROBKU.Shared / vKOROBKU.Protocol
  CompressionHeuristics.cs  ┘
tests/vKOROBKU.Tests/ — xunit, ~130 тестов; на Worker ссылается с Aliases="worker" (extern alias) из-за общих типов
scripts/build-release.ps1 — сборка релизного zip
docs/release-notes-v*.md  — заметки релизов (файл обязателен до создания тега!)
```

## Ключевая архитектура

### Воркер и протокол
- App ↔ Worker: named pipe + одноразовый токен-файл (`%LOCALAPPDATA%\vKOROBKU\WorkerAuth`). Worker запускается с `runas` (UAC).
- **Сессия обслуживает очередь заданий за один запуск** (один UAC на пачку): pump читает пайп в Channel, задания и команды cancel не гоняются за одним reader'ом. `WorkerSession` (App) / цикл в `Program.Main` (Worker). Команды: `cancel` (текущее задание), `shutdown`.
- **Сообщения — коды, не текст**: воркер шлёт `WorkerCodes.*` + аргументы (`CodeValue`, `CodeArg`), текст рендерит App (`WorkerMessageText`). Поле `Text` — только фолбэк для неожиданных исключений. Ошибки валидации — `WorkerJobException(code)`.
- Распаковка — **два пасса**: `/U /EXE` (снимает WOF), затем `/U` (снимает NTFS-сжатие). Проверено эмпирически.
- Отмена: kill compact.exe детерминированно в catch (НЕ через token registration — там LIFO-гонка, был осиротевший compact).
- Инвентарь воркера исключает reparse/encrypted всегда, **sparse — только при сжатии** (compact не сжимает sparse, но распаковывать WOF+sparse обязан). Физический объём исключённых файлов идёт в `ExcludedPhysicalBytes` — App прибавляет его к отображаемому весу (Steam оставляет sparse-флаги после загрузок!).
- Resume: файл либо полностью сконвертирован, либо нет — «дожать» пропускает файлы уже в целевом WOF-алгоритме.
- Skip-list: расширения (медиа/архивы, дефолт в `CompressionSkipList`) + файлы ≤ кластера, только при включённой настройке.

### Определение состояния (важно, здесь были главные баги)
- `GameCompressionDetector`: доля WOF-байтов ≥85% → Compressed, ≥5% → Partially. В «частичной» зоне — **mercy-пасс**: файлы, которые compact законно оставил (несжимаемые, ≤ chunk-size), не считаются проблемой. Проба несжимаемости (`CompressionHeuristics.IsLikelyIncompressible`) моделирует экономику WOF: мегабайт из **середины** файла, независимые чанки размера алгоритма, выгода только при пересечении 4К-кластерных границ, порог 15% (Deflate оптимистичнее XPRESS). Крупные файлы пробуются первыми, ранние выходы.
- Верификатор воркера (`CompressionResultVerifier`) использует те же общие эвристики — детектор и верификатор обязаны совпадать, иначе игры «пинг-понгают» между состояниями.

### Наблюдение (watcher)
- `watcher.json`, координатор `WatchedGamesCoordinator`. Baseline после сжатия = PhysicalAfter − SkipListedPhysical (— «вселенная воркера»); сканер проверки (`FolderSizeScanner`) исключает ровно то же (sparse/encrypted/skip-ext/≤кластера) — рассинхрон этих двух методик = фантомное «нужно дожать» (были HL2/Metro-инциденты).
- Decay = (checked − baseline) / (uncompressed − baseline); порог и мин. выгода в настройках. TTL перепроверки 7 дней, форс при смене build id (слот общий для Steam/Epic/GOG).
- «Требуют дожатия» (плашка/фильтр/«Дожать все») = деградировавшие наблюдаемые **∪** частично-сжатые карточки с resumable-алгоритмом (прерванные сжатия ещё не в watcher!).
- Проверка умеет и понижать карточку (деградация), и **повышать обратно** (decay=0 + совпадающий алгоритм).

### Локализация
- resx: `Strings.resx` — английская база (neutral, `NeutralResourcesLanguage("en")`), `Strings.ru.resx` — сателлит. **Новый язык = один файл** `Strings.xx.resx`.
- `Strings.cs` — рукописный типизированный аксессор (генератор VS в CLI не работает). Дрейф ловят тесты `LocalizationTests`: паритет ключей, непустота, **паритет плейсхолдеров {N}**.
- Выбор языка: Настройки → авто/ru/en, применяется при старте (`App.ApplyLanguagePreference`, ставит и UICulture, и Culture), рестарт обязателен.
- Логика никогда не сравнивает переведённые строки: комбобоксы — `ChoiceOption(Id, DisplayText)`, маркер ручных игр — инвариант `GameInfo.ManualSource = "Manual"` (+ `SourceText` для показа). AppLog — намеренно по-русски (диагностика).

### Сканеры и обложки
- Steam (acf), Epic (ProgramData манифесты .item), GOG (реестр `GOG.com\Games`, DLC отсекаются по dependsOn), Ubisoft (`Ubisoft\Launcher\Installs` + Uplay uninstall-имена) и EA (ключи `EA Games`/`Origin Games`; шифрованную базу EA app не трогаем) — **Ubisoft/EA не проверены вживую, экспериментальные**.
- Ручная папка → `Manual`; если позже найдена сканером — ручная запись удаляется, карточка «повышается».
- Обложки (`CoverService`): Steam CDN по appid → GOG API по productId (`api.gog.com/v2/games/{id}` → boxArtImage) → поиск по имени в Steam storesearch. Негативный кеш 7 дней только при подтверждённом промахе (сетевые ошибки пробрасываются). Кеш в `Covers/`.

### Данные (%LOCALAPPDATA%\vKOROBKU)
`preferences.json`, `watcher.json`, `compression-status.json` (статусы карточек, TTL доверия 6ч), `stats.json` (накопительная статистика «за всё время», только прибавление), `manual-games.json`, `hidden-games.json` (скрытые DirectStorage-игры — пути; скрытие переживает рескан, «Вернуть все» в настройках), журнал операций, кеш анализа, `Covers/`, `logs/` (app-*.log + crash-*.log).

## Рабочие соглашения (критично!)

- **Релизы — только по явной команде пользователя после его локального теста.**
- Нетривиальное — через feature-ветку + PR + зелёный CI (squash-merge, линейная история); мелочь можно прямо в main.
- **Файлы с кириллицей редактировать только Edit-тулом** — PowerShell Get-Content/Set-Content ломает UTF-8 без BOM (мojibake). Безопасная альтернатива для вставок: `[IO.File]::ReadAllText/WriteAllText` с UTF8(false).
- PS 5.1: двойные кавычки в аргументах native-команд ломаются → коммит-сообщения через `git commit -F файл` или Bash-heredoc.
- bin/Debug лочится запущенным приложением — не убивать процесс пользователя; ждать закрытия (Monitor) и пересобирать. **Не использовать общий `-p:OutDir` для обхода** — тесты получают протухшую Worker.dll из обычного bin.
- Локальный .NET SDK 8.0.422 лежит в scratchpad (`$env:DOTNET_ROOT = <scratchpad>\dotnet`); системного нет.
- Интеграционный тест воркера запускает его через `dotnet vKOROBKU.Worker.dll` (муксер обходит requireAdministrator-манифест apphost'а) — работает локально без UAC и в CI.
- git push иногда падает по порту 22 → фолбэк `ssh://git@ssh.github.com:443/...`.

## Сборка / релиз

```powershell
$env:DOTNET_ROOT = "<scratchpad>\dotnet"
& "$env:DOTNET_ROOT\dotnet.exe" build vKOROBKU.sln
& "$env:DOTNET_ROOT\dotnet.exe" test tests\vKOROBKU.Tests\vKOROBKU.Tests.csproj --no-build
# запуск: src\vKOROBKU.App\bin\Debug\net8.0-windows10.0.19041.0\vKOROBKU.exe
```

Релиз: поднять версию в **трёх** местах (App.csproj, Worker.csproj, build-release.ps1) + создать `docs/release-notes-vX.Y.Z.md` → коммит «Prepare vX.Y.Z release» → тег `vX.Y.Z` → push тега → release.yml сам тестирует, собирает zip+sha256 и публикует (0.* → prerelease автоматически).

## Роадмап

Ближайшие кандидаты (по приоритету):
1. **Настройки на игру** — «не сжимать никогда», «закрепить алгоритм».
2. **Сортировка «Лучшие кандидаты»** — по потенциальной экономии.
3. **Подпись кода + winget** — против SmartScreen; нужно решение пользователя по сертификату/Azure Trusted Signing.
4. **.NET 10 LTS** — обязательно до ноября 2026 (EOL .NET 8); сама миграция быстрая, риск только в новых warnings (TreatWarningsAsErrors включён).

Мелочи: проверить Ubisoft/EA на живой установке и снять пометку «экспериментально» (README + release notes), скриншот README (можно с англ. UI), Battle.net-сканер по запросу, косметика — кеш анализа хранит «Точность/Прогноз» строкой на языке момента расчёта (лечится повторным анализом; чистое решение — код вместо строки в `SavedGameAnalysis`).

Отклонено навсегда (решение пользователя): авто-дожатие по расписанию (Планировщик Windows), трей, toast-уведомления, FSW-мониторинг в реальном времени, сравнения с конкурентами в текстах, HLTB как источник обложек, IGDB (выпилен).
