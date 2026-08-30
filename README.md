# WinNetSwitch

[![CI](https://github.com/witqq/win-net-switch/actions/workflows/ci.yml/badge.svg)](https://github.com/witqq/win-net-switch/actions/workflows/ci.yml)
[![GitHub release](https://img.shields.io/github/v/release/witqq/win-net-switch)](https://github.com/witqq/win-net-switch/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

WinNetSwitch — небольшое приложение для Windows 10/11, которое живёт в системном трее и управляет физическими сетевыми адаптерами. Оно позволяет независимо включать и выключать Wi‑Fi, Ethernet и другие физические интерфейсы либо оставить включённым только один выбранный адаптер.

## Скачать

| Файл | Назначение |
|---|---|
| [WinNetSwitch-Setup.exe](https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch-Setup.exe) | Рекомендуемый установщик с автозапуском и штатным удалением |
| [WinNetSwitch.exe](https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch.exe) | Переносимая self-contained версия без установки и автозапуска |
| [SHA256SUMS.txt](https://github.com/witqq/win-net-switch/releases/latest/download/SHA256SUMS.txt) | Контрольные суммы опубликованных файлов |

Все версии и примечания к ним находятся на странице [GitHub Releases](https://github.com/witqq/win-net-switch/releases).

## Установка

1. Скачайте `WinNetSwitch-Setup.exe` по ссылке выше.
2. При желании проверьте SHA-256 файла по инструкции ниже.
3. Запустите установщик и подтвердите запрос контроля учётных записей Windows (UAC).
4. Подтвердите установку в окне WinNetSwitch.
5. После завершения найдите значок приложения в системном трее. Windows может поместить его в область скрытых значков рядом с часами.

Установщик:

- копирует приложение в `%LOCALAPPDATA%\Programs\WinNetSwitch`;
- добавляет ярлык в меню «Пуск»;
- регистрирует приложение в Windows «Установленные приложения»;
- создаёт задачу автозапуска при входе текущего пользователя;
- сразу запускает приложение в интерактивной пользовательской сессии.

### Предупреждение SmartScreen

Релизные файлы пока не подписаны коммерческим сертификатом подписи кода. Поэтому Microsoft Defender SmartScreen может показать предупреждение о неизвестном издателе.

Продолжайте запуск только если файл скачан из [официального GitHub Release](https://github.com/witqq/win-net-switch/releases/latest) и его SHA-256 совпадает с `SHA256SUMS.txt`. После проверки в окне SmartScreen можно выбрать «Подробнее» → «Выполнить в любом случае».

### Проверка SHA-256

Положите установщик и `SHA256SUMS.txt` в один каталог, откройте там PowerShell и выполните:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt |
    Where-Object { $_ -match '  WinNetSwitch-Setup\.exe$' } |
    ForEach-Object { ($_ -split '\s+')[0] })
$actual = (Get-FileHash .\WinNetSwitch-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
$actual -eq $expected
```

Результат `True` означает, что контрольная сумма совпала. Для переносимой версии замените имя файла на `WinNetSwitch.exe`.

## Использование

После запуска отдельное главное окно не открывается — WinNetSwitch работает только через значок в трее.

1. Щёлкните значок WinNetSwitch правой кнопкой.
2. Наведите указатель на нужный адаптер.
3. Выберите одно из действий:
   - `Включить` или `Выключить` меняет только выбранный адаптер;
   - `Включить только этот адаптер` включает выбранный и отключает все остальные физические адаптеры.

Галочка рядом с адаптером показывает итоговое включённое состояние. Для Wi‑Fi учитывается и состояние устройства, и программный переключатель Wi‑Fi radio.

Список обновляется в фоне. Пока меню раскрыто, его пункты не пересоздаются и фокус не сбрасывается; новое состояние становится видно после закрытия и повторного открытия меню. Команда `Обновить` и двойной щелчок по значку запускают ручное обновление.

> **Важно:** отключение активного адаптера немедленно разрывает его сетевые соединения, загрузки и удалённые сессии. Команда `Включить только этот адаптер` намеренно отключает все остальные физические интерфейсы. Не используйте её для адаптера, через который вы удалённо управляете компьютером.

## Как работает Wi‑Fi

WinNetSwitch объединяет активные физические интерфейсы из `Get-NetAdapter -Physical` с административно отключёнными PCI/USB сетевыми устройствами Plug and Play (PnP). Поэтому Wi‑Fi остаётся в списке даже после отключения адаптера и перезагрузки. Виртуальные интерфейсы VPN, Hyper-V, WSL и других программ намеренно не показываются и не изменяются.

При включении Wi‑Fi приложение:

1. включает отключённое PnP-устройство, если оно исчезло из `Get-NetAdapter`;
2. дожидается появления сетевого интерфейса;
3. включает software radio через штатный Windows Native Wi-Fi API;
4. проверяет итоговое состояние.

При выключении сначала отключается software radio, затем сам адаптер. Если операция завершилась после частичного изменения, приложение пытается восстановить исходное состояние и записывает результат в лог.

Native Wi-Fi API не может снять аппаратную блокировку. Режим полёта, аппаратная кнопка, отключённое в BIOS радио или политика организации могут помешать включению Wi‑Fi. Включённый адаптер также не гарантирует подключение к точке доступа: сохранённую Wi‑Fi-сеть выбирает Windows.

## Диагностика

Лог приложения находится здесь:

```text
%LOCALAPPDATA%\WinNetSwitch\logs\WinNetSwitch.log
```

Открыть его можно командой `Открыть лог ошибок` в tray-меню. При достижении 1 MiB предыдущий лог переносится в `WinNetSwitch.previous.log`.

| Проблема | Что проверить |
|---|---|
| Значка нет после установки | Откройте скрытые значки рядом с часами; затем попробуйте ярлык WinNetSwitch в меню «Пуск» |
| Сообщение «WinNetSwitch уже запущен» | Работает существующий экземпляр; найдите его значок в трее |
| Адаптер отсутствует в списке | Показываются только физические адаптеры; виртуальные VPN/Hyper-V/WSL интерфейсы исключены намеренно |
| Wi‑Fi-адаптер включился, но Wi‑Fi остался выключен | Проверьте режим полёта, аппаратный переключатель, BIOS и политики устройства; затем откройте лог |
| Wi‑Fi включён, но подключения к сети нет | Подключение к сохранённой точке доступа выполняет Windows, а не WinNetSwitch |
| Переключение занимает несколько секунд | Приложение ждёт подтверждения Windows и проверяет итоговое состояние; детали операции доступны в логе |
| Меню не изменилось во время обновления | Это ожидаемо: открытое меню сохраняется без моргания и применяет новое состояние после закрытия |
| Операция завершилась ошибкой | Откройте `Открыть лог ошибок`, повторите действие и приложите обезличенный фрагмент к bug report |

Если проблема воспроизводится, создайте [bug report](https://github.com/witqq/win-net-switch/issues/new?template=bug_report.yml). Не публикуйте пароли, токены, MAC-адреса, имена Wi‑Fi-сетей и другие персональные сведения.

## Удаление

Откройте «Параметры» → «Приложения» → «Установленные приложения», найдите WinNetSwitch и выберите «Удалить».

Деинсталлятор удаляет приложение, задачу автозапуска, ярлык, регистрацию в списке приложений и диагностические логи. Переносимая версия не регистрирует деинсталлятор: для неё достаточно завершить WinNetSwitch через tray-меню и удалить скачанный EXE. Логи переносимой версии при необходимости удаляются отдельно из `%LOCALAPPDATA%\WinNetSwitch`.

## Требования и ограничения

- Windows 10 версии 2004 (build 19041) или новее либо Windows 11;
- права локального администратора для управления адаптерами;
- Windows PowerShell 5.1 и встроенный модуль `NetAdapter`;
- готовый публичный Release предназначен для Windows x64; ARM64-сборку можно создать локально через `scripts\publish.ps1 -Runtime win-arm64`;
- установленный .NET runtime не требуется: приложение публикуется как self-contained.

Для сборки из исходников используется поддерживаемая LTS-версия .NET 10. В проекте нет сторонних NuGet-зависимостей.

## Разработка

Решение состоит из пяти проектов:

- `src\WinNetSwitch.Core` — модели, PowerShell runner, Native Wi-Fi API и транзакционная логика;
- `src\WinNetSwitch.App` — Windows Forms `ApplicationContext`, `NotifyIcon` и tray-меню;
- `src\WinNetSwitch.Windows` — установка, удаление, ярлык и Task Scheduler autostart;
- `src\WinNetSwitch.Setup` — self-contained GUI-установщик со встроенным payload;
- `tests\WinNetSwitch.Tests` — исполняемые тесты без внешнего test framework.

На Windows с SDK из `global.json` выполните:

```powershell
dotnet restore .\WinNetSwitch.slnx
dotnet build .\WinNetSwitch.slnx --configuration Release --no-restore
dotnet run --project .\tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj --configuration Release --no-restore
```

Полная локальная проверка требует elevated PowerShell и выполняет Release-сборку, 15 тестов, self-contained публикацию, read-only probe реальных адаптеров, нативный tray smoke-test и проверку payload установщика:

```powershell
.\scripts\verify.ps1
```

Для среды без интерактивного рабочего стола используйте `-SkipSmoke`. Готовые файлы создаются в `artifacts\publish\win-x64` и `artifacts\setup\win-x64`.

GitHub Actions автоматически выполняет [CI](https://github.com/witqq/win-net-switch/actions/workflows/ci.yml) для `main` и pull request. Тег `vMAJOR.MINOR.PATCH` запускает [Release workflow](https://github.com/witqq/win-net-switch/actions/workflows/release.yml), который повторно собирает проект в облаке и публикует EXE, установщик и SHA-256.

Подробности:

- [как предложить изменение](CONTRIBUTING.md);
- [как выпустить новую версию](docs/RELEASING.md);
- [как сообщить об уязвимости](SECURITY.md);
- [лицензия MIT](LICENSE).

## Поддержка проекта

- Ошибка: [создать bug report](https://github.com/witqq/win-net-switch/issues/new?template=bug_report.yml)
- Идея: [предложить улучшение](https://github.com/witqq/win-net-switch/issues/new?template=feature_request.yml)
- Уязвимость: используйте приватный [GitHub Security Advisory](https://github.com/witqq/win-net-switch/security/advisories/new), а не публичную Issue

Проект распространяется по лицензии [MIT](LICENSE).
