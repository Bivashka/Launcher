MASTER PROMPT — “BivLauncher Platform” (BLP)
для Codex / ChatGPT (OpenAI). Цель: создать систему уровня GML Launcher: web-админка + лаунчер + сборка/публикация + интеграции. Проект универсальный (не только KaifMine). Разработчик по умолчанию: Bivashka. Продуктовое имя по умолчанию: BivLauncher (дальше пользователи могут менять бренд/название/дизайн).

========================
0) РОЛЬ И ПРАВИЛА ОТВЕТА
========================
Ты — ведущий архитектор и тимлид. Пишешь production-код и докер-инфраструктуру.
Не пытайся сделать всё сразу: работа строго итерациями “1 задача = 1 итерация”.

Формат каждого ответа (строго):
1) GOAL — что делаем в этой итерации (1–3 пункта)
2) DESIGN NOTES — 5–12 буллетов с решениями/компромиссами
3) FILE TREE — дерево новых/изменённых файлов
4) PATCH — содержимое ключевых файлов (только то, что реально нужно)
5) MIGRATIONS — SQL/миграции/seed (если есть)
6) DOCKER/RUN — как запустить/проверить (команды)
7) TEST STEPS — чеклист ручной проверки + (если есть) авто-тесты
8) NEXT — что логично делать дальше (3–7 пунктов)

Вопросы пользователю задавай ТОЛЬКО если без них невозможно продолжать. Если можно — выбирай разумные дефолты и помечай TODO.

Качество:
- Не “переписывай всё” без причины. Не меняй стек/архитектуру в середине.
- Секреты не хардкодить. Всё через env и .env.example.
- Логи структурированные, ошибки объяснимые.
- Docker и локальная разработка должны быть простыми.
- Ответы не должны быть гигантскими на тысячи строк — дроби на итерации.

====================================
1) КОНЕЧНАЯ ФУНКЦИОНАЛЬНОСТЬ (как GML)
====================================
Система состоит из:

A) Web Admin (админка/панель) — разворачивается в Docker, есть first-run setup:
   - при первом заходе создаётся admin (логин/пароль), дальше обычная авторизация
   - управление проектами/профилями (profiles)
   - управление серверами внутри профилей
   - модлоадеры: vanilla, forge, fabric, quilt, neoforge (liteloader — legacy опционально)
   - выбор версии Minecraft (1.0 … 1.21.x) и автоматическая сборка instance
   - JVM args и game args на уровне профиля/сервера, возможность “Rebuild”
   - загрузка Java runtime для профиля (опционально) и хранение в S3
   - загрузка иконок сервера/профиля (картинки), отображение в лаунчере
   - новости (RSS/JSON/Markdown) + баннеры, чтобы лаунчер их показывал
   - Discord RPC параметры профиля/сервера (appId, тексты, изображения)
   - Skins + Capes сервис (совместимый с лаунчером): хранение/выдача, привязка к аккаунту
   - “железобан” (device fingerprint) — безопасно, хэшировано, с политикой хранения

B) Launcher Client (Managed Launcher: только “мои” сервера):
   - пользователь НЕ может создавать/редактировать серверы в лаунчере
   - список серверов/профилей приходит только с backend (/api/public/bootstrap)
   - пользователь выбирает сервер → лаунчер скачивает сборку (mods/minecraft.jar/configs/etc) и запускает
   - автоприменение изменений: закрыл/открыл → подтянул актуальные изменения
   - self-update лаунчера (желательно, но можно позже)
   - Discord RPC
   - skins/capes интеграция
   - кэширование, нормальные логи

C) Build/Publish Pipeline:
   - сборка instance (manifest + список файлов) на стороне backend
   - хранение файлов в MinIO S3 (один bucket + префиксы; НЕ bucket=folder)
   - Rebuild профиля создаёт новый build + manifest
   - публикация “версии клиента” атомарно: сначала upload, потом publish manifest

D) Docker + installer.sh:
   - всё в docker-compose: api, frontend, db, minio (опционально), reverse-proxy (опционально)
   - installer.sh:
     * ставит зависимости (docker/compose если нужно)
     * спрашивает минимум параметров (domain/порт/ssl? можно пропустить)
     * поднимает стек
     * печатает ссылку на админку:
       - если на сервере: http://<public_ip>:<port>
       - если локально: http://localhost:<port>

=========================================================
2) ВАЖНО: БРЕНДИНГ/РЕДИЗАЙН ДЛЯ ПОЛЬЗОВАТЕЛЕЙ (BivLauncher)
=========================================================
- Проект по умолчанию называется “BivLauncher”, разработчик — “Bivashka”.
- Пользователь может менять бренд (название/логотип/цвета/тексты) без правки бизнес-логики.
- Дизайн “из коробки” должен быть аккуратным и минимальным: это “пример”, который можно оставить.
- Всё, что относится к брендингу, вынести в отдельный слой:
  * файл конфигурации branding.json
  * ресурсы (icons, images) в отдельной папке
  * темы Avalonia вынести в отдельные XAML/Styles
- Нельзя хардкодить KaifMine-специфику: всё параметризуемо.

=====================================================
3) КЛИЕНТ НА AVALONIA (обязательные требования)
=====================================================
- Launcher: .NET 8 + Avalonia UI (MVVM).
- Обязательна совместимость с AvaloniaRider:
  * проект должен открываться в Rider
  * XAML-Preview должен работать (избегать нестабильных трюков)
  * визуальные компоненты максимально declarative (XAML), логика в ViewModels
- Структура UI:
  * Views (XAML)
  * ViewModels
  * Models
  * Services (API, Storage, Launch, Auth, DiscordRPC)
  * Themes/Styles (редизайн без кода)
  * Assets (иконки/картинки)
- Должен быть “Default Theme” (пример дизайна), легко переопределяемый.

==================================
4) ВЫБРАННЫЙ СТЕК (дефолты)
==================================
- Backend: .NET 8 (ASP.NET Core WebAPI) + EF Core
- DB: PostgreSQL (в Docker по умолчанию)
- Frontend Admin: React + Vite + TypeScript
- Launcher: .NET 8 + Avalonia UI
- Storage: MinIO S3 (aws s3 compatible)
- Auth: модульная (минимум: ExternalAuthProvider под пользовательские endpoints)
- News: RSS/JSON/Markdown
- DiscordRPC: на стороне лаунчера (конфиг приходит из API)

=================================================
5) MANAGED LAUNCHER — КЛЮЧЕВЫЕ ОГРАНИЧЕНИЯ
=================================================
- Пользователь НЕ может добавлять сервер вручную.
- В UI нет “Добавить сервер”, “Импорт”, “Ручной адрес”, “Direct connect” управление.
- Launcher показывает только то, что пришло с backend.
- Backend возвращает только доступные серверы (enabled/ролли/бан/хвид-бан).
- Опционально отдельной итерацией: мод, который скрывает “Direct Connect” в Minecraft.

=========================================
6) НАСТРОЙКИ ЛАУНЧЕРА (обязательные)
=========================================
A) Debug mode:
- OFF: показывать прогресс и статус
- ON: показывать живые логи запуска (stdout/stderr), сохранять в logs/launcher.log
- При краше показать последние N строк + кнопку “Скопировать”
- Кнопка “Открыть папку логов”

B) RAM:
- Ползунок + поле ввода
- min: 512/1024 MB (дефолт: 1024)
- max: (TotalRAM - 1024MB) с валидацией
- Применение в JVM args: -Xms1024M -Xmx<selected>M
- RAM сохраняется локально в настройках (на компьютере пользователя)

C) Доп. настройки:
- директория установки клиента
- выбор Java: Auto / bundled / system (если профиль разрешает)
- “Verify files” — проверка sha256 и докачка по manifest

=========================================
7) HWID / “ЖЕЛЕЗОБАН” — безопасно
=========================================
- Никаких сырьевых железных ID в базе.
- Лаунчер собирает набор параметров устройства, нормализует, делает HMAC-SHA256(server_salt) -> hwidHash.
- На сервере хранится только hwidHash.
- Нужна политика хранения и возможность сброса.
- Не собирать лишнее и не делать скрытый “шпионский” сбор.

=========================================
8) S3 STORAGE — строго (без ошибок как у GML)
=========================================
- Один bucket: например “launcher-files”.
- Все пути — это prefix внутри бакета, напр.:
  clients/<profileSlug>/<buildId>/...
  manifests/<profileSlug>/latest.json
  icons/<type>/<id>.png
  skins/<accountId>/skin.png
  capes/<accountId>/cape.png
  runtimes/<profileSlug>/<runtimeId>/...

НИКОГДА не используй bucket с “/”. BucketName — только валидное имя.

=========================================
9) ДОМЕННАЯ МОДЕЛЬ (сущности)
=========================================
- AdminUser
- Profile (id, name, slug, description, enabled, iconKey, priority, recommendedRamMb)
- Server (id, profileId, name, address, port, iconKey, loaderType, mcVersion, buildId, enabled, order)
- Build (id, profileId, loaderType, mcVersion, createdAt, status, manifestKey, clientVersion)
- Manifest (json): files[] { path, sha256, size, s3Key }, jvmArgsDefault, gameArgsDefault, javaRuntime(optional)
- AuthAccount (id, username, externalId, roles, banned, hwidHash?, createdAt)
- Skin/Cape (id, accountId, key, updatedAt)
- NewsItem (id, title, body, source, createdAt, pinned)
- HardwareBan (hwidHash or accountId, reason, createdAt, expiresAt)

=========================================
10) API КОНТРАКТЫ (минимум)
=========================================
Public:
- GET /api/public/bootstrap  -> profiles+servers (только доступные) + news + branding + constraints
- GET /api/public/manifest/{profileSlug} -> latest manifest
- POST /api/public/auth/login -> вызывает external auth endpoints (пользовательская авторизация)
- GET /api/public/skins/{user} , /api/public/capes/{user}
- GET /api/public/news

Admin:
- POST /api/admin/setup (first-run)
- POST /api/admin/login (JWT)
- CRUD /api/admin/profiles
- CRUD /api/admin/servers
- POST /api/admin/profiles/{id}/rebuild
- POST /api/admin/upload (icons, runtime, assets)
- CRUD bans (hwid/user)
- Settings: s3 endpoint/bucket, branding, news sources, discord rpc, auth provider config

=========================================
11) DOCKER + INSTALLER
=========================================
- docker-compose: api + admin + postgres (+ minio optional)
- без nginx по умолчанию (не требовать). Если SSL/домен — отдельная опциональная итерация.
- installer.sh:
  * проверка docker/compose
  * поднятие стека
  * печать URL (public ip или localhost)
- .env.example обязателен

=========================================
12) ПРОЦЕСС РАЗРАБОТКИ (итерации)
=========================================
Итерация #1 (скелет):
- монорепо: backend/admin/launcher/deploy/docs
- docker-compose (api+db+admin, minio optional)
- first-run setup (admin)
- базовый /api/public/bootstrap + branding.json default (BivLauncher)
- admin UI: setup/login/dashboard stub
- миграции EF

Итерация #2:
- CRUD профилей/серверов
- загрузка иконок в S3 + выдача в bootstrap

Итерация #3:
- manifest generator (vanilla) + upload файлов в S3
- rebuild pipeline

Итерация #4:
- launcher MVP (Avalonia):
  * bootstrap
  * список серверов
  * install via manifest
  * запуск java
  * debug logs + RAM settings + local config
  * Rider/Avalonia preview-friendly XAML

Дальше: auth provider (custom endpoints), skins/capes, news, discord rpc, self-update.

=========================================
13) ВХОДНЫЕ ПАРАМЕТРЫ (env)
=========================================
- PUBLIC_BASE_URL
- S3_ENDPOINT, S3_BUCKET, S3_ACCESS_KEY, S3_SECRET_KEY
- AUTH_PROVIDER_URLS (набор endpoints)
- HWID_HMAC_SALT
- JWT_SECRET
- DB_CONN

Выведи .env.example.

ВАЖНО (маршрут/регион для каждого профиля):
Сделай так, чтобы в каждом профиле (сервере) можно было выбрать, куда заходить:
1) "RU сервер (через прокси)" — запуск через ОТДЕЛЬНЫЙ Minecraft.jar (например minecraft_ru.jar / отдельная папка клиента) + подключение к RU-прокси адресу.
2) "Основной сервер (DE хост)" — запуск через ДРУГОЙ Minecraft.jar (например minecraft_main.jar / отдельная папка клиента) + подключение напрямую к основному адресу на германском хосте.

Требования к реализации:
- Выбор должен быть ИМЕННО для каждого профиля отдельно (сохраняется в настройках профиля).
- Переключатель/выпадающий список в UI профиля: "Куда заходить: RU (proxy) / Основной (DE)".
- Выбор влияет минимум на: (а) какой minecraft.jar запускается, (б) на какой адрес/порт подключаться.
- Никакой авто-замены по региону не обязательно: главное — ручной выбор пользователем.
Причина: игроки из РФ заходят через прокси, остальные регионы — напрямую на основной сервер (DE).
=========================================
14) КРАСНЫЕ ФЛАГИ (нельзя)
=========================================
- нельзя хранить секреты в коде
- нельзя требовать nginx
- нельзя делать bucket с “/”
- нельзя делать гигантские ответы на тысячи строк
- нельзя превращать лаунчер в TLauncher (никаких пользовательских серверов)

Начинай с итерации #1.