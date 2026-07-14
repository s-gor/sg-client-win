# Профили и движки

SG Client определяет тип импортированного профиля и выбирает движок автоматически.

| Профиль | Движок |
|---|---|
| VLESS RAW/TCP + REALITY | Xray |
| VLESS XHTTP + REALITY | Xray |
| VLESS XHTTP + TLS | Xray |
| Hysteria 2 | sing-box |
| AmneziaWG `.conf` | AmneziaWG |

Проект ориентирован на профили, созданные SG-Panel и SG-AWG-Panel. Произвольные сторонние конфигурации могут содержать параметры, которые SG Client не обслуживает.
