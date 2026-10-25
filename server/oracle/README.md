# Установка на отдельную Oracle VM с Ubuntu 24.04

Подготовлены локальные файлы установки. Они не создают аккаунт Oracle, VM или платные ресурсы. Предполагается отдельная уже созданная VM с Ubuntu 24.04, архитектура `amd64` или `arm64`, доступ по SSH и публичный IPv4. Пакет нужно распаковать с сохранением путей `server/lobby_server.py` и `server/oracle/`.

## TLS без покупки домена

Let's Encrypt уже выпускает доверенные сертификаты непосредственно на публичные IP. Их срок — 160 часов. Поддержка IP есть в актуальном Certbot; здесь используется официальный Snap версии не ниже 5.4 и метод standalone HTTP-01. Ubuntu 24.04 и обе указанные архитектуры поддерживают Snap. [IP-сертификаты](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability), [поддержка Certbot](https://letsencrypt.org/2026/03/11/shorter-certs-certbot), [официальная установка Snap](https://certbot.eff.org/instructions?os=snap&ws=other).

Запрос сертификата создаёт ключ и ACME-аккаунт **только на выбранной VM при реальном запуске установки**. В подготовленном пакете нет ключей или аккаунтов. Содержимое `/etc/letsencrypt` и `privkey.pem` не нужно копировать в чат или на компьютер друга.

Порт 80 должен быть свободен на VM и доступен из интернета: Certbot временно занимает его для проверки. Игра использует TLS на 7780. Отдельный таймер проверяет продление каждые 6 часов с разбросом до 30 минут. Certbot сам решает, когда пора продлевать; принудительное частое перевыпускание не используется. Штатный таймер Snap не отключается, чтобы сохранить обслуживание других сертификатов, если они появятся. [Правила продления и deploy hooks](https://eff-certbot.readthedocs.io/en/stable/using.html#renewing-certificates).

После успешного выпуска hook проверяет соответствие сертификата ключу и IP, устанавливает оба файла одной атомарной заменой ссылки и отправляет сервису SIGHUP. Новые соединения используют новый сертификат; текущие матчи продолжаются. При ошибке команды reload указатель возвращается к прежней паре. Повторная установка неизменённого сертификата не перезагружает TLS.

## Сеть перед запуском

У VM должны быть публичный IPv4, публичная подсеть, маршрут к Internet Gateway и доступ в интернет для Ubuntu/Snap/ACME. В Security List подсети или назначенной VM Network Security Group разрешите **stateful ingress**, source `0.0.0.0/0`, protocol TCP, source ports All:

| Destination port | Назначение |
| --- | --- |
| 80 | Проверка владения IP и последующее автоматическое продление |
| 7780 | Защищённые подключения игры |

SSH TCP22 оставьте доступным своему административному IP. Если исходящий трафик ограничен, отдельно сохраните DNS и исходящие HTTP/HTTPS для обновлений и ACME. Облачные правила и firewall внутри Ubuntu действуют отдельно. [Правила Oracle](https://docs.oracle.com/en-us/iaas/Content/Network/Concepts/securityrules.htm), [создание ingress-правил](https://docs.oracle.com/en-us/iaas/Content/Network/Concepts/creating-securitylist.htm).

Флаг `--configure-firewall` добавляет и сохраняет через отдельный systemd unit только входящие TCP80/7780. Он не сбрасывает цепочки, не изменяет SSH или OUTPUT и не включает UFW. Oracle отдельно предупреждает не удалять защитные правила iSCSI и не применять UFW к Ubuntu platform images. [Oracle Compute Best Practices](https://docs.oracle.com/en-us/iaas/Content/Compute/References/bestpracticescompute.htm).

## Команды на VM

Сначала замените `PUBLIC_IPV4` и `CONTACT_EMAIL` в примере реальными значениями. Email — контакт для Let's Encrypt; пароли и приватные SSH-ключи скрипту не нужны.

```bash
cd "$HOME/zonez-server"
bash server/oracle/install.sh --public-ip PUBLIC_IPV4 --email CONTACT_EMAIL --plan
```

До установки ознакомьтесь с [действующим соглашением Let's Encrypt](https://letsencrypt.org/repository/). Если согласны, отдельный `--agree-tos` подтверждает это при фактическом выпуске сертификата:

```bash
sudo bash server/oracle/install.sh --public-ip PUBLIC_IPV4 --email CONTACT_EMAIL --agree-tos --configure-firewall
```

Скрипт устанавливает пакеты Ubuntu и официальный Certbot Snap, создаёт пользователя `zonez` без входа в систему, копирует сервер в `/opt/zonez/server`, включает сервис и продление. Игра работает без root и привилегий, с максимумом 64 соединений и пределом памяти 512 MiB. Приватный ключ доступен только root и группе сервиса: `root:zonez`, `0640`. Python не получает доступа к ACME-аккаунту. Если порт 80 занят или уже установлен Certbot через apt, установка останавливается с объяснением; чужой веб-сервис не выключается.

Повторный запуск installer обновляет исходник и **перезапускает игру**. Планируйте обновление приложения между матчами. Это отличается от автоматического продления сертификата, которое использует reload.

## Проверка после установки

```bash
sudo bash server/oracle/verify.sh
sudo /snap/bin/certbot renew --cert-name zonez-ip --dry-run --run-deploy-hooks
sudo systemctl list-timers zonez-certbot-renew.timer
sudo journalctl -u zonez-lobby -n 50 --no-pager
sudo journalctl -u zonez-certbot-renew -n 50 --no-pager
```

`verify.sh` проверяет пользователя процесса, права ключа, работающий таймер, доверие сертификату для публичного IP и обмен `welcome`/`ping`/`pong` через локальное TLS-соединение. `--dry-run --run-deploy-hooks` проверяет путь продления; Certbot передаёт deploy hook текущий действующий сертификат, не тестовый сертификат staging. [Описание dry run](https://eff-certbot.readthedocs.io/en/stable/using.html).

Затем обязательна проверка с отдельного интернет-подключения. В игре: **Играть → Онлайн → Лобби и поиск**, адрес `tls://PUBLIC_IPV4:7780`. Создайте лобби и войдите по коду с другого компьютера; два независимых лобби должны встретиться в поиске. Локальная проверка не подтверждает доступность портов через Oracle Security List/NSG.

Публичный IPv4 должен сохраняться: сертификат и настройки игроков привязаны к нему. После замены IP потребуется сознательно перевыпустить сертификат и обновить адрес; installer не подменяет уже установленный IP автоматически. Регулярно проверяйте результат таймера: без успешного продления сертификат истечёт примерно через шесть дней.

## Проверки исходников без VM

```bash
python3 -m unittest discover -s server/oracle/tests -p 'test_*.py' -v
bash -n server/oracle/install.sh server/oracle/renew-certificate.sh server/oracle/firewall.sh server/oracle/verify.sh
```

Тесты hook используют только фиктивные данные и подменённые системные вызовы; они не выпускают сертификаты, не создают приватные ключи и не вызывают облачные API. Реальные Certbot, systemd, firewall и внешний маршрут проверяются только на выбранной VM.
