# SG Client

![Windows](https://img.shields.io/badge/Windows-x64-0078D4)
![Release](https://img.shields.io/badge/release-v0.0.40-2ea44f)
![Portable](https://img.shields.io/badge/package-Portable-blue)

SG Client — единый Windows-клиент для профилей, создаваемых SG-Panel и SG-AWG-Panel.

## Поддерживаемые подключения

- VLESS + REALITY;
- VLESS + XHTTP + TLS;
- Hysteria2 + QUIC + TLS;
- AmneziaWG.

## Как работает SG Client

Для каждого типа подключения используется отдельный подходящий движок:

- Xray — для VLESS;
- sing-box — для Hysteria2 и TUN-сценариев;
- AmneziaWG и Wintun — для профилей SG-AWG-Panel.

## Быстрый запуск

1. Скачайте `SG-CLIENT-040-PORTABLE-x64.zip`.
2. Полностью распакуйте архив в отдельную папку.
3. Запустите `SG-Client-040.exe`.
4. Подтвердите запрос Windows на запуск с правами администратора, если он появится.

Не запускайте программу непосредственно из ZIP-архива.

## Возможности

- системный TUN;
- раздельная маршрутизация;
- GeoIP, GeoSite и SRS-правила;
- режимы маскировки DPI для поддерживаемых Xray-профилей;
- Kill Switch с аварийным сбросом;
- проверка фактического состояния подключения;
- обновление Xray и sing-box из интерфейса.

Mihomo не используется для пользовательской проверки обновлений. AmneziaWG обновляется вместе с SG Client. Самостоятельное обновление SG Client отключено.

## Релизный формат

Официальный формат — компактный Portable ZIP. Установочный EXE не публикуется.

Portable не содержит пользовательских профилей, журналов, рабочих конфигураций и временных файлов.

## Лицензии

Проект использует Xray-core, sing-box, AmneziaWG, Wintun и другие открытые компоненты. Условия и авторство указаны в `LICENSE` и `THIRD-PARTY-NOTICES.md`.

## Автор

Разработка SG Client: **Ser.Gor**
