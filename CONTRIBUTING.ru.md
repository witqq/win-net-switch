# Участие в разработке

[English](CONTRIBUTING.md) | **Русский**

Спасибо за интерес к WinNetSwitch.

Перед изменением кода ознакомьтесь с [правилами сообщества](CODE_OF_CONDUCT.ru.md) и проверьте [существующие Issues](https://github.com/witqq/win-net-switch/issues). Для ошибки используйте [русскую форму bug report](https://github.com/witqq/win-net-switch/issues/new?template=bug_report_ru.yml), для новой возможности — [русскую форму feature request](https://github.com/witqq/win-net-switch/issues/new?template=feature_request_ru.yml). Уязвимости не публикуйте в Issues: следуйте [политике безопасности](SECURITY.ru.md).

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

## Pull request

1. Создайте отдельную ветку от актуальной `main`.
2. Сделайте одно логически завершённое изменение вместе с тестами и документацией.
3. Выполните команды проверки выше.
4. Откройте pull request и заполните checklist шаблона.
5. Дождитесь зелёного GitHub Actions workflow `CI`.

В pull request не включайте сгенерированные `artifacts`, локальные логи и файлы окружения. Инструкция для сопровождающего по выпуску версии находится в [docs/RELEASING.ru.md](docs/RELEASING.ru.md).
