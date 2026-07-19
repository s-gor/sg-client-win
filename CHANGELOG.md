## SG Client 095 — Simplified Settings and Mandatory Diagnostics

- Kept the complete SG Client 089 functional and visual base.
- Removed General, TUN and Advanced section headings and subtitles from the unified Settings page.
- Kept all working setting cards in one continuous classic full-width list.
- Made UDP, traffic sniffing and the local Xray connections log mandatory and always enabled.
- Removed the three mandatory capability switches from the UI while keeping log-level selection.
- Preserved the separate compact About page and updated it to build 095.
- Preserved protocols, routing, traffic accounting, tray controls, subscriptions, Local Proxy and runtime binaries.

## SG Client 088 — Unified Settings and Header Geometry

- Merged General, TUN and Advanced settings into one compact Settings page.
- Removed three decorative switches that did not affect the main UI.
- Reworked About so it fits without inner scrolling or fixed-width clipping.
- Fixed the top navigation separator clipping the Import item.
- Preserved SG Client 087 routing, traffic, protocols, tray, subscriptions and Connections work.

## SG Client 087 — Connections Layout and Contrast

- Kept the complete SG Client 086 Build Fix 2 functional base, including Latte RC6, tray controls and flexible subscriptions.
- Rebuilt the live-connections toolbar as two stable rows so search, output filter, history and export actions no longer overlap.
- Added an inset table container with safe left, right and bottom padding and a scrollbar that stays inside the card.
- Rebalanced column widths for Xray and sing-box without a horizontal scrollbar at the normal window size.
- Added route badges for VPN, Direct, Block and Other.
- Added deeper, higher-contrast Connections surfaces for Graphite, Latte and Northern without changing the rest of each theme.
- Preserved routing logic, traffic accounting, diagnostics, protocols and runtime binaries.

## SG Client 085 — Inline Latency Action

- Kept the complete SG Client 084 functional base.
- Removed the separate latency status/progress container from the profile footer.
- Kept only three permanent actions: Connect/Switch, Check latency and Delete.
- Reused the middle action for live progress and Stop while a test is running.
- Preserved every protocol, traffic counter, routing fix, Local Proxy function and runtime binary.

## SG Client 082 — Bottom-Anchored Connection Scene

- Kept SG Client 080 as the complete functional base; SG Client 077 was used only as a visual reference.
- Restored a flexible main scene and anchored the complete 2×2 mode grid immediately above the traffic card.
- Kept the connection emblem and profile/status area above the controls with natural breathing room.
- Preserved every 080 protocol, traffic counter, routing fix, Local Proxy function, backup feature and runtime binary.

## SG Client 079 — Restored Connection Scene and Local Proxy UX

- Restored the circular connection emblem, selected profile and connection detail above the compact mode grid.
- Kept the TUN, System Proxy, Local Proxy and Disconnect All 2×2 grid.
- Fixed the full `SG-Client` brand in the header and removed the stale version badge.
- Polished the three profile-list actions and hides the latency progress block when no test is running.
- Moved the mixed HTTP/SOCKS port from general Advanced settings into Expert → Local Proxy.
- Added the ready-to-copy local proxy address and safe port editing while all modes are off.
- Preserved the SG Client 077/078 routing reload, diagnostics and safe disconnect logic.

## SG Client 078 — Compact Main Mode Grid

- Removed the large central profile scene from the main screen.
- Added a 2×2 grid for TUN, System Proxy, Local Proxy and Disconnect All.
- Added a safe global stop command for proxy modes, cores, TUN/AWG, tests and Kill Switch.
- Replaced two separate latency buttons with one latency menu.
- Preserved the SG Client 077 active-mode routing reload and final-route verification.

## SG Client 076 — Rebuilt and Verified Base

- Rebuilt from the confirmed SG Client 074 launcher/build foundation.
- Preserves SG Client 075 latency-test summary, problem filtering, Local Proxy routing fixes and 15-minute connection history.
- Replaces the silent VBS elevation path with a logged PowerShell elevation wrapper.
- Adds launcher logging before UAC and a persistent result window for startup/build failures.
- Keeps complete recursive backups, modern VLESS/XHTTP and AmneziaWG support, WinTUN traffic counters and live routes.

## SG Client 075 Build Fix 1 — Local Proxy Routing and Test Summary

- Applied SG Smart Routing consistently to TUN, System Proxy and Local Proxy.
- Fixed custom schemes with Direct as the final action: only explicitly listed domains/IPs use VPN.
- Prevented legacy routing profiles from overriding SG routing in proxy modes.
- Added Xray and sing-box regression tests for Local Proxy custom routing.
- Added a full latency-test result summary and a problem-only profile filter.
- Restored a functional 15-minute connection history for sing-box.
- Clarified connection actions and added a dedicated Close Window button.

## SG Client 075 — Reliable Subscription Tests and Stable Live Routes

- Reworked large-list latency testing with deterministic progress and cancellation.
- Every tested profile receives a final state; stale `Testing` values are cleared.
- Added explicit `Unavailable`, `Error`, `Cancelled`, and `Not tested` outcomes.
- Improved the progress panel and widened the Stop button.
- Marked localhost/127.0.0.1 Direct routes as expected local traffic.
- Stabilized and widened the live-routes output filter.
- Fixed sing-box DNS route unit-test initialization.

## SG Client 074 — Complete Backup and Restored Live Routes

- Restored the factual live VPN / Direct / Block list after the Fix 12 empty-window regression.
- Removed the broken content-only TabControl template and explicitly selects the live routes tab when the window opens.
- Kept the misleading static Route Check, VPN Check and quick-check UI hidden.
- Backup now recursively includes all persistent `guiConfigs` state, including `sg-awg/profiles.json` and local AWG `.conf` files.
- Volatile `sg-awg/runtime` data is excluded from backup and restore.
- Restore is prepared in an isolated directory and replaces the current configuration only after validation and safe DNS migration.
- Backup verification and restore confirmation report whether settings, traffic history and local AWG profiles are present.
- The 074 builder prefers the most recently used 073 configuration so the current local state is carried forward.
- Preserved simple link/config import, separate subscription management, WinTUN traffic accounting, modern VLESS/XHTTP, monthly profile traffic and the full log window.

## SG Client 072 — Traffic by Profile

- Moved Local Proxy back out of the main quick-action row; the main screen again shows TUN and System Proxy as primary actions.
- Traffic statistics are now stored separately by stable profile ID.
- The traffic card shows the node/profile name and separate values for session, today, current month and all time.
- Switching profiles changes the displayed statistics without mixing totals between nodes.
- Added a dedicated all-nodes traffic window with session, today, month, total and last-used columns.
- Renaming a profile preserves its history because counters are keyed by ID.
- Resetting statistics affects only the current profile.
- Xray/sing-box delta counters and AmneziaWG cumulative counters use the same per-profile store.
- Preserved Daily UX, backup restore, Connections, RoscomVPN GeoFiles and stable `SG-Client.exe`.

## SG Client 071 — Daily UX

- Separated the highlighted profile from the actually running profile: a single click only selects a row; explicit switch, double-click or Enter activates it.
- The green active marker remains attached to the running profile while another row is inspected.
- Added a visible Local Proxy action next to TUN and System Proxy on the main screen.
- Added latency-test progress, completed/total counters and an explicit stop action for large subscriptions.
- Persisted profile-browser sort, subscription filter, protocol filter, country filter and country-exclusion mode.
- Added “exclude selected country” filtering.
- Changed the executable assembly name to stable `SG-Client.exe`; the version remains in application metadata, package name and build log.
- The build preserves profiles, subscriptions, settings and local backups from older builds when found.
- Preserved Connections, AmneziaWG diagnostics, RoscomVPN GeoFiles, country flags and external backup restore from earlier releases.

## SG Client 071 — RoscomVPN TUN compatibility hotfix

- Compatibility analysis now includes the active base Xray routing profile, not only SG Smart Routing.
- Missing categories such as `geosite:google` are detected before RoscomVPN files are installed.
- Applying the compatible preset preserves existing routing profiles and activates a dedicated empty base profile so RoscomVPN smart-routing rules are not shadowed.
- Rollback restores the previously active base routing profile together with GeoFiles and SG Smart Routing.

## SG Client 071 — RoscomVPN GeoFiles Support

- Added a dedicated RoscomVPN source using the official `geoip.dat` and `geosite.dat` release files.
- GeoFiles validation now reads the real category names from both protobuf files instead of requiring `geosite:category-ads-all` and `geoip:ru` for every source.
- Added family detection for standard, RoscomVPN and custom category sets.
- Added visible category summaries, full expandable category lists, counts, size/date, SHA-256, source and validation status.
- The pair is downloaded, checked and replaced as one guarded transaction; failures restore both files and the previous routing configuration.
- SG Client checks active SG Smart Routing categories before installation and blocks an incompatible replacement.
- Added an optional compatible RoscomVPN TUN routing preset: Private, Whitelist, Russian resources and selected services go Direct; the rest uses VPN.
- Optional RoscomVPN switches can block `category-ads`, `win-spy` and `torrent`.
- Local files are auto-detected as RoscomVPN when their category families match.
- Preserved all SG Client 069 Connections, proxy-parser, profile-selection, AmneziaWG and theme functionality.

## SG Client 068 — Connections Domain Correlation

### Имена сайтов в живых соединениях

- добавлено отображение домена над IP/портом в таблице Connections;
- источник имени показывается явно: `Xray access log`, `Xray sniffing` или `DNS-сопоставление Xray`;
- при отсутствии подтверждённого имени выводится «Домен не определён»;
- разные домены на одном CDN-IP группируются отдельно;
- в компактную карточку при наведении добавлены домен, источник имени и фактическое назначение;
- включены DNS-журнал Xray, информационный уровень файлового журнала и QUIC sniffing;
- поиск и CSV/JSON учитывают домен, источник имени и IP/порт.

## SG Client 068 — Connections Redesign

- Split the section into factual **Connections** and deterministic **Route Test** tabs.
- Xray live rows now contain only access-log facts: destination, country, actual route, technical outbound, network, request count and last seen time.
- Removed local Xray rule guesses and the permanent route-explanation panel.
- Added exact address-route evaluation for `full:`, `domain:`, `keyword:`, `regexp:`, IP/CIDR, `geoip:` and `geosite:` using active Routing rules before SG Smart Routing and installed GeoFiles.
- Rules that also require port, network, protocol, inbound or process return an explicit incomplete result instead of a guess.
- Added an explicit incomplete/ambiguous result when DNS, regexp or GeoFiles do not permit a deterministic answer.
- Added an AmneziaWG tunnel diagnostics card instead of an empty connections table.
- Preserved unified AmneziaWG import, Graphite default and all working SG Client 066/068 functionality.

## SG Client 068 — Connections Search Fix

- пустое неочевидное поле заменено на явный поиск с лупой и постоянной подсказкой;
- добавлена кнопка очистки, `Ctrl+F` и безопасная обработка `Esc`;
- поиск проверяет домен/IP, фактический выход, технический outbound и сеть;
- фильтр по умолчанию — «Все выходы»; при пустом результате показана понятная инструкция сброса.

## SG Client 067 — Connections Preview 1J

- Xray access log is grouped into unique destinations instead of raw repeated rows.
- Current-session test mode is the default; optional 15-minute history is available.
- SG Client locally matches verifiable smart-routing rules and compares them with the actual Xray outbound.
- Unverifiable geosite/geoip rules are reported honestly instead of being guessed.
- Added request counts and last-seen time for grouped Xray destinations.
- Preserved unified AmneziaWG import and Graphite default.

# История изменений

## SG Client 067 — Connections Preview 1I

### AmneziaWG из SG-AWG-Panel

- QR-код, вставка текста, файл `.conf` и подписка используют единый разборщик;
- наличие `Jc/Jmin/Jmax/S1–S4/H1–H4` определяет профиль как AmneziaWG;
- имя берётся из `# Name`, затем `# Client`, затем имени файла;
- предложенное имя можно изменить перед добавлением;
- полный конфиг и UDP Endpoint сохраняются без потерь;
- обычный WireGuard без параметров Amnezia остаётся WireGuard.

### Connections

- постоянная нижняя панель Xray убрана как неинформативная;
- вместо повторяющегося `payload не передан` показывается технический outbound Xray;
- селектор маршрута расширен и получил цельный скруглённый правый край;
- тема по умолчанию остаётся «Графит».

## SG Client 067 — Connections Preview 1G

### Фильтр, таблица и тема по умолчанию

- фильтр маршрутов переведён на собственный тематический Popup без системного ComboBox/ContextMenu;
- пункты «Все маршруты / Direct / VPN / Block / Другие» отображаются полностью;
- RX/TX удалены из таблицы и CSV, чтобы не занимать место недоступными для Xray значениями;
- процесс показывается только для sing-box;
- тема нового build по умолчанию — «Графит», «Латте» и «Северная» остаются доступны;
- профили, подписки, GeoIP и маршрутизация сохранены.

## SG Client 067 — Connections Preview 1C

### Стабильность окна «Соединения»

- убран старый ViewModel с собственным бесконечным циклом фонового опроса;
- добавлены отмена операций при закрытии окна и таймауты 3 секунды;
- исключены параллельные обновления и массовые уведомления UI для больших списков;
- AmneziaWG теперь показывает компактное пояснение без пустой таблицы и неактуальных кнопок;
- ComboBox и popup полностью стилизованы под тему;
- окно уменьшено, основной интерфейс за ним затемняется заметнее.

## SG Client 067 — Connections Preview 1

### Соединения и маршруты

- добавлен отдельный раздел «Соединения» в верхней панели;
- sing-box отображает активные назначения, точное правило и payload, outbound, цепочку, процесс и время жизни через Clash API;
- для sing-box добавлено закрытие выбранного соединения и всех соединений;
- Xray отображает недавние назначения и фактически выбранный outbound из access log;
- при отсутствии точного payload Xray интерфейс показывает ограничение честно и не подставляет предположение;
- добавлены поиск, фильтры Direct/VPN/Block/Другие, объяснение маршрута и экспорт CSV/JSON;
- справка дополнена разделом «Соединения и маршруты».

### Совместимость

- сохранена вся функциональность SG Client 066: тема «Латте», российская маршрутизация, GeoIP, PNG-флаги, большие подписки, TUN и три режима подключения.

## SG Client 066

### Светлая тема Latte Graphite

- светлая тема переработана в тёплой кофейно-молочной гамме;
- основной интерактивный акцент — стальной синий `#31566F`;
- главные действия отделены от кофейных вторичных кнопок;
- поля, popup, карточки и системные поверхности не возвращаются к чисто белому;
- публичная маркировка сборки возвращена к релизному номеру `066`.

### Маршрутизация

- добавлен пресет `geosite:tld-ru` для российских доменных зон;
- добавлен пресет `geosite:category-ru + geoip:ru` для российских сайтов и IP;
- доступны действия Direct, VPN, Block и «Выключено»;
- категории GeoFiles проверяются до применения конфигурации;
- в справку добавлено различие между `full:` и `domain:`.

### Профили и GeoIP

- сохранены встроенные PNG-флаги и локальный GeoIP fallback;
- сохранены поиск, фильтры и виртуализация больших подписок;
- сохранена поддержка VLESS, XHTTP, Hysteria2, AmneziaWG, Trojan и VMess.

## SG Client 040

### Интерфейс

- введён единый стандарт дополнительных окон;
- унифицированы размеры, скругления, шапки и нижние панели;
- основное действие во всех окнах называется «Применить»;
- окна остаются открытыми после успешного применения;
- удалены оставшиеся системные белые полосы и старые элементы управления;
- уведомления приведены к тёмному стилю SG.

### Подключение и диагностика

- сохранено подтверждение фактического состояния ядра и TUN;
- сохранено сравнение DPI до и после применения;
- сохранена работа Xray, sing-box и AmneziaWG через единый интерфейс;
- пользовательская диагностика маршрутизации читает рабочий `config.json`.

### Обновления

- проверка обновлений ограничена Xray и sing-box;
- Mihomo исключён из автоматической и ручной пользовательской проверки;
- пустое уведомление не создаётся, если новых версий нет;
- AmneziaWG обновляется только вместе с проверенным runtime-комплектом SG Client.

### Публикация

- подготовлен чистый portable-пакет без пользовательских профилей и журналов;
- добавлены SHA-256, release checklist и Windows CI;
- основной формат распространения — Portable ZIP.
