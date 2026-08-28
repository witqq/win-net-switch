# WinNetSwitch

WinNetSwitch — небольшое приложение для Windows 10/11, которое живёт в системном трее и одним щелчком оставляет включённым ровно один выбранный **физический** сетевой адаптер. Типичный сценарий — быстро переключаться между Wi‑Fi и проводным Ethernet.

## Как работает переключение

Приложение получает физические адаптеры штатной командой Windows `Get-NetAdapter -Physical`. Виртуальные адаптеры VPN, Hyper-V, WSL и других программ намеренно не показываются и не отключаются.

При выборе адаптера WinNetSwitch:

1. включает выбранный адаптер, если он отключён;
2. повторно проверяет, что Windows действительно его включила;
3. отключает остальные включённые физические адаптеры;
4. проверяет, что включён только выбранный адаптер.

Если операция завершается ошибкой после частичного изменения, приложение пытается восстановить исходный набор включённых адаптеров и показывает результат восстановления в уведомлении.

> Переключение физического адаптера разрывает текущие сетевые соединения, загрузки и удалённые сессии. Не используйте приложение для переключения адаптера, через который вы удалённо управляете компьютером. Включённый Wi‑Fi-адаптер не гарантирует подключение к точке доступа: выбор Wi‑Fi-сети остаётся за Windows.

## Требования

- Windows 10 версии 2004 (build 19041) или новее либо Windows 11;
- права локального администратора: Windows требует elevation для `Enable-NetAdapter` и `Disable-NetAdapter`;
- Windows PowerShell 5.1 и встроенный модуль `NetAdapter`;
- .NET 10 SDK только для сборки. Self-contained публикация не требует установленного .NET runtime.

Проект использует поддерживаемую LTS-версию .NET 10 и не содержит сторонних NuGet-зависимостей.

## Быстрый запуск

Откройте PowerShell в корне проекта и создайте один self-contained executable:

```powershell
.\scripts\publish.ps1
```

Готовый файл находится здесь:

```text
artifacts\publish\win-x64\WinNetSwitch.exe
```

Установить его в `%LOCALAPPDATA%\Programs\WinNetSwitch` и добавить ярлык в меню «Пуск» можно командой:

```powershell
.\scripts\install.ps1
```

Чтобы сразу запустить установленную версию, добавьте `-Start`; Windows покажет запрос UAC:

```powershell
.\scripts\install.ps1 -Start
```

Запустите `WinNetSwitch.exe` и подтвердите запрос контроля учётных записей Windows (UAC). Отдельное окно не открывается: значок появляется в системном трее. Если Windows скрыла значок, откройте область скрытых значков рядом с часами.

Щёлкните значок правой кнопкой и выберите нужный адаптер. Галочка показывает включённый адаптер. Один щелчок запускает эксклюзивное переключение; пока оно выполняется, повторные действия заблокированы. В меню также есть команды `Обновить` и `Выход`. Двойной щелчок по значку обновляет список.

## Разработка и проверка

В решении три проекта:

- `src\WinNetSwitch.Core` — модели, безопасный PowerShell runner и транзакционная логика переключения;
- `src\WinNetSwitch.App` — Windows Forms `ApplicationContext`, `NotifyIcon` и меню без главной формы;
- `tests\WinNetSwitch.Tests` — исполняемые тесты без внешнего test framework.

Базовые команды:

```powershell
dotnet restore .\WinNetSwitch.slnx
dotnet build .\WinNetSwitch.slnx --configuration Release --no-restore
dotnet run --project .\tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj --configuration Release --no-restore
```

Тесты используют только `FakePowerShellRunner`, не требуют прав администратора и не читают или меняют реальные сетевые адаптеры.

Полная Windows-проверка выполняет restore, Release-сборку, 11 автоматических тестов, self-contained публикацию `win-x64`, безопасное чтение реального списка физических адаптеров через production service и нативный tray smoke-test:

```powershell
# Запустить из PowerShell с правами администратора.
.\scripts\verify.ps1
```

Если SDK установлен изолированно, путь к CLI можно передать без изменения системного `PATH`:

```powershell
.\scripts\verify.ps1 -DotNet C:\path\to\dotnet.exe
```

Adapter probe выполняет только `Get-NetAdapter -Physical`, проверяет production JSON/parser-контракт и не меняет состояние сети. Smoke-режим создаёт нативные WinForms message loop, иконку, `NotifyIcon` и меню из встроенных fake-адаптеров, подтверждает отсутствие главной формы и завершает работу. Ни одна из этих проверок не выполняет `Enable-NetAdapter` или `Disable-NetAdapter`. Для CI без интерактивного рабочего стола можно пропустить обе Windows runtime-проверки:

```powershell
.\scripts\verify.ps1 -SkipSmoke
```

Для Windows on ARM публикация создаётся отдельно:

```powershell
.\scripts\publish.ps1 -Runtime win-arm64
```
