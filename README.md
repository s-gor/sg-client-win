# SG Client 099F

<p align="center">
  <strong>Windows-клиент для современных VPN и proxy-подключений SG</strong>
</p>

<p align="center">
  <img alt="Release" src="https://img.shields.io/badge/Release-099F-456F5C?style=for-the-badge">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%20%7C%2011-2F6B57?style=for-the-badge">
  <img alt="Portable" src="https://img.shields.io/badge/Portable-Yes-B88A45?style=for-the-badge">
</p>

---

## SG Client

**SG Client** — единый Windows-клиент для современных VPN и proxy-подключений с несколькими сетевыми движками, единым интерфейсом, подписками, маршрутизацией и диагностикой.

Наиболее полная интеграция доступна с **SG-Panel**, **SG-AWG-Panel** и **SG Gateway**, но клиент поддерживает и совместимые сторонние профили.

## Что нового в 099F

- полноценная поддержка **AWG2 / AWG3** в SG-native подписках;
- реальное измерение задержки **AWG2 / AWG3** через кратковременно поднятый AWG-туннель;
- расширенная проверка задержки для ранее проблемных профилей: **Mieru UDP**, **TUIC v5**, **AnyTLS**, AWG2/AWG3;
- ускоренная общая проверка серверов: обычные Xray/Mihomo-профили больше не ждут AWG;
- быстрый первый AWG-probe и автоматический расширенный retry только при временной неготовности Windows;
- **RU White List для AmneziaWG** вместе с Xray, sing-box и Mihomo;
- SG-native подписки умеют получать и обновлять AWG2/AWG3 без создания дублей;
- обновлены карточки профилей: компактное имя, отдельный badge технологии и независимое отображение latency;
- **Kill Switch включён по умолчанию** для новых настроек; старые конфигурации переводятся на ON один раз, после чего выбор пользователя сохраняется;
- исправлены граничные случаи IPv4/IPv6 CIDR;
- добавлена автоматическая адаптация интерфейса под **низкое разрешение**; на обычных мониторах масштаб остаётся 100%;
- масштаб пересчитывается при переносе окна между мониторами с разным разрешением/DPI;
- обновлено ядро **Xray**;
- подготовлена чистая Portable-сборка без пользовательских профилей, логов, временных конфигураций, latency-cache и backup-дублей GeoFiles.

Подробности: [RELEASE-NOTES-099F.md](RELEASE-NOTES-099F.md).

---

## Поддерживаемые движки и профили

| Движок | Основные профили |
|---|---|
| **Xray** | VLESS REALITY / TLS, RAW/TCP, XHTTP |
| **sing-box** | Hysteria 2 и совместимые профили |
| **Mihomo** | Mieru TCP/UDP, AnyTLS, TUIC v5 |
| **AmneziaWG** | AWG2 / AWG3 |
| **Wintun** | TUN-режим Windows |

---

## Возможности

- TUN Mode, System Proxy и Local Proxy;
- импорт ссылок, профилей и подписок;
- SG-native подписки;
- SG Smart Routing;
- RU White List;
- GeoFiles и SRS-наборы маршрутизации;
- Kill Switch;
- проверка задержки и фильтрация проблемных профилей;
- live Connections и диагностика маршрутов;
- статистика трафика по профилям;
- резервное копирование и восстановление;
- управление из системного трея;
- адаптация интерфейса под небольшие экраны.

---

## Portable

SG Client не требует установки. Для обычного использования достаточно распаковать Portable ZIP в отдельную папку и запустить `SG-Client.exe`.

Публичная Portable-сборка не должна содержать пользовательские профили, журналы, рабочие конфигурации, резервные копии и локальную историю тестов.

---

<p align="center">
  <strong>SG Client 099F</strong><br>
  Windows · Portable · Xray · sing-box · Mihomo · AmneziaWG
</p>
