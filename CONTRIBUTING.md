# Участие в разработке

Спасибо за интерес к WinNetSwitch.

## Подготовка окружения

Для сборки нужны Windows 10/11 и .NET SDK версии из `global.json`. В проекте нет сторонних NuGet-зависимостей.

```powershell
dotnet restore .\WinNetSwitch.slnx
dotnet build .\WinNetSwitch.slnx --configuration Release --no-restore
dotnet run --project .\tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj --configuration Release --no-restore
```

Полная проверка, включая self-contained публикацию, нативный tray smoke-test и проверку payload установщика, запускается в PowerShell с правами администратора:

```powershell
.\scripts\verify.ps1
```

## Изменения

- Не добавляйте секреты, реальные сетевые идентификаторы, пользовательские пути и диагностические логи.
- Для изменения сетевой логики добавляйте тест, который отличает требуемое поведение от внешне похожего ошибочного состояния.
- Не отключайте проверки итогового состояния и транзакционный откат ради ускорения операции.
- Перед pull request выполните Release-сборку и тесты.

Сообщения коммитов оформляйте в повелительном стиле с префиксом `feat:`, `fix:`, `docs:`, `test:`, `build:` или `chore:`.
