# Плагин Stream Deck и публикация в Marketplace

[English](STREAM_DECK.md) | **Русский**

## Архитектура и обязательная зависимость

Плагин Stream Deck — непривилегированный Node.js-клиент. Он отправляет ограниченные JSON-запросы elevated tray-приложению WinNetSwitch через локальный named pipe `WinNetSwitch.Control.v1`. Pipe разрешает доступ только SID текущего интерактивного входа Windows, использует mandatory integrity level `medium`, чтобы обычный процесс Stream Deck мог подключиться, и не открывает доступ для `Everyone`, другого сеанса входа или удалённого logon. Обнаружение и изменение адаптеров остаётся в `PhysicalNetworkAdapterService`; сам плагин не запускает PowerShell и не вызывает сетевые Windows API напрямую.

WinNetSwitch — обязательное, отдельно устанавливаемое companion-приложение. Пакет `.streamDeckPlugin` не должен содержать `WinNetSwitch.exe`, установщик, DLL, MSI, PowerShell, batch или command script. Property Inspector и manifest плагина содержат ссылки:

- [скачать WinNetSwitch](https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch-Setup.exe);
- [поддержка и bug reports](https://github.com/witqq/win-net-switch/issues);
- [политика конфиденциальности](../PRIVACY.ru.md).

## Установка пользователем

1. Установите и запустите актуальный WinNetSwitch. Его значок должен присутствовать в трее.
2. Установите Stream Deck 7.1 или новее на Windows 10 или новее.
3. Скачайте `dev.witqq.win-net-switch.streamDeckPlugin` из последнего GitHub Release.
4. Откройте файл двойным щелчком и подтвердите установку в Stream Deck.
5. Перетащите `Adapter On/Off` или `Cycle Adapters` из категории WinNetSwitch на кнопку.
6. Для `Adapter On/Off` выберите физический адаптер в Property Inspector. После изменения оборудования используйте обновление списка.

Запускайте Stream Deck в обычном режиме, не от имени администратора. Повышенные права остаются только у WinNetSwitch, поскольку они нужны для изменения адаптеров. Успешная операция показывает зелёное подтверждение Stream Deck и Windows-уведомление из трея; ошибка показывает alert и Windows-уведомление об ошибке. Повторные запросы Property Inspector в течение двух секунд используют один запрос адаптеров, а после каждой мутации кэш сбрасывается.

`Adapter On/Off` меняет только выбранный адаптер. `Cycle Adapters` сортирует физические адаптеры по отображаемому имени без учёта регистра, выбирает элемент после первого активного адаптера, после последнего возвращается к первому и вызывает транзакционную операцию enable-only. Если активных адаптеров нет, выбирается первый.

Отключение адаптера может прервать загрузки и удалённые сессии. Не проверяйте мутацию на адаптере, через который идёт текущее удалённое подключение.

Если кнопка показывает `Start WinNetSwitch`, убедитесь, что актуальное companion-приложение запущено, и проверьте `%APPDATA%\Elgato\StreamDeck\Plugins\dev.witqq.win-net-switch.sdPlugin\logs`. Не оставляйте Stream Deck запущенным от имени администратора как обход проблемы; в bug report укажите версии companion и плагина и приложите обезличенные фрагменты логов.

## Локальная разработка

Необходимые инструменты:

- Node.js 24 через `nvm`, `nvm-windows` или другой version manager;
- npm;
- Stream Deck 7.1 или новее для интерактивной проверки устройства;
- .NET 10 SDK для разработки companion-приложения.

В каталоге `stream-deck-plugin` выполните:

```powershell
npm ci
npm run typecheck
npm test
npm run package
```

`npm run package` собирает TypeScript entry point через Rolldown и вызывает официальную Elgato CLI. Результат находится в `artifacts\stream-deck\dev.witqq.win-net-switch.streamDeckPlugin`.

Чтобы связать каталог разработки с локальной установкой Stream Deck, выполните следующую команду только на машине разработчика:

```powershell
streamdeck link .\dev.witqq.win-net-switch.sdPlugin
```

CI не связывает и не устанавливает плагин. `scripts\test-stream-deck-package.ps1` открывает пакет как ZIP, проверяет обязательные файлы и отклоняет companion executables и scripts.

## Публикация в Marketplace

Публикация выполняется вручную через [Elgato Maker Console](https://maker.elgato.com/). Перед отправкой:

- используйте Maker organization `witqq`, а значение `Author` в manifest оставьте идентичным;
- загружайте созданный `.streamDeckPlugin`, а не каталог исходников;
- укажите Windows-only и обязательную внешнюю зависимость WinNetSwitch;
- добавьте ссылки на companion installer, поддержку, исходники и privacy policy;
- подготовьте название, английское описание, release notes и вариант монетизации;
- подготовьте thumbnail PNG 1920 × 960 и минимум три gallery PNG 1920 × 960 либо поддерживаемые видео 1920 × 1080;
- перед публикацией скачайте из Maker Console и проверьте обработанную DRM-сборку;
- продемонстрируйте обе команды на реальном Stream Deck или Stream Deck Mobile без раскрытия приватных данных адаптеров.

Неизменяемый UUID плагина — `dev.witqq.win-net-switch`. UUID команд — `dev.witqq.win-net-switch.toggle-adapter` и `dev.witqq.win-net-switch.cycle-adapters`; после публикации их нельзя менять.

Перед отправкой сверяйтесь с актуальными официальными документами: [distribution](https://docs.elgato.com/streamdeck/sdk/introduction/distribution/), [plugin guidelines](https://docs.elgato.com/guidelines/stream-deck/plugins/), [submission guide](https://docs.elgato.com/maker-console/submitting-products/) и [review process](https://docs.elgato.com/maker-console/review-process/).
