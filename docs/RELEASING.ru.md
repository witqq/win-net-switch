# Выпуск новой версии

[English](RELEASING.md) | **Русский**

Релиз WinNetSwitch создаётся GitHub Actions из существующего аннотированного Git-тега `vMAJOR.MINOR.PATCH`. Workflow не изменяет исходный код и прекращает работу, если версия тега не совпадает с `Directory.Build.props`.

## Подготовка

1. Переключитесь на `main`, получите последние изменения и убедитесь, что рабочее дерево чистое.
2. Выберите новую версию по правилам Semantic Versioning.
3. Одинаково обновите версию в четырёх источниках:
   - `Directory.Build.props` — `Version`;
   - `src\WinNetSwitch.Windows\InstallationPaths.cs` — `InstallationPaths.Version`;
   - `src\WinNetSwitch.App\app.manifest` — `assemblyIdentity`;
   - `src\WinNetSwitch.Setup\app.manifest` — `assemblyIdentity`.
4. Обновите пользовательскую документацию, если поведение или требования изменились.
5. Если изменился плагин Stream Deck, увеличьте его четырёхкомпонентную версию в `stream-deck-plugin\dev.witqq.win-net-switch.sdPlugin\manifest.json`. Эта версия независима от версии companion-приложения.

Положительный поиск должен вернуть новую версию во всех ожидаемых файлах:

```powershell
rg -n "1\.4\.1" Directory.Build.props src\WinNetSwitch.Windows src\WinNetSwitch.App\app.manifest src\WinNetSwitch.Setup\app.manifest
```

Замените `1.4.1` на фактическую версию релиза.

## Проверка и публикация

В elevated PowerShell выполните полную локальную проверку:

```powershell
.\scripts\verify.ps1
```

Затем закоммитьте изменение версии, отправьте `main` и дождитесь зелёного workflow `CI`. Только после этого создавайте тег:

```powershell
git tag -a v1.4.1 -m "WinNetSwitch 1.4.1"
git push origin v1.4.1
```

Release workflow:

1. проверяет формат тега и версию проекта;
2. восстанавливает .NET- и npm-зависимости, собирает решение и плагин, запускает все тесты и native smoke checks;
3. создаёт self-contained `WinNetSwitch.exe`, `WinNetSwitch-Setup.exe` и проверенный пакет `dev.witqq.win-net-switch.streamDeckPlugin`;
4. формирует `SHA256SUMS.txt`;
5. публикует GitHub Release с автоматически созданными примечаниями.

## Проверка опубликованного релиза

На странице Release убедитесь, что опубликованы ровно четыре файла:

- `WinNetSwitch-Setup.exe`;
- `WinNetSwitch.exe`;
- `dev.witqq.win-net-switch.streamDeckPlugin`;
- `SHA256SUMS.txt`.

Скачайте `SHA256SUMS.txt` и сравните его значения с SHA-256 обоих EXE и пакета Stream Deck. Запустите `scripts\test-stream-deck-package.ps1` для скачанного плагина и убедитесь, что companion executable не встроен. Проверьте, что Release не отмечен как draft или prerelease и указывает на ожидаемый тег.

Не перемещайте и не переиспользуйте уже опубликованный тег. Если в релизе обнаружена ошибка, исправьте источник, увеличьте patch-версию и выпустите новый тег.
