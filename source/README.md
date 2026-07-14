# SG Client

Windows-клиент для профилей, создаваемых **SG-Panel** и **SG-AWG-Panel**.

![Version](https://img.shields.io/badge/version-074-35d39a)
![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-0078d4)
![.NET](https://img.shields.io/badge/.NET-10-512bd4)
![Package](https://img.shields.io/badge/package-Portable%20ZIP-243447)
![License](https://img.shields.io/badge/license-GPL--3.0-8b5cf6)

> Текущая версия: **v0.0.74**. Официальный формат — чистый Portable ZIP для Windows x64.

## Скачать

- [SG Client 074 Portable x64](https://github.com/s-gor/sg-client-win/releases/download/v0.0.74/SG-CLIENT-074-PORTABLE-x64.zip)
- [Страница релиза v0.0.74](https://github.com/s-gor/sg-client-win/releases/tag/v0.0.74)
- [SHA-256](https://github.com/s-gor/sg-client-win/releases/download/v0.0.74/SG-CLIENT-074-PORTABLE-x64.zip.sha256)

Полностью распакуйте ZIP в отдельную папку и запускайте `SG-Client.exe`. Не запускайте программу непосредственно из архива.

## Новое в SG Client 074

- восстановлено живое окно маршрутов и соединений с фактическими направлениями `VPN`, `Direct` и `Block`;
- резервная копия теперь рекурсивно сохраняет всё постоянное состояние клиента, включая локальные профили AmneziaWG;
- восстановление сначала проверяется во временной папке и только затем заменяет текущие данные;
- сохраняются профили, подписки, настройки, статистика и `guiConfigs\sg-awg\profiles\*.conf`;
- временный каталог `guiConfigs\sg-awg\runtime` в резервную копию не включается;
- сохранены современный импорт VLESS/XHTTP, поддержка AmneziaWG, месячная статистика по профилям, большое окно журнала и учёт TUN-трафика TCP/UDP/QUIC.

Подробности: [RELEASE-NOTES-074.md](RELEASE-NOTES-074.md).

## Обновление с предыдущей версии

1. Полностью закройте старый SG Client.
2. Распакуйте 074 в новую отдельную папку.
3. Для надёжного переноса старого состояния скопируйте из старой папки целиком:
   - `guiConfigs`;
   - при необходимости `guiBackups`.
4. Запустите `SG-Client.exe` из новой папки.
5. После успешного переноса создайте новую резервную копию уже средствами 074.

> Старые резервные ZIP, созданные некоторыми сборками 071–073, могли не содержать подпапку `guiConfigs\sg-awg`. Если использовались локальные профили AmneziaWG, переносите целиком старую папку `guiConfigs`.

## Поддерживаемые профили

| Источник | Профиль | Движок |
|---|---|---|
| SG-Panel | VLESS RAW/TCP + REALITY | Xray |
| SG-Panel | VLESS XHTTP + REALITY/TLS | Xray |
| SG-Panel | современный VLESS Encryption / ML-KEM | Xray |
| SG-Panel | Hysteria2 + QUIC + TLS | sing-box |
| SG-AWG-Panel | AmneziaWG `.conf` | AmneziaWG |

Поддерживаются прямые ссылки, файлы конфигурации, QR-коды и отдельное управление подписками.

## Основные возможности

- режимы `TUN`, `System Proxy` и локальный прокси;
- умная маршрутизация с действиями `VPN`, `Direct` и `Block`;
- российские пресеты и пользовательские правила;
- управление DNS, Kill Switch, MTU, локальной сетью и DPI-маскировкой;
- живые маршруты и соединения;
- статистика за сеанс, день, месяц и всё время по профилям;
- поиск, фильтры, страны и проверка задержки для больших подписок;
- резервное копирование и восстановление полного постоянного состояния;
- обновление Xray и sing-box;
- встроенные GeoFiles и поддержка пользовательских источников.

## Быстрый запуск

1. Скачайте `SG-CLIENT-074-PORTABLE-x64.zip`.
2. Полностью распакуйте архив.
3. Запустите `SG-Client.exe` и подтвердите запрос прав администратора.
4. Импортируйте прямую ссылку через меню **Импорт** либо добавьте URL в разделе **Подписки**.
5. Выберите профиль и нажмите `TUN On`.

## Состав Portable

```text
SG-Client.exe
bin/
  xray/
  sing_box/
  awg/
  srss/
guiConfigs/
guiBackups/
docs/
RESET-KILL-SWITCH.cmd
```

Публичный ZIP не содержит пользовательских профилей, подписок, ключей, настроек, статистики или журналов.

## Сборка из исходников

Требуются Windows 10/11 x64 и .NET SDK 10.x.

```cmd
dotnet restore v2rayN\v2rayN.sln
dotnet test v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj -c Release
dotnet publish v2rayN\v2rayN\v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true -p:EnableWindowsTargeting=true -o artifacts\sg-client
```

Подробности: [docs/06-BUILD.md](docs/06-BUILD.md).

## Документация

- [Быстрый старт](docs/01-QUICK-START.md)
- [Профили и движки](docs/02-PROFILES-AND-ENGINES.md)
- [TUN и маршрутизация](docs/03-TUN-AND-ROUTING.md)
- [Маскировка DPI](docs/04-DPI.md)
- [Диагностика](docs/05-TROUBLESHOOTING.md)
- [Сборка](docs/06-BUILD.md)
- [Проверка релиза](docs/07-RELEASE-CHECKLIST.md)
- [История изменений](CHANGELOG.md)

## Техническая основа и лицензии

Проект использует открытые компоненты v2rayN, Xray-core, sing-box, AmneziaWG, Wintun и открытые базы маршрутизации. Авторство и лицензии перечислены в [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Не публикуйте в Issues приватные ключи, UUID действующих профилей, токены, полные конфигурации серверов и неочищенные журналы. См. [SECURITY.md](SECURITY.md).

Проект и интерфейс SG Client: **Ser.Gor**.
