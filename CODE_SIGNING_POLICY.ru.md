# Политика подписи кода

[English](CODE_SIGNING_POLICY.md) | **Русский**

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Подписываемые артефакты

Политика распространяется только на официальные `WinNetSwitch.exe` и `WinNetSwitch-Setup.exe`, которые GitHub Actions собирает из этого публичного репозитория для неизменяемого version tag. Отдельный пакет Stream Deck проходит валидацию и получает контрольную сумму, но не является Authenticode executable.

Подпись должна сохранять следующий порядок сборки:

- собрать и проверить `WinNetSwitch.exe`;
- подписать и проверить `WinNetSwitch.exe`;
- встроить подписанный executable в `WinNetSwitch-Setup.exe`;
- подписать и проверить `WinNetSwitch-Setup.exe`;
- сформировать контрольные суммы и опубликовать релиз без изменения подписанных файлов.

Подпись не действует, пока SignPath Foundation не примет проект, а release workflow не получит одобренную интеграцию SignPath. До этого документация релизов прямо указывает, что binaries не подписаны.

## Роли проекта

- Committer и reviewer: [witqq](https://github.com/witqq).
- Approver подписи: [witqq](https://github.com/witqq).

Изменения других участников перед merge проверяет maintainer. Запрос release-подписи утверждает approver; запрос должен происходить из GitHub-hosted release workflow этого репозитория и соответствовать version tag.

## Меры безопасности

- Maintainer и approver используют многофакторную аутентификацию GitHub и SignPath.
- GitHub App SignPath получает доступ только к этому репозиторию.
- Release signing принимает только артефакты, для которых SignPath подтвердил исходный код и происхождение GitHub Actions build.
- Credentials подписи хранятся только в GitHub Actions secrets. Токены и закрытые ключи нельзя помещать в исходники, workflow inputs, build logs, issues, pull requests, чат или release assets.
- Ошибка подписи прекращает публикацию. Workflow не должен публиковать неподписанный файл в релизе, заявленном как подписанный.
- Уязвимости передаются приватно по [Security Policy](SECURITY.ru.md).

## Конфиденциальность

WinNetSwitch не передаёт сведения другим сетевым системам, если пользователь явно не запросил сетевое действие, например открытие GitHub-ссылки загрузки или поддержки. Обычное управление адаптерами, локальный IPC, настройки и диагностические логи остаются на Windows-компьютере пользователя. См. полную [политику конфиденциальности](PRIVACY.ru.md).

Когда maintainers используют GitHub и SignPath, эти сервисы самостоятельно обрабатывают сведения о сборке, репозитории, identity и signing request по собственным условиям конфиденциальности.

## Проверка релиза

После включения подписи пользователь может открыть у EXE **Свойства** → **Цифровые подписи** либо выполнить:

```powershell
Get-AuthenticodeSignature .\WinNetSwitch.exe |
    Select-Object Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

Статус подписи должен быть `Valid`. Дополнительно сравните SHA-256 файла со значением в release-файле `SHA256SUMS.txt`.
