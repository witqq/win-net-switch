# Подключение SignPath Foundation

[English](SIGNPATH_ONBOARDING.md) | **Русский**

Этот документ описывает действия владельца проекта. Он не разрешает агенту или contributor отправлять заявку, устанавливать GitHub App, менять доступ к репозиторию или обрабатывать token.

## Проверенная готовность проекта

WinNetSwitch — публичный, активно поддерживаемый MIT-проект с опубликованными Windows-релизами, хранящимися в репозитории build scripts, CI, тестами, пользовательской документацией, privacy policy, security policy и публичной [политикой подписи кода](../CODE_SIGNING_POLICY.ru.md). В репозитории находятся исходники проекта и распространяемые open-source зависимости с license notices; собственных проприетарных компонентов WinNetSwitch намеренно нет.

Решение о соответствии принимает SignPath Foundation. Подготовка репозитория не означает принятия заявки и не гарантирует немедленного исчезновения SmartScreen.

## Действия владельца

- Включите многофакторную аутентификацию в GitHub и SignPath.
- Прочитайте актуальные [условия SignPath Foundation](https://signpath.org/terms.html).
- Отправьте проект через [форму SignPath Foundation](https://signpath.org/), указав:
  - project: `WinNetSwitch`;
  - repository: `https://github.com/witqq/win-net-switch`;
  - latest release: `https://github.com/witqq/win-net-switch/releases/latest`;
  - license: `https://github.com/witqq/win-net-switch/blob/main/LICENSE`;
  - code-signing policy: `https://github.com/witqq/win-net-switch/blob/main/CODE_SIGNING_POLICY.md`;
  - privacy: `https://github.com/witqq/win-net-switch/blob/main/PRIVACY.md`;
  - security: `https://github.com/witqq/win-net-switch/blob/main/SECURITY.md`.
- После принятия заявки установите GitHub App SignPath только для `witqq/win-net-switch` и подключите репозиторий как trusted build system.
- В SignPath проверьте project, artifact configuration, release signing policy, trusted GitHub build system, origin verification, approver и назначенный сервисом сертификат SignPath Foundation.
- Создайте отдельного CI submitter/API token только с правом отправлять signing requests этого проекта.
- В GitHub **Settings** → **Secrets and variables** → **Actions** сохраните token как repository secret `SIGNPATH_API_TOKEN`. Никогда не вставляйте его значение в чат, issue, workflow input или исходный файл.
- Сохраните несекретные identifiers как repository variables:
  - `SIGNPATH_ORGANIZATION_ID`;
  - `SIGNPATH_PROJECT_SLUG`;
  - `SIGNPATH_SIGNING_POLICY_SLUG`;
  - `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`.
- Передайте maintainer release pipeline несекретные endpoint и identifiers; о секрете сообщите только факт его наличия, но не значение.

## Критерии приёмки интеграции

Не считайте подключение подписи завершённым, пока новый неизменяемый релиз не подтвердит всё перечисленное:

- отправленный в SignPath artifact происходит из tagged GitHub-hosted workflow;
- внутренний `WinNetSwitch.exe` подписан до встраивания в установщик;
- готовый установщик подписан после этого;
- `Get-AuthenticodeSignature` и `signtool verify /pa /all /v` успешно проверяют оба release EXE;
- обе подписи содержат RFC 3161 timestamp с SHA-256;
- контрольные суммы формируются только после подписи;
- ошибка или пропуск любого шага подписи прекращает публикацию;
- опубликованные файлы и `SHA256SUMS.txt` имеют совпадающие хеши.

При реализации pipeline используйте актуальное официальное [руководство интеграции SignPath с GitHub](https://docs.signpath.io/trusted-build-systems/github).
