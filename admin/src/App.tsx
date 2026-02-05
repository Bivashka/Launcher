import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

type Phase = 'loading' | 'setup' | 'login' | 'dashboard'
type DashboardPage = 'overview' | 'wizard' | 'servers' | 'build' | 'news' | 'integrations' | 'security' | 'crashes' | 'docs' | 'settings' | 'audit'

type SetupStatusResponse = { needsSetup: boolean }
type LoginResponse = { token: string; tokenType: string; username: string }
type ApiError = { error?: string; title?: string; retryAfterSeconds?: number }

class ApiRequestError extends Error {
  status: number
  retryAfterSeconds?: number

  constructor(message: string, status: number, retryAfterSeconds?: number) {
    super(message)
    this.name = 'ApiRequestError'
    this.status = status
    this.retryAfterSeconds = retryAfterSeconds
  }
}
type UploadResponse = {
  key: string
  publicUrl: string
  size: number
  contentType: string
  linkedProfileId?: string | null
  linkedProfileSlug?: string | null
  runtimeSha256?: string
  runtimeSizeBytes?: number
  runtimeContentType?: string
}
type RuntimeVerifyResponse = {
  key: string
  resolvedFromProfile: boolean
  linkedProfileId?: string | null
  linkedProfileSlug?: string | null
  profileRuntimeKey: string
  profileRuntimeSha256: string
  profileRuntimeSizeBytes: number
  profileRuntimeContentType: string
  storageSha256: string
  storageSizeBytes: number
  storageContentType: string
  sha256MatchesProfile: boolean
  sizeMatchesProfile: boolean
  contentTypeMatchesProfile: boolean
}
type RuntimeCleanupResponse = {
  profileId: string
  profileSlug: string
  dryRun: boolean
  keepLast: number
  totalFound: number
  keepKeys: string[]
  deleteKeys: string[]
  deletedCount: number
}
type BuildResponse = { id: string; status: string; manifestKey: string; filesCount: number; totalSizeBytes: number }
type CosmeticUploadResponse = { account: string; key: string; url: string }
type DiscordRpcConfig = {
  id: string
  scopeType: 'profile' | 'server'
  scopeId: string
  enabled: boolean
  appId: string
  detailsText: string
  stateText: string
  largeImageKey: string
  largeImageText: string
  smallImageKey: string
  smallImageText: string
  updatedAtUtc: string
}

type DiscordRpcSettings = {
  enabled: boolean
  privacyMode: boolean
  updatedAtUtc?: string | null
}

type AuthProviderSettings = {
  authMode: 'external' | 'any'
  loginUrl: string
  loginFieldKey: string
  passwordFieldKey: string
  timeoutSeconds: number
  allowDevFallback: boolean
  updatedAtUtc?: string | null
}

type AuthProviderProbeResult = {
  success: boolean
  authMode: string
  loginUrl: string
  statusCode: number | null
  message: string
  checkedAtUtc: string
}

type TwoFactorSettings = {
  enabled: boolean
  updatedAtUtc?: string | null
}

type TwoFactorAccount = {
  id: string
  username: string
  externalId: string
  twoFactorRequired: boolean
  hasSecret: boolean
  twoFactorEnrolledAtUtc?: string | null
  updatedAtUtc: string
}

type BrandingSettings = {
  productName: string
  developerName: string
  tagline: string
  supportUrl: string
  primaryColor: string
  accentColor: string
  logoText: string
  backgroundImageUrl: string
  backgroundOverlayOpacity: number
  loginCardPosition: 'left' | 'center' | 'right'
  loginCardWidth: number
}

type DeveloperSupportInfo = {
  displayName: string
  telegram: string
  discord: string
  website: string
  notes: string
}

type InstallTelemetrySettings = {
  enabled: boolean
  updatedAtUtc?: string | null
}

type ProjectInstallStat = {
  id: string
  projectName: string
  lastLauncherVersion: string
  seenCount: number
  firstSeenAtUtc: string
  lastSeenAtUtc: string
}

type S3Settings = {
  useS3: boolean
  localRootPath: string
  endpoint: string
  bucket: string
  accessKey: string
  secretKey: string
  forcePathStyle: boolean
  useSsl: boolean
  autoCreateBucket: boolean
  updatedAtUtc?: string | null
}

type StorageTestResult = {
  success: boolean
  useS3: boolean
  message: string
  probeKey: string
  roundTripMs: number
  testedAtUtc: string
}

type StorageMigrationResult = {
  dryRun: boolean
  sourceUseS3: boolean
  targetUseS3: boolean
  scanned: number
  copied: number
  skipped: number
  failed: number
  copiedBytes: number
  truncated: boolean
  durationMs: number
  startedAtUtc: string
  finishedAtUtc: string
  errors: string[]
}

type WizardPreflightCheck = {
  id: string
  label: string
  status: 'passed' | 'failed' | 'skipped'
  message: string
}

type WizardPreflightRun = {
  id: string
  actor: string
  ranAtUtc: string
  passedCount: number
  totalCount: number
  checks: WizardPreflightCheck[]
}

type NewsItem = {
  id: string
  title: string
  body: string
  source: string
  pinned: boolean
  enabled: boolean
  createdAtUtc: string
}

type NewsSourceSettings = {
  id: string
  name: string
  type: 'rss' | 'json' | 'markdown' | 'telegram' | 'vk'
  url: string
  enabled: boolean
  maxItems: number
  minFetchIntervalMinutes: number
  lastFetchAttemptAtUtc?: string | null
  lastSyncAtUtc?: string | null
  lastContentChangeAtUtc?: string | null
  lastSyncError: string
  updatedAtUtc: string
}

type NewsSourceForm = {
  id: string | null
  name: string
  type: 'rss' | 'json' | 'markdown' | 'telegram' | 'vk'
  url: string
  enabled: boolean
  maxItems: number
  minFetchIntervalMinutes: number
}

type NewsSourcesSyncResponse = {
  sourcesProcessed: number
  imported: number
  results: Array<{
    sourceId: string
    name: string
    type: string
    imported: number
    error: string
  }>
}

type NewsSyncSettings = {
  enabled: boolean
  intervalMinutes: number
  lastRunAtUtc?: string | null
  lastRunError: string
  updatedAtUtc?: string | null
}

type NewsRetentionSettings = {
  enabled: boolean
  maxItems: number
  maxAgeDays: number
  lastAppliedAtUtc?: string | null
  lastDeletedItems: number
  lastError: string
  updatedAtUtc?: string | null
}

type NewsRetentionDryRunResult = {
  enabled: boolean
  maxItems: number
  maxAgeDays: number
  totalItems: number
  wouldDeleteByAge: number
  wouldDeleteByOverflow: number
  wouldDeleteTotal: number
  wouldRemainItems: number
  calculatedAtUtc: string
}

type RuntimeRetentionSettings = {
  enabled: boolean
  intervalMinutes: number
  keepLast: number
  lastRunAtUtc?: string | null
  lastDeletedItems: number
  lastProfilesProcessed: number
  lastRunError: string
  updatedAtUtc?: string | null
}

type RuntimeRetentionDryRunResult = {
  enabled: boolean
  intervalMinutes: number
  keepLast: number
  profileSlugFilter: string
  maxProfiles: number
  previewKeysLimit: number
  profilesScanned: number
  profilesWithDeletions: number
  profilesReturned: number
  hasMoreProfiles: boolean
  totalDeleteCandidates: number
  profiles: Array<{
    profileSlug: string
    totalRuntimeObjects: number
    keepCount: number
    deleteCount: number
    deleteKeysPreview: string[]
    hasMoreDeleteKeys: boolean
  }>
  calculatedAtUtc: string
}

type BanItem = {
  id: string
  accountId: string | null
  accountUsername: string
  accountExternalId: string
  hwidHash: string
  reason: string
  createdAtUtc: string
  expiresAtUtc: string | null
  active: boolean
}

type CrashReportItem = {
  id: string
  crashId: string
  status: 'new' | 'resolved'
  profileSlug: string
  serverName: string
  routeCode: string
  launcherVersion: string
  osVersion: string
  javaVersion: string
  exitCode: number | null
  reason: string
  errorType: string
  logExcerpt: string
  metadataJson: string
  occurredAtUtc: string
  createdAtUtc: string
  updatedAtUtc: string
  resolvedAtUtc?: string | null
}

type DocumentationArticle = {
  id: string
  slug: string
  title: string
  category: string
  summary: string
  bodyMarkdown: string
  order: number
  published: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

type AdminAuditLog = {
  id: string
  action: string
  actor: string
  entityType: string
  entityId: string
  requestId: string
  remoteIp: string
  userAgent: string
  detailsJson: string
  createdAtUtc: string
}

type AuditCleanupResponse = {
  dryRun: boolean
  olderThanUtc: string
  requestedLimit: number
  totalEligible: number
  candidates: number
  deleted: number
  hasMore: boolean
  oldestCandidateAtUtc?: string | null
  newestCandidateAtUtc?: string | null
}

type Profile = {
  id: string
  name: string
  slug: string
  description: string
  enabled: boolean
  iconKey: string
  priority: number
  recommendedRamMb: number
  jvmArgsDefault: string
  gameArgsDefault: string
  bundledJavaPath: string
  bundledRuntimeKey: string
  bundledRuntimeSha256: string
  bundledRuntimeSizeBytes: number
  bundledRuntimeContentType: string
  latestBuildId: string
  latestManifestKey: string
  latestClientVersion: string
}

type Server = {
  id: string
  profileId: string
  name: string
  address: string
  port: number
  mainJarPath: string
  ruProxyAddress: string
  ruProxyPort: number
  ruJarPath: string
  iconKey: string
  loaderType: string
  mcVersion: string
  buildId: string
  enabled: boolean
  order: number
}

type ProfileForm = {
  name: string
  slug: string
  description: string
  enabled: boolean
  iconKey: string
  priority: number
  recommendedRamMb: number
  jvmArgsDefault: string
  gameArgsDefault: string
  bundledJavaPath: string
  bundledRuntimeKey: string
}
type ServerForm = Omit<Server, 'id'>

const wizardPreflightHistoryStorageKey = 'blp_wizard_preflight_history_v1'
const maxWizardPreflightHistoryRuns = 8

function isWizardPreflightStatus(value: unknown): value is WizardPreflightCheck['status'] {
  return value === 'passed' || value === 'failed' || value === 'skipped'
}

function sanitizeWizardPreflightCheck(value: unknown): WizardPreflightCheck | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const raw = value as Partial<WizardPreflightCheck>
  if (typeof raw.id !== 'string' || typeof raw.label !== 'string' || typeof raw.message !== 'string') {
    return null
  }

  if (!isWizardPreflightStatus(raw.status)) {
    return null
  }

  return {
    id: raw.id,
    label: raw.label,
    status: raw.status,
    message: raw.message,
  }
}

function sanitizeWizardPreflightRun(value: unknown): WizardPreflightRun | null {
  if (!value || typeof value !== 'object') {
    return null
  }

  const raw = value as Partial<WizardPreflightRun> & { checks?: unknown }
  if (
    typeof raw.id !== 'string' ||
    typeof raw.ranAtUtc !== 'string' ||
    typeof raw.passedCount !== 'number' ||
    typeof raw.totalCount !== 'number' ||
    !Array.isArray(raw.checks)
  ) {
    return null
  }

  const checks = raw.checks.map((item) => sanitizeWizardPreflightCheck(item)).filter((item): item is WizardPreflightCheck => item !== null)
  if (checks.length === 0) {
    return null
  }

  return {
    id: raw.id,
    actor: typeof raw.actor === 'string' && raw.actor.trim().length > 0 ? raw.actor.trim() : 'admin',
    ranAtUtc: raw.ranAtUtc,
    passedCount: Math.max(0, Math.floor(raw.passedCount)),
    totalCount: Math.max(checks.length, Math.floor(raw.totalCount)),
    checks,
  }
}

function loadWizardPreflightHistory(): WizardPreflightRun[] {
  try {
    const raw = localStorage.getItem(wizardPreflightHistoryStorageKey)
    if (!raw) {
      return []
    }

    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) {
      return []
    }

    return parsed
      .map((item) => sanitizeWizardPreflightRun(item))
      .filter((item): item is WizardPreflightRun => item !== null)
      .slice(0, maxWizardPreflightHistoryRuns)
  } catch {
    return []
  }
}
type NewsForm = Omit<NewsItem, 'id' | 'createdAtUtc'>
type HwidBanForm = {
  hwidHash: string
  reason: string
  expiresAtLocal: string
}
type AccountBanForm = {
  user: string
  reason: string
  expiresAtLocal: string
}
type DocumentationForm = {
  slug: string
  title: string
  category: string
  summary: string
  bodyMarkdown: string
  order: number
  published: boolean
}

const defaultProfileForm: ProfileForm = {
  name: '',
  slug: '',
  description: '',
  enabled: true,
  iconKey: '',
  priority: 100,
  recommendedRamMb: 2048,
  jvmArgsDefault: '-Xms1024M -Xmx2048M',
  gameArgsDefault: '',
  bundledJavaPath: '',
  bundledRuntimeKey: '',
}

const defaultServerForm: ServerForm = {
  profileId: '',
  name: '',
  address: '',
  port: 25565,
  mainJarPath: 'minecraft_main.jar',
  ruProxyAddress: '',
  ruProxyPort: 25565,
  ruJarPath: 'minecraft_ru.jar',
  iconKey: '',
  loaderType: 'vanilla',
  mcVersion: '1.21.1',
  buildId: '',
  enabled: true,
  order: 100,
}

const defaultDocumentationForm: DocumentationForm = {
  slug: '',
  title: '',
  category: 'docs',
  summary: '',
  bodyMarkdown: '',
  order: 100,
  published: true,
}

const defaultDiscordForm = {
  enabled: true,
  appId: '',
  detailsText: '',
  stateText: '',
  largeImageKey: '',
  largeImageText: '',
  smallImageKey: '',
  smallImageText: '',
}

const defaultDiscordRpcSettings: DiscordRpcSettings = {
  enabled: true,
  privacyMode: false,
  updatedAtUtc: null,
}

const defaultNewsForm: NewsForm = {
  title: '',
  body: '',
  source: 'manual',
  pinned: false,
  enabled: true,
}

const defaultNewsSourceForm: NewsSourceForm = {
  id: null,
  name: '',
  type: 'rss',
  url: '',
  enabled: true,
  maxItems: 5,
  minFetchIntervalMinutes: 10,
}

const defaultNewsSyncSettings: NewsSyncSettings = {
  enabled: false,
  intervalMinutes: 60,
  lastRunAtUtc: null,
  lastRunError: '',
  updatedAtUtc: null,
}

const defaultNewsRetentionSettings: NewsRetentionSettings = {
  enabled: false,
  maxItems: 500,
  maxAgeDays: 30,
  lastAppliedAtUtc: null,
  lastDeletedItems: 0,
  lastError: '',
  updatedAtUtc: null,
}

const defaultRuntimeRetentionSettings: RuntimeRetentionSettings = {
  enabled: false,
  intervalMinutes: 360,
  keepLast: 3,
  lastRunAtUtc: null,
  lastDeletedItems: 0,
  lastProfilesProcessed: 0,
  lastRunError: '',
  updatedAtUtc: null,
}

const defaultHwidBanForm: HwidBanForm = {
  hwidHash: '',
  reason: '',
  expiresAtLocal: '',
}

const defaultAccountBanForm: AccountBanForm = {
  user: '',
  reason: '',
  expiresAtLocal: '',
}

const defaultAuthProviderSettings: AuthProviderSettings = {
  authMode: 'external',
  loginUrl: '',
  loginFieldKey: 'username',
  passwordFieldKey: 'password',
  timeoutSeconds: 15,
  allowDevFallback: true,
  updatedAtUtc: null,
}

const defaultTwoFactorSettings: TwoFactorSettings = {
  enabled: false,
  updatedAtUtc: null,
}

const defaultBrandingSettings: BrandingSettings = {
  productName: 'BivLauncher',
  developerName: 'Bivashka',
  tagline: 'Managed launcher platform',
  supportUrl: 'https://example.com/support',
  primaryColor: '#2F6FED',
  accentColor: '#20C997',
  logoText: 'BLP',
  backgroundImageUrl: '',
  backgroundOverlayOpacity: 0.55,
  loginCardPosition: 'center',
  loginCardWidth: 460,
}

const defaultDeveloperSupportInfo: DeveloperSupportInfo = {
  displayName: 'Bivashka',
  telegram: 'https://t.me/bivashka',
  discord: 'bivashka',
  website: 'https://github.com/bivashka',
  notes: 'Official developer support contact. Not editable from admin UI.',
}

const defaultInstallTelemetrySettings: InstallTelemetrySettings = {
  enabled: true,
  updatedAtUtc: null,
}

const defaultS3Settings: S3Settings = {
  useS3: true,
  localRootPath: '/app/storage',
  endpoint: 'http://localhost:9000',
  bucket: 'launcher-files',
  accessKey: 'minioadmin',
  secretKey: 'minioadmin',
  forcePathStyle: true,
  useSsl: false,
  autoCreateBucket: true,
  updatedAtUtc: null,
}

const supportedLoaders = ['vanilla', 'forge', 'fabric', 'quilt', 'neoforge', 'liteloader'] as const
const jvmArgPresets: Array<{ id: string; label: string; value: string }> = [
  { id: 'balanced', label: 'Balanced 2G', value: '-Xms1024M -Xmx2048M -XX:+UseG1GC -XX:+ParallelRefProcEnabled' },
  { id: 'performance', label: 'Performance 4G', value: '-Xms2048M -Xmx4096M -XX:+UseG1GC -XX:MaxGCPauseMillis=120 -XX:+UnlockExperimentalVMOptions' },
  { id: 'lowmem', label: 'Low Memory 1G', value: '-Xms512M -Xmx1024M -XX:+UseSerialGC' },
]
const gameArgPresets: Array<{ id: string; label: string; value: string }> = [
  { id: 'default', label: 'Default', value: '--server {SERVER_HOST} --port {SERVER_PORT}' },
  { id: 'fullscreen', label: 'Fullscreen', value: '--fullscreen --server {SERVER_HOST} --port {SERVER_PORT}' },
  { id: 'windowed', label: 'Windowed 1280x720', value: '--width 1280 --height 720 --server {SERVER_HOST} --port {SERVER_PORT}' },
]
const dashboardPages: Array<{ id: DashboardPage; title: string; subtitle: string }> = [
  { id: 'overview', title: 'Overview', subtitle: 'Status and quick metrics' },
  { id: 'wizard', title: 'Setup Wizard', subtitle: 'Guided first configuration and readiness checks' },
  { id: 'servers', title: 'Servers & Profiles', subtitle: 'Game topology and profile data' },
  { id: 'build', title: 'Build & Runtime', subtitle: 'Artifacts, retention and cosmetics' },
  { id: 'news', title: 'News', subtitle: 'Content, sources and sync policies' },
  { id: 'integrations', title: 'Integrations', subtitle: 'Discord, auth provider and S3' },
  { id: 'security', title: 'Security', subtitle: 'Bans and account protection' },
  { id: 'crashes', title: 'Crashes', subtitle: 'Crash logs, triage and export' },
  { id: 'docs', title: 'Documentation', subtitle: 'Guides, FAQ and searchable markdown docs' },
  { id: 'settings', title: 'Branding', subtitle: 'Product visuals and identity' },
  { id: 'audit', title: 'Audit Logs', subtitle: 'Traceability, export and cleanup' },
]

const uiExactTranslations: Record<string, string> = {
  'Loading...': 'Загрузка...',
  'Syncing...': 'Синхронизация...',
  Live: 'Онлайн',
  Workspace: 'Рабочая область',
  Logout: 'Выйти',
  Profiles: 'Профили',
  Servers: 'Серверы',
  News: 'Новости',
  Bans: 'Баны',
  'Control Center': 'Центр управления',
  'Stable API auth': 'Стабильная API-авторизация',
  'Per-profile runtime': 'Рантайм на профиль',
  'Live sync controls': 'Управление синхронизацией',
  'Audit export + cleanup': 'Экспорт и очистка аудита',
  Overview: 'Обзор',
  'Status and quick metrics': 'Статус и быстрые метрики',
  'Servers & Profiles': 'Серверы и профили',
  'Game topology and profile data': 'Топология игры и данные профилей',
  'Build & Runtime': 'Сборки и рантайм',
  'Artifacts, retention and cosmetics': 'Артефакты, ретеншн и косметика',
  Integrations: 'Интеграции',
  'Discord, auth provider and S3': 'Discord, auth provider и S3',
  Security: 'Безопасность',
  'Bans and account protection': 'Баны и защита аккаунтов',
  Branding: 'Брендинг',
  'Product visuals and identity': 'Визуал и идентичность продукта',
  'Audit Logs': 'Журнал аудита',
  'Traceability, export and cleanup': 'Трассировка, экспорт и очистка',
  'First run setup': 'Первичная настройка',
  Username: 'Логин',
  Password: 'Пароль',
  Creating: 'Создание',
  'Create admin': 'Создать администратора',
  'Admin login': 'Вход администратора',
  'Signing in...': 'Вход...',
  'Sign in': 'Войти',
  'Edit profile': 'Редактировать профиль',
  'Create profile': 'Создать профиль',
  Name: 'Название',
  'Slug (example: main-survival)': 'Slug (пример: main-survival)',
  Description: 'Описание',
  'Icon key (S3 key)': 'Ключ иконки (S3)',
  'Upload icon': 'Загрузить иконку',
  Priority: 'Приоритет',
  'RAM MB': 'RAM МБ',
  'Bundled Java path (relative, optional)': 'Путь встроенной Java (относительный, опционально)',
  'Bundled runtime artifact key (S3 key, optional)': 'Ключ встроенного рантайма (S3, опционально)',
  'Runtime metadata': 'Метаданные рантайма',
  Enabled: 'Включено',
  'Update profile': 'Обновить профиль',
  'Edit server': 'Редактировать сервер',
  'Create server': 'Создать сервер',
  'Select profile': 'Выберите профиль',
  Address: 'Адрес',
  Port: 'Порт',
  'Main jar path (DE route)': 'Путь main jar (DE маршрут)',
  'RU proxy address': 'Адрес RU-прокси',
  'RU proxy port': 'Порт RU-прокси',
  'RU jar path (proxy route)': 'Путь RU jar (proxy маршрут)',
  'MC Version': 'Версия MC',
  'Update server': 'Обновить сервер',
  'Skins / Capes': 'Скины / Кейпы',
  'Username or ExternalId': 'Имя пользователя или ExternalId',
  'Upload skin': 'Загрузить скин',
  'Upload cape': 'Загрузить кейп',
  'Java Runtime Artifact': 'Артефакт Java Runtime',
  'Select profile slug': 'Выберите slug профиля',
  'Upload runtime': 'Загрузить рантайм',
  'Runtime key override for verify (optional)': 'Ключ рантайма для проверки (опционально)',
  'Verify runtime artifact': 'Проверить артефакт рантайма',
  'Keep last N': 'Хранить последние N',
  'Dry run': 'Пробный запуск',
  'Cleanup old runtimes': 'Очистить старые рантаймы',
  'Runtime Retention Schedule': 'Расписание ретеншна рантайма',
  'Enable background runtime cleanup': 'Включить фоновую очистку рантайма',
  'Interval minutes': 'Интервал, минуты',
  'Save runtime retention': 'Сохранить ретеншн рантайма',
  'Dry-run': 'Пробный запуск',
  'Run retention now': 'Запустить ретеншн сейчас',
  'Dry-run profile slug (optional)': 'Slug профиля для dry-run (опционально)',
  'Dry-run max profiles': 'Макс. профилей в dry-run',
  'Delete keys preview limit': 'Лимит ключей в превью удаления',
  'Export dry-run JSON': 'Экспорт dry-run JSON',
  'Apply from dry-run': 'Применить из dry-run',
  'Copy delete keys': 'Скопировать ключи удаления',
  'Dry-run summary': 'Сводка dry-run',
  'no keys': 'нет ключей',
  'not configured': 'не настроено',
  'no icon': 'без иконки',
  'not set': 'не задано',
  Rebuild: 'Пересобрать',
  Edit: 'Редактировать',
  Delete: 'Удалить',
  'Discord RPC': 'Discord RPC',
  Profile: 'Профиль',
  Server: 'Сервер',
  'Select scope': 'Выберите область',
  'App ID': 'ID приложения',
  'Details text': 'Текст details',
  'State text': 'Текст state',
  'Large image key': 'Ключ большой картинки',
  'Large image text': 'Текст большой картинки',
  'Small image key': 'Ключ маленькой картинки',
  'Small image text': 'Текст маленькой картинки',
  Load: 'Загрузить',
  Save: 'Сохранить',
  'Rebuild options': 'Параметры пересборки',
  'MC version (for loader path)': 'Версия MC (для пути loader)',
  'Source sub-path override (optional)': 'Переопределение подпути source (опционально)',
  'Java runtime path override (optional)': 'Переопределение пути Java runtime (опционально)',
  'launch: auto': 'launch: auto',
  'launch: jar': 'launch: jar',
  'launch: mainclass': 'launch: mainclass',
  'Launch main class (optional)': 'Главный класс запуска (опционально)',
  'Launch classpath entries (one per line, supports globs: libraries/**/*.jar)':
    'Элементы classpath запуска (по одному на строку, поддерживаются globs: libraries/**/*.jar)',
  'Publish buildId to profile servers': 'Публиковать buildId на серверы профиля',
  'Edit news': 'Редактировать новость',
  'Create news': 'Создать новость',
  Title: 'Заголовок',
  'Body (Markdown/JSON/plain text)': 'Текст (Markdown/JSON/plain text)',
  'Source (manual/rss/json)': 'Источник (manual/rss/json)',
  'Source (manual/rss/json/telegram/vk)': 'Источник (manual/rss/json/telegram/vk)',
  Pinned: 'Закреплено',
  'Update news': 'Обновить новость',
  Reset: 'Сбросить',
  'News Sources': 'Источники новостей',
  'Source name': 'Название источника',
  URL: 'URL',
  'Max items': 'Макс. элементов',
  'Update source': 'Обновить источник',
  'Add source': 'Добавить источник',
  'Save source settings': 'Сохранить настройки источников',
  'Sync news now': 'Синхронизировать новости',
  'Sources Status': 'Статус источников',
  Sync: 'Синхронизировать',
  'News Auto-sync': 'Автосинхронизация новостей',
  'Enable background sync': 'Включить фоновую синхронизацию',
  'Save auto-sync settings': 'Сохранить настройки автосинхронизации',
  'Run now': 'Запустить',
  'Auto-sync Status': 'Статус автосинхронизации',
  'Enabled / Interval': 'Включено / Интервал',
  'Last run': 'Последний запуск',
  'Last error': 'Последняя ошибка',
  Updated: 'Обновлено',
  'News Retention': 'Ретеншн новостей',
  'Enable retention policy': 'Включить политику ретеншна',
  'Max age days': 'Макс. возраст (дни)',
  'Save retention settings': 'Сохранить настройки ретеншна',
  'Retention Status': 'Статус ретеншна',
  Limits: 'Лимиты',
  'Last apply / deleted': 'Последнее применение / удалено',
  'Dry-run preview': 'Превью dry-run',
  'HWID ban': 'Бан HWID',
  'Ban HWID': 'Забанить HWID',
  'Account ban': 'Бан аккаунта',
  'Ban account': 'Забанить аккаунт',
  'Reset account HWID': 'Сбросить HWID аккаунта',
  'Active / History bans': 'Активные / история банов',
  Remove: 'Снять бан',
  'Auth Provider': 'Auth Provider',
  'Login URL (external auth endpoint)': 'Login URL (внешний auth endpoint)',
  'Timeout seconds': 'Таймаут, сек',
  'Allow dev fallback (local player auth when URL empty)': 'Разрешить dev fallback (локальный auth при пустом URL)',
  'Save auth provider settings': 'Сохранить настройки auth provider',
  'Auth Provider Status': 'Статус auth provider',
  'S3 Storage': 'Хранилище S3',
  Bucket: 'Bucket',
  'Access key': 'Access key',
  'Secret key': 'Secret key',
  'Force path style': 'Force path style',
  'Use SSL (https)': 'Использовать SSL (https)',
  'Auto create bucket': 'Автосоздание bucket',
  'Save S3 settings': 'Сохранить настройки S3',
  'S3 Status': 'Статус S3',
  'Endpoint / Bucket': 'Endpoint / Bucket',
  'Path style / SSL': 'Path style / SSL',
  'Product name': 'Название продукта',
  'Developer name': 'Имя разработчика',
  Tagline: 'Теглайн',
  'Support URL': 'URL поддержки',
  'Primary color (#RRGGBB)': 'Основной цвет (#RRGGBB)',
  'Accent color (#RRGGBB)': 'Акцентный цвет (#RRGGBB)',
  'Logo text': 'Текст логотипа',
  'Save branding settings': 'Сохранить настройки брендинга',
  'Branding Preview': 'Предпросмотр брендинга',
  'Product / Developer': 'Продукт / Разработчик',
  Colors: 'Цвета',
  'Support / Logo': 'Поддержка / Логотип',
  'Admin Audit Logs': 'Журналы аудита администратора',
  'Action prefix (e.g. runtime)': 'Префикс действия (например, runtime)',
  'Actor (exact)': 'Исполнитель (точное совпадение)',
  'Entity type (exact)': 'Тип сущности (точное совпадение)',
  'Entity id contains': 'Entity id содержит',
  'Request id (exact)': 'Request id (точное совпадение)',
  'Remote IP (exact)': 'Remote IP (точное совпадение)',
  'From (local)': 'С (локально)',
  'To (local)': 'По (локально)',
  Limit: 'Лимит',
  'Range:': 'Диапазон:',
  'Clear filters': 'Очистить фильтры',
  'Sort: newest first': 'Сортировка: новые сверху',
  'Sort: oldest first': 'Сортировка: старые сверху',
  'Presets:': 'Пресеты:',
  upload: 'загрузка',
  verify: 'проверка',
  cleanup: 'очистка',
  run: 'запуск',
  apply: 'применить',
  all: 'все',
  'Refresh logs': 'Обновить логи',
  'Load more': 'Загрузить еще',
  'Export limit': 'Лимит экспорта',
  'Export JSON': 'Экспорт JSON',
  'Export CSV': 'Экспорт CSV',
  'Export uses current filters and sort.': 'Экспорт использует текущие фильтры и сортировку.',
  'Cleanup older than (days)': 'Очистка старше (дней)',
  'Cleanup batch limit': 'Лимит пачки очистки',
  'Cleanup dry-run': 'Очистка dry-run',
  'Cleanup apply': 'Применить очистку',
  'Audit Feed': 'Лента аудита',

  'Setup Wizard': '?????? ?????????',
  'Guided first configuration and readiness checks': '????????? ????????? ????????? ? ???????? ??????????',
  'Pre-flight checks': '??????????????? ????????',
  'Run pre-flight': '????????? pre-flight',
  'Recent runs': '????????? ???????',
  'Clear history': '???????? ???????',
  'No checks yet': '???? ??? ????????',
  Passed: '????????',
  Failed: '?????????',
  Skipped: '?????????',
  'All checks passed': '??? ???????? ????????',
}

const uiPartialTranslations: Array<[string, string]> = [
  ['Cannot reach API. Check backend and VITE_API_BASE_URL.', 'Нет доступа к API. Проверьте backend и VITE_API_BASE_URL.'],
  ['Request failed', 'Ошибка запроса'],
  ['Missing admin token', 'Отсутствует токен администратора'],
  ['Profile saved.', 'Профиль сохранен.'],
  ['Profile deleted.', 'Профиль удален.'],
  ['Server saved.', 'Сервер сохранен.'],
  ['Server deleted.', 'Сервер удален.'],
  ['News item saved.', 'Новость сохранена.'],
  ['News item deleted.', 'Новость удалена.'],
  ['saved.', 'сохранено.'],
  ['failed', 'ошибка'],
  ['Loaded: ', 'Загружено: '],
  ['next offset: ', 'следующий offset: '],
  ['has more: ', 'есть еще: '],
  ['sort: ', 'сортировка: '],
  ['actor: ', 'исполнитель: '],
  ['entity: ', 'сущность: '],
  ['req: ', 'request: '],
  ['ip: ', 'ip: '],
  ['expires: ', 'истекает: '],
  ['active: ', 'активен: '],
  ['created: ', 'создан: '],
  ['account: ', 'аккаунт: '],
  ['pinned: ', 'закреплено: '],
  ['enabled: ', 'включено: '],
  ['last sync: ', 'последняя синхронизация: '],
  ['profiles: ', 'профилей: '],
  ['deleted: ', 'удалено: '],
  ['error: ', 'ошибка: '],
  ['Session expired. Please log in again.', '?????? ???????. ??????? ?????.'],
  ['Too many requests. Retry shortly.', '??????? ????? ????????. ????????? ???? ?????.'],
  ['Too many requests. Retry in ', '??????? ????? ????????. ????????? ????? '],
  [' second(s).', ' ???.'],
  ['Pre-flight history cleared.', '??????? pre-flight ???????.'],
  ['Pre-flight history cleared locally (backend endpoint unavailable).', '??????? pre-flight ??????? ???????? (endpoint ?? backend ??????????).'],
  ['Failed to clear pre-flight history', '?? ??????? ???????? ??????? pre-flight'],
  ['Loaded pre-flight snapshot from ', '???????? ?????? pre-flight ?? '],
  ['Pre-flight complete: ', 'Pre-flight ????????: '],
  ['Actor: ', '?????: '],
  ['Runs API health, storage round-trip and auth-provider probe. Use before first public release.', '????????? API health, ????? storage ? auth-provider probe. ??????????? ????? ?????? ????????? ???????.'],
  ['Run pre-flight to collect live readiness status.', '????????? pre-flight, ????? ???????? ?????????? ?????? ??????????.'],
]

function translateUiText(input: string): string {
  const trimmed = input.trim()
  if (!trimmed) {
    return input
  }

  const exact = uiExactTranslations[trimmed]
  if (exact) {
    return input.replace(trimmed, exact)
  }

  let translated = input
  for (const [from, to] of uiPartialTranslations) {
    translated = translated.replaceAll(from, to)
  }

  return translated
}

function translateDomTree(root: ParentNode): void {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  let node: Node | null = walker.nextNode()

  while (node) {
    const value = node.nodeValue ?? ''
    if (/[A-Za-z]/.test(value)) {
      const nextValue = translateUiText(value)
      if (nextValue !== value) {
        node.nodeValue = nextValue
      }
    }

    node = walker.nextNode()
  }

  if (!(root instanceof Element) && !(root instanceof Document)) {
    return
  }

  const elements = root.querySelectorAll<HTMLElement>('[placeholder],[title],[aria-label]')
  for (const element of elements) {
    const placeholder = element.getAttribute('placeholder')
    if (placeholder && /[A-Za-z]/.test(placeholder)) {
      const nextPlaceholder = translateUiText(placeholder)
      if (nextPlaceholder !== placeholder) {
        element.setAttribute('placeholder', nextPlaceholder)
      }
    }

    const title = element.getAttribute('title')
    if (title && /[A-Za-z]/.test(title)) {
      const nextTitle = translateUiText(title)
      if (nextTitle !== title) {
        element.setAttribute('title', nextTitle)
      }
    }

    const ariaLabel = element.getAttribute('aria-label')
    if (ariaLabel && /[A-Za-z]/.test(ariaLabel)) {
      const nextAriaLabel = translateUiText(ariaLabel)
      if (nextAriaLabel !== ariaLabel) {
        element.setAttribute('aria-label', nextAriaLabel)
      }
    }
  }
}

function formatBytes(sizeBytes: number): string {
  if (sizeBytes <= 0) {
    return '0 B'
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = sizeBytes
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }

  const precision = value >= 10 || unitIndex === 0 ? 0 : 1
  return `${value.toFixed(precision)} ${units[unitIndex]}`
}

function toUtcIsoStringFromLocalInput(value: string): string {
  const trimmed = value.trim()
  if (!trimmed) {
    return ''
  }

  const parsed = new Date(trimmed)
  if (Number.isNaN(parsed.getTime())) {
    return ''
  }

  return parsed.toISOString()
}

function toLocalDateTimeInputValue(value: Date): string {
  const pad2 = (item: number) => String(item).padStart(2, '0')
  const year = value.getFullYear()
  const month = pad2(value.getMonth() + 1)
  const day = pad2(value.getDate())
  const hours = pad2(value.getHours())
  const minutes = pad2(value.getMinutes())
  return `${year}-${month}-${day}T${hours}:${minutes}`
}

function splitArgs(raw: string): string[] {
  const input = raw.trim()
  if (!input) {
    return []
  }

  const result: string[] = []
  let current = ''
  let inQuotes = false
  for (let i = 0; i < input.length; i += 1) {
    const ch = input[i]
    if (ch === '"') {
      inQuotes = !inQuotes
      continue
    }

    if (!inQuotes && /\s/.test(ch)) {
      if (current) {
        result.push(current)
        current = ''
      }
      continue
    }

    current += ch
  }

  if (current) {
    result.push(current)
  }

  return result
}

function analyzeArgs(raw: string, mode: 'jvm' | 'game'): { errors: string[]; warnings: string[] } {
  const errors: string[] = []
  const warnings: string[] = []
  const normalized = raw.replace(/\r\n/g, '\n').trim()
  if (normalized.length > 4096) {
    errors.push('Too long: max 4096 chars.')
  }

  if (/[;&|`]/.test(normalized)) {
    errors.push('Shell separators are not allowed: ; & | `')
  }

  const args = splitArgs(normalized)
  const dangerousPatterns = [
    '-javaagent',
    '-agentlib',
    '-XX:OnError',
    '-XX:ErrorFile',
    '-XX:OnOutOfMemoryError',
    '-Djavax.net.ssl.keyStorePassword',
    '-Djavax.net.ssl.trustStorePassword',
  ]

  for (const token of args) {
    if (dangerousPatterns.some((pattern) => token.startsWith(pattern))) {
      warnings.push(`Dangerous arg detected: ${token}`)
    }
  }

  if (mode === 'jvm' && args.length > 40) {
    warnings.push(`Too many JVM args (${args.length}).`)
  }

  if (mode === 'game' && args.length > 60) {
    warnings.push(`Too many game args (${args.length}).`)
  }

  return {
    errors,
    warnings: Array.from(new Set(warnings)),
  }
}

function toSlug(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, '-')
    .replace(/-{2,}/g, '-')
    .replace(/^-+/, '')
    .replace(/-+$/, '')
}

function App() {
  const apiBaseUrl = useMemo(() => {
    const configured = import.meta.env.VITE_API_BASE_URL
    if (configured && configured.trim()) {
      return configured.trim()
    }

    const protocol = window.location.protocol === 'https:' ? 'https:' : 'http:'
    return `${protocol}//${window.location.hostname}:8080`
  }, [])
  const currentOrigin = useMemo(() => window.location.origin, [])

  const [phase, setPhase] = useState<Phase>('loading')
  const [username, setUsername] = useState('admin')
  const [password, setPassword] = useState('')
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('blp_admin_token'))
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [activePage, setActivePage] = useState<DashboardPage>('overview')
  const [wizardProjectName, setWizardProjectName] = useState('')
  const [wizardProfileSlug, setWizardProfileSlug] = useState('')
  const [wizardServerName, setWizardServerName] = useState('')
  const [wizardServerAddress, setWizardServerAddress] = useState('127.0.0.1')
  const [wizardServerPort, setWizardServerPort] = useState(25565)
  const [wizardLoaderType, setWizardLoaderType] = useState<string>('vanilla')
  const [wizardMcVersion, setWizardMcVersion] = useState('1.21.1')

  const [profiles, setProfiles] = useState<Profile[]>([])
  const [servers, setServers] = useState<Server[]>([])
  const [newsItems, setNewsItems] = useState<NewsItem[]>([])
  const [newsSources, setNewsSources] = useState<NewsSourceSettings[]>([])
  const [newsSyncSettings, setNewsSyncSettings] = useState<NewsSyncSettings>(defaultNewsSyncSettings)
  const [newsRetentionSettings, setNewsRetentionSettings] = useState<NewsRetentionSettings>(defaultNewsRetentionSettings)
  const [newsRetentionDryRun, setNewsRetentionDryRun] = useState<NewsRetentionDryRunResult | null>(null)
  const [runtimeRetentionSettings, setRuntimeRetentionSettings] = useState<RuntimeRetentionSettings>(defaultRuntimeRetentionSettings)
  const [runtimeRetentionDryRun, setRuntimeRetentionDryRun] = useState<RuntimeRetentionDryRunResult | null>(null)
  const [auditLogs, setAuditLogs] = useState<AdminAuditLog[]>([])
  const [auditLogsOffset, setAuditLogsOffset] = useState(0)
  const [auditLogsHasMore, setAuditLogsHasMore] = useState(false)
  const [auditLogActionPrefix, setAuditLogActionPrefix] = useState('')
  const [auditLogActor, setAuditLogActor] = useState('')
  const [auditLogEntityType, setAuditLogEntityType] = useState('')
  const [auditLogEntityId, setAuditLogEntityId] = useState('')
  const [auditLogRequestId, setAuditLogRequestId] = useState('')
  const [auditLogRemoteIp, setAuditLogRemoteIp] = useState('')
  const [auditLogFromLocal, setAuditLogFromLocal] = useState('')
  const [auditLogToLocal, setAuditLogToLocal] = useState('')
  const [auditLogLimit, setAuditLogLimit] = useState(50)
  const [auditLogSortOrder, setAuditLogSortOrder] = useState<'desc' | 'asc'>('desc')
  const [auditExportLimit, setAuditExportLimit] = useState(5000)
  const [auditCleanupOlderThanDays, setAuditCleanupOlderThanDays] = useState(90)
  const [auditCleanupLimit, setAuditCleanupLimit] = useState(5000)
  const [runtimeRetentionDryRunProfileSlug, setRuntimeRetentionDryRunProfileSlug] = useState('')
  const [runtimeRetentionDryRunMaxProfiles, setRuntimeRetentionDryRunMaxProfiles] = useState(20)
  const [runtimeRetentionDryRunKeysLimit, setRuntimeRetentionDryRunKeysLimit] = useState(10)
  const [bans, setBans] = useState<BanItem[]>([])
  const [crashes, setCrashes] = useState<CrashReportItem[]>([])
  const [crashStatusFilter, setCrashStatusFilter] = useState<'all' | 'new' | 'resolved'>('new')
  const [crashProfileSlugFilter, setCrashProfileSlugFilter] = useState('')
  const [crashSearchFilter, setCrashSearchFilter] = useState('')
  const [crashFromLocal, setCrashFromLocal] = useState('')
  const [crashToLocal, setCrashToLocal] = useState('')
  const [crashLimit, setCrashLimit] = useState(50)
  const [docs, setDocs] = useState<DocumentationArticle[]>([])
  const [editingDocId, setEditingDocId] = useState<string | null>(null)
  const [docForm, setDocForm] = useState<DocumentationForm>(defaultDocumentationForm)
  const [docSearchFilter, setDocSearchFilter] = useState('')
  const [docCategoryFilter, setDocCategoryFilter] = useState('')
  const [docPublishedOnlyFilter, setDocPublishedOnlyFilter] = useState(false)
  const [editingProfileId, setEditingProfileId] = useState<string | null>(null)
  const [editingServerId, setEditingServerId] = useState<string | null>(null)
  const [editingNewsId, setEditingNewsId] = useState<string | null>(null)
  const [profileForm, setProfileForm] = useState<ProfileForm>(defaultProfileForm)
  const [serverForm, setServerForm] = useState<ServerForm>(defaultServerForm)
  const [newsForm, setNewsForm] = useState<NewsForm>(defaultNewsForm)
  const [newsSourceForm, setNewsSourceForm] = useState<NewsSourceForm>(defaultNewsSourceForm)
  const [hwidBanForm, setHwidBanForm] = useState<HwidBanForm>(defaultHwidBanForm)
  const [accountBanForm, setAccountBanForm] = useState<AccountBanForm>(defaultAccountBanForm)
  const [authProviderSettings, setAuthProviderSettings] = useState<AuthProviderSettings>(defaultAuthProviderSettings)
  const [authProbeResult, setAuthProbeResult] = useState<AuthProviderProbeResult | null>(null)
  const [wizardPreflightChecks, setWizardPreflightChecks] = useState<WizardPreflightCheck[]>([])
  const [wizardPreflightHistory, setWizardPreflightHistory] = useState<WizardPreflightRun[]>(() => loadWizardPreflightHistory())
  const [twoFactorSettings, setTwoFactorSettings] = useState<TwoFactorSettings>(defaultTwoFactorSettings)
  const [twoFactorAccounts, setTwoFactorAccounts] = useState<TwoFactorAccount[]>([])
  const [twoFactorSearch, setTwoFactorSearch] = useState('')
  const [twoFactorRequiredOnly, setTwoFactorRequiredOnly] = useState(false)
  const [twoFactorLimit, setTwoFactorLimit] = useState(100)
  const [brandingSettings, setBrandingSettings] = useState<BrandingSettings>(defaultBrandingSettings)
  const [developerSupportInfo, setDeveloperSupportInfo] = useState<DeveloperSupportInfo>(defaultDeveloperSupportInfo)
  const [installTelemetrySettings, setInstallTelemetrySettings] = useState<InstallTelemetrySettings>(defaultInstallTelemetrySettings)
  const [projectInstallStats, setProjectInstallStats] = useState<ProjectInstallStat[]>([])
  const [s3Settings, setS3Settings] = useState<S3Settings>(defaultS3Settings)
  const [storageTestResult, setStorageTestResult] = useState<StorageTestResult | null>(null)
  const [storageMigrationTargetMode, setStorageMigrationTargetMode] = useState<'s3' | 'local'>('local')
  const [storageMigrationDryRun, setStorageMigrationDryRun] = useState(true)
  const [storageMigrationOverwrite, setStorageMigrationOverwrite] = useState(true)
  const [storageMigrationMaxObjects, setStorageMigrationMaxObjects] = useState(5000)
  const [storageMigrationPrefix, setStorageMigrationPrefix] = useState('')
  const [storageMigrationResult, setStorageMigrationResult] = useState<StorageMigrationResult | null>(null)
  const [profileIconFile, setProfileIconFile] = useState<File | null>(null)
  const [serverIconFile, setServerIconFile] = useState<File | null>(null)
  const [runtimeProfileSlug, setRuntimeProfileSlug] = useState('')
  const [runtimeFile, setRuntimeFile] = useState<File | null>(null)
  const [runtimeVerifyKey, setRuntimeVerifyKey] = useState('')
  const [runtimeVerifyResult, setRuntimeVerifyResult] = useState<RuntimeVerifyResponse | null>(null)
  const [runtimeCleanupKeepLast, setRuntimeCleanupKeepLast] = useState(3)
  const [runtimeCleanupDryRun, setRuntimeCleanupDryRun] = useState(true)
  const [runtimeCleanupResult, setRuntimeCleanupResult] = useState<RuntimeCleanupResponse | null>(null)
  const [cosmeticsUser, setCosmeticsUser] = useState('')
  const [skinFile, setSkinFile] = useState<File | null>(null)
  const [capeFile, setCapeFile] = useState<File | null>(null)
  const [discordScopeType, setDiscordScopeType] = useState<'profile' | 'server'>('profile')
  const [discordScopeId, setDiscordScopeId] = useState('')
  const [discordRpcSettings, setDiscordRpcSettings] = useState<DiscordRpcSettings>(defaultDiscordRpcSettings)
  const [discordForm, setDiscordForm] = useState(defaultDiscordForm)
  const [rebuildLoaderType, setRebuildLoaderType] = useState<string>('vanilla')
  const [rebuildMcVersion, setRebuildMcVersion] = useState('1.21.1')
  const [rebuildJvmArgsDefault, setRebuildJvmArgsDefault] = useState('')
  const [rebuildGameArgsDefault, setRebuildGameArgsDefault] = useState('')
  const [rebuildSourceSubPath, setRebuildSourceSubPath] = useState('')
  const [rebuildJavaRuntimePath, setRebuildJavaRuntimePath] = useState('')
  const [rebuildLaunchMode, setRebuildLaunchMode] = useState<'auto' | 'jar' | 'mainclass'>('auto')
  const [rebuildLaunchMainClass, setRebuildLaunchMainClass] = useState('')
  const [rebuildLaunchClasspath, setRebuildLaunchClasspath] = useState('')
  const [rebuildPublishToServers, setRebuildPublishToServers] = useState(true)
  const runtimeMetadataProfile = useMemo(() => {
    if (editingProfileId) {
      return profiles.find((profile) => profile.id === editingProfileId) ?? null
    }

    const slug = profileForm.slug.trim().toLowerCase()
    if (!slug) {
      return null
    }

    return profiles.find((profile) => profile.slug.toLowerCase() === slug) ?? null
  }, [profiles, editingProfileId, profileForm.slug])
  const profileJvmArgsAnalysis = useMemo(
    () => analyzeArgs(profileForm.jvmArgsDefault, 'jvm'),
    [profileForm.jvmArgsDefault],
  )
  const profileGameArgsAnalysis = useMemo(
    () => analyzeArgs(profileForm.gameArgsDefault, 'game'),
    [profileForm.gameArgsDefault],
  )
  const rebuildJvmArgsAnalysis = useMemo(
    () => analyzeArgs(rebuildJvmArgsDefault, 'jvm'),
    [rebuildJvmArgsDefault],
  )
  const rebuildGameArgsAnalysis = useMemo(
    () => analyzeArgs(rebuildGameArgsDefault, 'game'),
    [rebuildGameArgsDefault],
  )
  const filteredDocs = useMemo(() => {
    const search = docSearchFilter.trim().toLowerCase()
    const category = docCategoryFilter.trim().toLowerCase()

    return docs.filter((article) => {
      if (docPublishedOnlyFilter && !article.published) {
        return false
      }

      if (category && article.category.toLowerCase() !== category) {
        return false
      }

      if (!search) {
        return true
      }

      const haystack = `${article.title} ${article.summary} ${article.slug} ${article.bodyMarkdown}`.toLowerCase()
      return haystack.includes(search)
    })
  }, [docs, docSearchFilter, docCategoryFilter, docPublishedOnlyFilter])
  const wizardSteps = useMemo(() => {
    const hasEnabledProfile = profiles.some((profile) => profile.enabled)
    const hasEnabledServer = servers.some((server) => server.enabled)
    const authReady = authProviderSettings.authMode === 'any'
      ? true
      : Boolean(authProviderSettings.loginUrl.trim())
    const storageReady = s3Settings.useS3
      ? [s3Settings.endpoint, s3Settings.bucket, s3Settings.accessKey, s3Settings.secretKey]
        .every((value) => Boolean(value.trim()))
      : Boolean(s3Settings.localRootPath.trim())
    const docsReady = docs.some((item) => item.published)
    const brandingReady = Boolean(brandingSettings.productName.trim()) && Boolean(brandingSettings.primaryColor.trim())
    const twoFactorReviewed = !twoFactorSettings.enabled || twoFactorAccounts.some((account) => account.twoFactorRequired)

    return [
      {
        id: 'topology',
        title: 'Create game topology',
        description: 'At least one enabled profile and one enabled server.',
        done: hasEnabledProfile && hasEnabledServer,
        page: 'servers' as DashboardPage,
        required: true,
        hint: 'Open Servers & Profiles and create initial profile/server pair.',
      },
      {
        id: 'auth',
        title: 'Configure auth mode',
        description: 'External login URL is set (or ANY mode intentionally selected).',
        done: authReady,
        page: 'integrations' as DashboardPage,
        required: true,
        hint: 'In Integrations, review auth provider mode and request field mapping.',
      },
      {
        id: 'storage',
        title: 'Validate storage',
        description: 'Storage settings are complete for selected mode.',
        done: storageReady,
        page: 'integrations' as DashboardPage,
        required: true,
        hint: 'Save S3/Local settings and run Test storage.',
      },
      {
        id: 'branding',
        title: 'Brand launcher identity',
        description: 'Product name and primary color are configured.',
        done: brandingReady,
        page: 'settings' as DashboardPage,
        required: false,
        hint: 'In Branding, verify name, colors and launcher preview.',
      },
      {
        id: 'docs',
        title: 'Publish docs/FAQ',
        description: 'At least one published documentation article exists.',
        done: docsReady,
        page: 'docs' as DashboardPage,
        required: false,
        hint: 'Use Documentation page and load starter docs for quick bootstrap.',
      },
      {
        id: 'security',
        title: 'Review 2FA policy',
        description: '2FA policy reviewed with required accounts when enabled.',
        done: twoFactorReviewed,
        page: 'security' as DashboardPage,
        required: false,
        hint: 'In Security, enable global 2FA and mark critical accounts if needed.',
      },
    ]
  }, [
    profiles,
    servers,
    authProviderSettings.authMode,
    authProviderSettings.loginUrl,
    s3Settings.useS3,
    s3Settings.localRootPath,
    s3Settings.endpoint,
    s3Settings.bucket,
    s3Settings.accessKey,
    s3Settings.secretKey,
    docs,
    brandingSettings.productName,
    brandingSettings.primaryColor,
    twoFactorSettings.enabled,
    twoFactorAccounts,
  ])
  const wizardRequiredTotal = useMemo(
    () => wizardSteps.filter((step) => step.required).length,
    [wizardSteps],
  )
  const wizardRequiredDone = useMemo(
    () => wizardSteps.filter((step) => step.required && step.done).length,
    [wizardSteps],
  )
  const wizardOptionalDone = useMemo(
    () => wizardSteps.filter((step) => !step.required && step.done).length,
    [wizardSteps],
  )
  const wizardRequiredPercent = useMemo(() => {
    if (wizardRequiredTotal === 0) {
      return 100
    }

    return Math.round((wizardRequiredDone / wizardRequiredTotal) * 100)
  }, [wizardRequiredDone, wizardRequiredTotal])
  const wizardNormalizedSlug = useMemo(
    () => toSlug(wizardProfileSlug || wizardProjectName),
    [wizardProfileSlug, wizardProjectName],
  )
  const wizardHasSlugConflict = useMemo(
    () => profiles.some((profile) => profile.slug.toLowerCase() === wizardNormalizedSlug),
    [profiles, wizardNormalizedSlug],
  )

  useEffect(() => {
    void determineStartPhase()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (runtimeVerifyKey.trim()) {
      return
    }

    const selectedProfile = profiles.find((profile) => profile.slug === runtimeProfileSlug)
    if (selectedProfile?.bundledRuntimeKey) {
      setRuntimeVerifyKey(selectedProfile.bundledRuntimeKey)
    }
  }, [profiles, runtimeProfileSlug, runtimeVerifyKey])

  useEffect(() => {
    setRuntimeCleanupResult(null)
    setRuntimeVerifyResult(null)
  }, [runtimeProfileSlug])

  useEffect(() => {
    setStorageMigrationTargetMode(s3Settings.useS3 ? 'local' : 's3')
  }, [s3Settings.useS3])

  useEffect(() => {
    try {
      localStorage.setItem(wizardPreflightHistoryStorageKey, JSON.stringify(wizardPreflightHistory))
    } catch {
    }
  }, [wizardPreflightHistory])

  useEffect(() => {
    const applyTranslation = () => {
      translateDomTree(document.body)
    }

    applyTranslation()

    const observer = new MutationObserver(() => {
      applyTranslation()
    })

    observer.observe(document.body, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ['placeholder', 'title', 'aria-label'],
    })

    return () => {
      observer.disconnect()
    }
  }, [])

  async function determineStartPhase() {
    setError('')
    setNotice('')
    try {
      const response = await fetch(`${apiBaseUrl}/api/admin/setup/status`)
      if (!response.ok) {
        const responseText = await response.text()
        const compactResponse = responseText.trim().slice(0, 220)
        const suffix = compactResponse ? `: ${compactResponse}` : ''
        throw new Error(`API responded with ${response.status} on setup status${suffix}`)
      }

      const payload = (await response.json()) as SetupStatusResponse
      if (payload.needsSetup) {
        setPhase('setup')
        return
      }

      const savedToken = localStorage.getItem('blp_admin_token')
      if (!savedToken) {
        setPhase('login')
        return
      }

      setToken(savedToken)
      setPhase('dashboard')
      setActivePage('overview')
      await loadAdminData(savedToken)
    } catch (error) {
      setPhase('login')
      if (error instanceof TypeError) {
        setError(`Cannot reach API from ${window.location.origin}. Check ADMIN_ALLOWED_ORIGINS and VITE_API_BASE_URL.`)
        return
      }

      if (error instanceof Error) {
        setError(error.message)
        return
      }

      setError('Cannot reach API. Check backend and VITE_API_BASE_URL.')
    }
  }

  async function requestWithAuth<T>(url: string, init?: RequestInit): Promise<T> {
    if (!token) {
      throw new Error('Missing admin token')
    }

    const headers = new Headers(init?.headers)
    headers.set('Authorization', `Bearer ${token}`)
    if (init?.body) {
      headers.set('Content-Type', 'application/json')
    }

    const response = await fetch(`${apiBaseUrl}${url}`, {
      ...init,
      headers,
    })

    if (!response.ok) {
      const text = await response.text()
      let parsedError = ''
      let retryAfterSeconds: number | null = null

      if (text) {
        try {
          const payload = JSON.parse(text) as ApiError
          parsedError = payload.error ?? payload.title ?? ''
          if (typeof payload.retryAfterSeconds === 'number' && payload.retryAfterSeconds > 0) {
            retryAfterSeconds = Math.ceil(payload.retryAfterSeconds)
          }
        } catch {
          parsedError = text
        }
      }

      const retryHeader = response.headers.get('Retry-After')
      if (retryAfterSeconds === null && retryHeader) {
        const parsed = Number.parseInt(retryHeader, 10)
        if (Number.isFinite(parsed) && parsed > 0) {
          retryAfterSeconds = parsed
        }
      }

      if (response.status === 401) {
        localStorage.removeItem('blp_admin_token')
        setToken(null)
        setPhase('login')
        setActivePage('overview')
        throw new ApiRequestError('Session expired. Please log in again.', 401, retryAfterSeconds ?? undefined)
      }

      if (response.status === 429) {
        if (retryAfterSeconds !== null) {
          throw new ApiRequestError(
            parsedError || `Too many requests. Retry in ${retryAfterSeconds} second(s).`,
            429,
            retryAfterSeconds,
          )
        }

        throw new ApiRequestError(parsedError || 'Too many requests. Retry shortly.', 429, undefined)
      }

      throw new ApiRequestError(parsedError || `Request failed (${response.status})`, response.status, retryAfterSeconds ?? undefined)
    }

    if (response.status === 204) {
      return undefined as T
    }

    return (await response.json()) as T
  }

  function buildAuditFilterQuery(): URLSearchParams {
    const query = new URLSearchParams()
    query.set('sort', auditLogSortOrder)

    const actionPrefix = auditLogActionPrefix.trim()
    if (actionPrefix) {
      query.set('actionPrefix', actionPrefix)
    }

    const actor = auditLogActor.trim()
    if (actor) {
      query.set('actor', actor)
    }

    const entityType = auditLogEntityType.trim()
    if (entityType) {
      query.set('entityType', entityType)
    }

    const entityId = auditLogEntityId.trim()
    if (entityId) {
      query.set('entityId', entityId)
    }

    const requestId = auditLogRequestId.trim()
    if (requestId) {
      query.set('requestId', requestId)
    }

    const remoteIp = auditLogRemoteIp.trim()
    if (remoteIp) {
      query.set('remoteIp', remoteIp)
    }

    const fromUtc = toUtcIsoStringFromLocalInput(auditLogFromLocal)
    if (fromUtc) {
      query.set('fromUtc', fromUtc)
    }

    const toUtc = toUtcIsoStringFromLocalInput(auditLogToLocal)
    if (toUtc) {
      query.set('toUtc', toUtc)
    }

    return query
  }

  function buildAuditLogsQuery(offset = 0): string {
    const query = buildAuditFilterQuery()
    query.set('limit', String(Math.min(500, Math.max(1, Number(auditLogLimit) || 50))))
    query.set('offset', String(Math.max(0, offset)))
    return query.toString()
  }

  async function fetchAuditLogs(activeToken: string, offset: number, append: boolean) {
    const normalizedLimit = Math.min(500, Math.max(1, Number(auditLogLimit) || 50))
    const response = await fetch(`${apiBaseUrl}/api/admin/audit-logs?${buildAuditLogsQuery(offset)}`, {
      headers: { Authorization: `Bearer ${activeToken}` },
    })

    if (!response.ok) {
      throw new Error('Unable to load audit logs.')
    }

    const loaded = (await response.json()) as AdminAuditLog[]
    if (append) {
      setAuditLogs((prev) => {
        const seen = new Set(prev.map((item) => item.id))
        const merged = [...prev]
        for (const item of loaded) {
          if (seen.has(item.id)) {
            continue
          }

          merged.push(item)
          seen.add(item.id)
        }

        return merged
      })
    } else {
      setAuditLogs(loaded)
    }

    setAuditLogsOffset(offset + loaded.length)
    setAuditLogsHasMore(loaded.length >= normalizedLimit)
  }

  function buildCrashFilterQuery(): URLSearchParams {
    const query = new URLSearchParams()
    query.set('limit', String(Math.min(500, Math.max(1, Number(crashLimit) || 50))))
    query.set('offset', '0')

    if (crashStatusFilter !== 'all') {
      query.set('status', crashStatusFilter)
    }

    const profileSlug = crashProfileSlugFilter.trim().toLowerCase()
    if (profileSlug) {
      query.set('profileSlug', profileSlug)
    }

    const search = crashSearchFilter.trim()
    if (search) {
      query.set('search', search)
    }

    const fromUtc = toUtcIsoStringFromLocalInput(crashFromLocal)
    if (fromUtc) {
      query.set('fromUtc', fromUtc)
    }

    const toUtc = toUtcIsoStringFromLocalInput(crashToLocal)
    if (toUtc) {
      query.set('toUtc', toUtc)
    }

    return query
  }

  async function fetchCrashes(activeToken: string) {
    const response = await fetch(`${apiBaseUrl}/api/admin/crashes?${buildCrashFilterQuery().toString()}`, {
      headers: { Authorization: `Bearer ${activeToken}` },
    })

    if (!response.ok) {
      throw new Error('Unable to load crashes.')
    }

    const loaded = (await response.json()) as CrashReportItem[]
    setCrashes(loaded)
  }

  function buildDocFilterQuery(): URLSearchParams {
    const query = new URLSearchParams()
    const search = docSearchFilter.trim()
    const category = docCategoryFilter.trim().toLowerCase()
    if (search) {
      query.set('search', search)
    }
    if (category) {
      query.set('category', category)
    }
    if (docPublishedOnlyFilter) {
      query.set('publishedOnly', 'true')
    }

    return query
  }

  async function fetchDocs(activeToken: string) {
    const response = await fetch(`${apiBaseUrl}/api/admin/docs?${buildDocFilterQuery().toString()}`, {
      headers: { Authorization: `Bearer ${activeToken}` },
    })

    if (!response.ok) {
      throw new Error('Unable to load docs.')
    }

    const loaded = (await response.json()) as DocumentationArticle[]
    setDocs(loaded)
  }

  function buildTwoFactorAccountsQuery(): URLSearchParams {
    const query = new URLSearchParams()
    const search = twoFactorSearch.trim()
    if (search) {
      query.set('search', search)
    }
    if (twoFactorRequiredOnly) {
      query.set('requiredOnly', 'true')
    }
    query.set('limit', String(Math.min(500, Math.max(1, twoFactorLimit || 100))))
    return query
  }

  async function fetchTwoFactorAccounts(activeToken: string) {
    const response = await fetch(`${apiBaseUrl}/api/admin/settings/two-factor/accounts?${buildTwoFactorAccountsQuery().toString()}`, {
      headers: { Authorization: `Bearer ${activeToken}` },
    })

    if (!response.ok) {
      throw new Error('Unable to load 2FA accounts.')
    }

    const loaded = (await response.json()) as TwoFactorAccount[]
    setTwoFactorAccounts(loaded)
  }

  async function loadAdminData(activeToken: string) {
    setError('')
    try {
      const [profileData, serverData, newsData, newsSourcesData, newsSyncData, newsRetentionData, runtimeRetentionData, bansData, crashData, docsData, authProviderData, discordRpcSettingsData, twoFactorData, twoFactorAccountsData, brandingData, developerSupportData, installTelemetrySettingsData, projectInstallStatsData, s3Data] = await Promise.all([
        fetch(`${apiBaseUrl}/api/admin/profiles`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/servers`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/news`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/news-sources`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/news-sync`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/news-retention`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/runtime-retention`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/bans`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/crashes?${buildCrashFilterQuery().toString()}`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/docs`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/auth-provider`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/discord-rpc`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/two-factor`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/two-factor/accounts?limit=100`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/branding`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/support/developer`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/install-telemetry`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/install-telemetry/projects?limit=200`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/s3`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
      ])

      if (
        !profileData.ok ||
        !serverData.ok ||
        !newsData.ok ||
        !newsSourcesData.ok ||
        !newsSyncData.ok ||
        !newsRetentionData.ok ||
        !runtimeRetentionData.ok ||
        !bansData.ok ||
        !crashData.ok ||
        !docsData.ok ||
        !authProviderData.ok ||
        !discordRpcSettingsData.ok ||
        !twoFactorData.ok ||
        !twoFactorAccountsData.ok ||
        !brandingData.ok ||
        !developerSupportData.ok ||
        !installTelemetrySettingsData.ok ||
        !projectInstallStatsData.ok ||
        !s3Data.ok
      ) {
        throw new Error('Unable to load admin data.')
      }

      const loadedProfiles = (await profileData.json()) as Profile[]
      const loadedServers = (await serverData.json()) as Server[]
      const loadedNews = (await newsData.json()) as NewsItem[]
      const loadedNewsSources = (await newsSourcesData.json()) as NewsSourceSettings[]
      const loadedNewsSyncSettings = (await newsSyncData.json()) as NewsSyncSettings
      const loadedNewsRetentionSettings = (await newsRetentionData.json()) as NewsRetentionSettings
      const loadedRuntimeRetentionSettings = (await runtimeRetentionData.json()) as RuntimeRetentionSettings
      const loadedBans = (await bansData.json()) as BanItem[]
      const loadedCrashes = (await crashData.json()) as CrashReportItem[]
      const loadedDocs = (await docsData.json()) as DocumentationArticle[]
      const loadedAuthProviderSettings = (await authProviderData.json()) as AuthProviderSettings
      const loadedDiscordRpcSettings = (await discordRpcSettingsData.json()) as DiscordRpcSettings
      const loadedTwoFactorSettings = (await twoFactorData.json()) as TwoFactorSettings
      const loadedTwoFactorAccounts = (await twoFactorAccountsData.json()) as TwoFactorAccount[]
      const loadedBranding = (await brandingData.json()) as BrandingSettings
      const loadedDeveloperSupport = (await developerSupportData.json()) as DeveloperSupportInfo
      const loadedInstallTelemetrySettings = (await installTelemetrySettingsData.json()) as InstallTelemetrySettings
      const loadedProjectInstallStats = (await projectInstallStatsData.json()) as ProjectInstallStat[]
      const loadedS3Settings = (await s3Data.json()) as S3Settings
      setProfiles(loadedProfiles)
      setServers(loadedServers)
      setNewsItems(loadedNews)
      setNewsSources(
        loadedNewsSources.map((source) => ({
          ...source,
          minFetchIntervalMinutes: Math.min(1440, Math.max(1, Number(source.minFetchIntervalMinutes) || 10)),
          lastFetchAttemptAtUtc: source.lastFetchAttemptAtUtc ?? null,
          lastContentChangeAtUtc: source.lastContentChangeAtUtc ?? null,
        })),
      )
      setNewsSyncSettings(loadedNewsSyncSettings)
      setNewsRetentionSettings(loadedNewsRetentionSettings)
      setRuntimeRetentionSettings(loadedRuntimeRetentionSettings)
      setBans(loadedBans)
      setCrashes(loadedCrashes)
      setDocs(loadedDocs)
      setAuthProviderSettings(loadedAuthProviderSettings)
      setDiscordRpcSettings(loadedDiscordRpcSettings)
      setTwoFactorSettings(loadedTwoFactorSettings)
      setTwoFactorAccounts(loadedTwoFactorAccounts)
      setBrandingSettings(loadedBranding)
      setDeveloperSupportInfo(loadedDeveloperSupport)
      setInstallTelemetrySettings(loadedInstallTelemetrySettings)
      setProjectInstallStats(loadedProjectInstallStats)
      setS3Settings(loadedS3Settings)
      setStorageTestResult(null)
      setStorageMigrationResult(null)

      // Optional bootstrap call: keep dashboard usable on older backend versions without this endpoint.
      try {
        const wizardPreflightHistoryData = await fetch(`${apiBaseUrl}/api/admin/wizard/preflight-runs?limit=${maxWizardPreflightHistoryRuns}`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        })

        if (wizardPreflightHistoryData.ok) {
          const loadedWizardPreflightHistory = (await wizardPreflightHistoryData.json()) as WizardPreflightRun[]
          setWizardPreflightHistory(
            loadedWizardPreflightHistory
              .map((item) => sanitizeWizardPreflightRun(item))
              .filter((item): item is WizardPreflightRun => item !== null)
              .slice(0, maxWizardPreflightHistoryRuns),
          )
        }
      } catch {
      }

      await fetchAuditLogs(activeToken, 0, false)

      setServerForm((prev) => ({
        ...prev,
        profileId: prev.profileId || loadedProfiles[0]?.id || '',
      }))
      if (!discordScopeId) {
        setDiscordScopeId(loadedProfiles[0]?.id || loadedServers[0]?.id || '')
      }
      if (!runtimeProfileSlug) {
        setRuntimeProfileSlug(loadedProfiles[0]?.slug || '')
      }
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Unable to load admin data.')
    }
  }

  async function onSetupSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await fetch(`${apiBaseUrl}/api/admin/setup`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })

      if (!response.ok) {
        const payload = (await response.json()) as ApiError
        throw new Error(payload.error ?? 'Setup failed')
      }

      setPassword('')
      setPhase('login')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Setup failed')
    } finally {
      setBusy(false)
    }
  }

  async function onLoginSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await fetch(`${apiBaseUrl}/api/admin/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })

      if (!response.ok) {
        throw new Error('Invalid username or password')
      }

      const payload = (await response.json()) as LoginResponse
      localStorage.setItem('blp_admin_token', payload.token)
      setToken(payload.token)
      setPassword('')
      setPhase('dashboard')
      setActivePage('overview')
      await loadAdminData(payload.token)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Login failed')
    } finally {
      setBusy(false)
    }
  }

  async function onProfileSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const argsErrors = [...profileJvmArgsAnalysis.errors, ...profileGameArgsAnalysis.errors]
    if (argsErrors.length > 0) {
      setError(`Args validation failed: ${argsErrors.join(' ')}`)
      return
    }
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const method = editingProfileId ? 'PUT' : 'POST'
      const path = editingProfileId ? `/api/admin/profiles/${editingProfileId}` : '/api/admin/profiles'
      await requestWithAuth<Profile>(path, {
        method,
        body: JSON.stringify(profileForm),
      })

      setProfileForm(defaultProfileForm)
      setEditingProfileId(null)
      setProfileIconFile(null)
      setNotice('Profile saved.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Profile save failed')
    } finally {
      setBusy(false)
    }
  }

  function onProfileEdit(profile: Profile) {
    setEditingProfileId(profile.id)
    setRuntimeProfileSlug(profile.slug)
    setRuntimeVerifyKey(profile.bundledRuntimeKey || '')
    setRuntimeVerifyResult(null)
    setRuntimeCleanupResult(null)
    setRebuildJvmArgsDefault(profile.jvmArgsDefault || '-Xms1024M -Xmx2048M')
    setRebuildGameArgsDefault(profile.gameArgsDefault || '')
    setProfileForm({
      name: profile.name,
      slug: profile.slug,
      description: profile.description,
      enabled: profile.enabled,
      iconKey: profile.iconKey,
      priority: profile.priority,
      recommendedRamMb: profile.recommendedRamMb,
      jvmArgsDefault: profile.jvmArgsDefault || '-Xms1024M -Xmx2048M',
      gameArgsDefault: profile.gameArgsDefault || '',
      bundledJavaPath: profile.bundledJavaPath || '',
      bundledRuntimeKey: profile.bundledRuntimeKey || '',
    })
  }

  async function onProfileDelete(profileId: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<void>(`/api/admin/profiles/${profileId}`, { method: 'DELETE' })
      if (editingProfileId === profileId) {
        setEditingProfileId(null)
        setProfileForm(defaultProfileForm)
        setProfileIconFile(null)
      }
      setNotice('Profile deleted.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Profile delete failed')
    } finally {
      setBusy(false)
    }
  }

  async function onServerSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const method = editingServerId ? 'PUT' : 'POST'
      const path = editingServerId ? `/api/admin/servers/${editingServerId}` : '/api/admin/servers'
      await requestWithAuth<Server>(path, {
        method,
        body: JSON.stringify(serverForm),
      })

      setEditingServerId(null)
      setServerForm({
        ...defaultServerForm,
        profileId: profiles[0]?.id || '',
      })
      setServerIconFile(null)
      setNotice('Server saved.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Server save failed')
    } finally {
      setBusy(false)
    }
  }

  function onServerEdit(server: Server) {
    setEditingServerId(server.id)
    setServerForm({
      profileId: server.profileId,
      name: server.name,
      address: server.address,
      port: server.port,
      mainJarPath: server.mainJarPath || 'minecraft_main.jar',
      ruProxyAddress: server.ruProxyAddress || '',
      ruProxyPort: server.ruProxyPort || 25565,
      ruJarPath: server.ruJarPath || 'minecraft_ru.jar',
      iconKey: server.iconKey,
      loaderType: server.loaderType,
      mcVersion: server.mcVersion,
      buildId: server.buildId,
      enabled: server.enabled,
      order: server.order,
    })
  }

  async function onServerDelete(serverId: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<void>(`/api/admin/servers/${serverId}`, { method: 'DELETE' })
      if (editingServerId === serverId) {
        setEditingServerId(null)
        setServerForm({
          ...defaultServerForm,
          profileId: profiles[0]?.id || '',
        })
        setServerIconFile(null)
      }
      setNotice('Server deleted.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Server delete failed')
    } finally {
      setBusy(false)
    }
  }

  async function onNewsSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const method = editingNewsId ? 'PUT' : 'POST'
      const path = editingNewsId ? `/api/admin/news/${editingNewsId}` : '/api/admin/news'
      await requestWithAuth<NewsItem>(path, {
        method,
        body: JSON.stringify(newsForm),
      })

      setEditingNewsId(null)
      setNewsForm(defaultNewsForm)
      setNotice('News item saved.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News save failed')
    } finally {
      setBusy(false)
    }
  }

  function onNewsEdit(item: NewsItem) {
    setEditingNewsId(item.id)
    setNewsForm({
      title: item.title,
      body: item.body,
      source: item.source,
      pinned: item.pinned,
      enabled: item.enabled,
    })
  }

  function onNewsResetForm() {
    setEditingNewsId(null)
    setNewsForm(defaultNewsForm)
  }

  async function onNewsDelete(newsId: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<void>(`/api/admin/news/${newsId}`, { method: 'DELETE' })
      if (editingNewsId === newsId) {
        setEditingNewsId(null)
        setNewsForm(defaultNewsForm)
      }
      setNotice('News item deleted.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News delete failed')
    } finally {
      setBusy(false)
    }
  }

  function onNewsSourceEdit(source: NewsSourceSettings) {
    setNewsSourceForm({
      id: source.id,
      name: source.name,
      type: source.type,
      url: source.url,
      enabled: source.enabled,
      maxItems: source.maxItems,
      minFetchIntervalMinutes: Math.min(1440, Math.max(1, Number(source.minFetchIntervalMinutes) || 10)),
    })
  }

  function onNewsSourceResetForm() {
    setNewsSourceForm(defaultNewsSourceForm)
  }

  function onNewsSourceSaveLocal() {
    const name = newsSourceForm.name.trim()
    const url = newsSourceForm.url.trim()
    if (!name || !url) {
      setError('News source name and URL are required.')
      return
    }

    setError('')
    setNewsSources((prev) => {
      const next: NewsSourceSettings = {
        id: newsSourceForm.id ?? crypto.randomUUID(),
        name,
        type: newsSourceForm.type,
        url,
        enabled: newsSourceForm.enabled,
        maxItems: Math.min(20, Math.max(1, newsSourceForm.maxItems || 5)),
        minFetchIntervalMinutes: Math.min(1440, Math.max(1, newsSourceForm.minFetchIntervalMinutes || 10)),
        lastFetchAttemptAtUtc: prev.find((x) => x.id === newsSourceForm.id)?.lastFetchAttemptAtUtc ?? null,
        lastSyncAtUtc: prev.find((x) => x.id === newsSourceForm.id)?.lastSyncAtUtc ?? null,
        lastContentChangeAtUtc: prev.find((x) => x.id === newsSourceForm.id)?.lastContentChangeAtUtc ?? null,
        lastSyncError: prev.find((x) => x.id === newsSourceForm.id)?.lastSyncError ?? '',
        updatedAtUtc: prev.find((x) => x.id === newsSourceForm.id)?.updatedAtUtc ?? new Date().toISOString(),
      }

      const withoutCurrent = prev.filter((x) => x.id !== next.id)
      return [...withoutCurrent, next].sort((a, b) => a.name.localeCompare(b.name))
    })
    setNotice('News source prepared locally. Save source settings to persist.')
    setNewsSourceForm(defaultNewsSourceForm)
  }

  function onNewsSourceDeleteLocal(sourceId: string) {
    setNewsSources((prev) => prev.filter((x) => x.id !== sourceId))
    if (newsSourceForm.id === sourceId) {
      setNewsSourceForm(defaultNewsSourceForm)
    }
    setNotice('News source removed locally. Save source settings to persist.')
  }

  async function onSaveNewsSourcesSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<NewsSourceSettings[]>('/api/admin/settings/news-sources', {
        method: 'PUT',
        body: JSON.stringify({
          sources: newsSources.map((source) => ({
            id: source.id,
            name: source.name,
            type: source.type,
            url: source.url,
            enabled: source.enabled,
            maxItems: source.maxItems,
            minFetchIntervalMinutes: source.minFetchIntervalMinutes,
          })),
        }),
      })

      setNewsSources(saved)
      setNotice('News source settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News source settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSyncNewsSources(force = false) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<NewsSourcesSyncResponse>(`/api/admin/settings/news-sources/sync?force=${force ? 'true' : 'false'}`, {
        method: 'POST',
        body: JSON.stringify({}),
      })

      const failed = result.results.filter((x) => !!x.error).length
      setNotice(`News sync done: imported ${result.imported}, sources ${result.sourcesProcessed}, failed ${failed}.`)
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News sync failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveNewsSyncSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<NewsSyncSettings>('/api/admin/settings/news-sync', {
        method: 'PUT',
        body: JSON.stringify({
          enabled: newsSyncSettings.enabled,
          intervalMinutes: newsSyncSettings.intervalMinutes,
        }),
      })

      setNewsSyncSettings(saved)
      setNotice('News auto-sync settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News auto-sync save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRunNewsAutoSyncNow() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<NewsSourcesSyncResponse>('/api/admin/settings/news-sync/run', {
        method: 'POST',
      })

      const failed = result.results.filter((x) => !!x.error).length
      setNotice(`Auto-sync run completed: imported ${result.imported}, sources ${result.sourcesProcessed}, failed ${failed}.`)
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Auto-sync run failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveNewsRetentionSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<NewsRetentionSettings>('/api/admin/settings/news-retention', {
        method: 'PUT',
        body: JSON.stringify({
          enabled: newsRetentionSettings.enabled,
          maxItems: newsRetentionSettings.maxItems,
          maxAgeDays: newsRetentionSettings.maxAgeDays,
        }),
      })

      setNewsRetentionSettings(saved)
      setNewsRetentionDryRun(null)
      setNotice('News retention settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News retention settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRunNewsRetentionNow() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<{
        applied: boolean
        deletedItems: number
        remainingItems: number
        appliedAtUtc: string
        error: string
      }>('/api/admin/settings/news-retention/run', {
        method: 'POST',
      })

      if (result.error) {
        setNotice(`Retention run finished with error: ${result.error}`)
      } else {
        setNotice(`Retention run done: deleted ${result.deletedItems}, remaining ${result.remainingItems}.`)
      }
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News retention run failed')
    } finally {
      setBusy(false)
    }
  }

  async function onDryRunNewsRetentionNow() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<NewsRetentionDryRunResult>('/api/admin/settings/news-retention/dry-run', {
        method: 'POST',
      })

      setNewsRetentionDryRun(result)
      setNotice(
        `Retention dry-run: delete ${result.wouldDeleteTotal} (age ${result.wouldDeleteByAge}, overflow ${result.wouldDeleteByOverflow}), remain ${result.wouldRemainItems}.`,
      )
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'News retention dry-run failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveRuntimeRetentionSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<RuntimeRetentionSettings>('/api/admin/settings/runtime-retention', {
        method: 'PUT',
        body: JSON.stringify({
          enabled: runtimeRetentionSettings.enabled,
          intervalMinutes: runtimeRetentionSettings.intervalMinutes,
          keepLast: runtimeRetentionSettings.keepLast,
        }),
      })

      setRuntimeRetentionSettings(saved)
      setRuntimeRetentionDryRun(null)
      setNotice('Runtime retention settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime retention settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRunRuntimeRetentionNow() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<{
        applied: boolean
        profilesProcessed: number
        deletedItems: number
        appliedAtUtc: string
        error: string
      }>('/api/admin/settings/runtime-retention/run', {
        method: 'POST',
      })

      if (result.error) {
        setNotice(`Runtime retention run finished with error: ${result.error}`)
      } else {
        setNotice(`Runtime retention run done: deleted ${result.deletedItems}, profiles ${result.profilesProcessed}.`)
      }
      setRuntimeRetentionDryRun(null)
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime retention run failed')
    } finally {
      setBusy(false)
    }
  }

  async function onDryRunRuntimeRetentionNow() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const query = new URLSearchParams()
      const profileSlug = runtimeRetentionDryRunProfileSlug.trim()
      if (profileSlug) {
        query.set('profileSlug', profileSlug)
      }

      const maxProfiles = Math.min(200, Math.max(1, Number(runtimeRetentionDryRunMaxProfiles) || 20))
      const previewKeysLimit = Math.min(100, Math.max(1, Number(runtimeRetentionDryRunKeysLimit) || 10))
      query.set('maxProfiles', String(maxProfiles))
      query.set('previewKeysLimit', String(previewKeysLimit))

      const result = await requestWithAuth<RuntimeRetentionDryRunResult>(`/api/admin/settings/runtime-retention/dry-run?${query.toString()}`, {
        method: 'POST',
      })

      setRuntimeRetentionDryRun(result)
      setNotice(
        `Runtime retention dry-run: delete ${result.totalDeleteCandidates} across ${result.profilesWithDeletions}/${result.profilesScanned} profiles.`,
      )
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime retention dry-run failed')
    } finally {
      setBusy(false)
    }
  }

  function onExportRuntimeRetentionDryRun() {
    if (!runtimeRetentionDryRun) {
      setError('Run runtime retention dry-run first.')
      return
    }

    const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
    const fileName = `runtime-retention-dry-run-${timestamp}.json`
    const payload = JSON.stringify(runtimeRetentionDryRun, null, 2)
    const blob = new Blob([payload], { type: 'application/json;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
    setNotice(`Dry-run report exported: ${fileName}`)
  }

  async function onApplyRuntimeRetentionFromDryRun() {
    if (!runtimeRetentionDryRun) {
      setError('Run runtime retention dry-run first.')
      return
    }

    const deleteCandidatesInScope = runtimeRetentionDryRun.profiles
      .reduce((acc, profile) => acc + profile.deleteCount, 0)
    if (deleteCandidatesInScope <= 0) {
      setError('No delete candidates in current dry-run scope.')
      return
    }

    const hiddenProfiles = runtimeRetentionDryRun.profilesWithDeletions - runtimeRetentionDryRun.profilesReturned
    const confirmationLines = [
      'Apply runtime retention from current dry-run scope?',
      `Profiles in scope: ${runtimeRetentionDryRun.profilesReturned}`,
      `Delete candidates in scope: ${deleteCandidatesInScope}`,
      hiddenProfiles > 0
        ? `Note: ${hiddenProfiles} profiles are outside current maxProfiles filter and will NOT be affected.`
        : '',
      'This action cannot be undone.',
    ].filter((line) => line.length > 0)

    if (!window.confirm(confirmationLines.join('\n'))) {
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const query = new URLSearchParams()
      if (runtimeRetentionDryRun.profileSlugFilter) {
        query.set('profileSlug', runtimeRetentionDryRun.profileSlugFilter)
      }

      query.set('maxProfiles', String(runtimeRetentionDryRun.maxProfiles))
      const result = await requestWithAuth<{
        applied: boolean
        profilesProcessed: number
        deletedItems: number
        appliedAtUtc: string
        error: string
      }>(`/api/admin/settings/runtime-retention/run-from-preview?${query.toString()}`, {
        method: 'POST',
      })

      if (result.error) {
        setNotice(`Runtime retention apply-from-dry-run finished with error: ${result.error}`)
      } else {
        setNotice(`Runtime retention applied from dry-run: deleted ${result.deletedItems}, profiles ${result.profilesProcessed}.`)
      }

      setRuntimeRetentionDryRun(null)
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime retention apply-from-dry-run failed')
    } finally {
      setBusy(false)
    }
  }

  async function onCopyToClipboard(value: string, label: string) {
    const payload = value.trim()
    if (!payload) {
      setError(`Nothing to copy for ${label}.`)
      return
    }

    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(payload)
    } else {
      const textarea = document.createElement('textarea')
      textarea.value = payload
      document.body.appendChild(textarea)
      textarea.select()
      document.execCommand('copy')
      textarea.remove()
    }

    setNotice(`${label} copied.`)
  }

  async function onCopyRuntimeRetentionDeleteKeys() {
    if (!runtimeRetentionDryRun) {
      setError('Run runtime retention dry-run first.')
      return
    }

    const keys = Array.from(
      new Set(
        runtimeRetentionDryRun.profiles.flatMap((profile) => profile.deleteKeysPreview),
      ),
    )

    if (keys.length === 0) {
      setError('No delete keys available in dry-run preview.')
      return
    }

    const payload = keys.join('\n')
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(payload)
    } else {
      const textarea = document.createElement('textarea')
      textarea.value = payload
      document.body.appendChild(textarea)
      textarea.select()
      document.execCommand('copy')
      textarea.remove()
    }

    const isPreviewOnly = runtimeRetentionDryRun.hasMoreProfiles ||
      runtimeRetentionDryRun.profiles.some((profile) => profile.hasMoreDeleteKeys)
    setNotice(`Copied ${keys.length} runtime keys${isPreviewOnly ? ' (preview subset)' : ''}.`)
  }

  async function onSyncSingleNewsSource(sourceId: string, sourceName: string, force = false) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const syncUrl = `/api/admin/settings/news-sources/sync?sourceId=${encodeURIComponent(sourceId)}${force ? '&force=true' : ''}`
      const forcedResult = await requestWithAuth<NewsSourcesSyncResponse>(syncUrl, {
        method: 'POST',
      })

      const imported = forcedResult.results[0]?.imported ?? 0
      const errorMessage = forcedResult.results[0]?.error ?? ''
      if (errorMessage) {
        setNotice(`Source "${sourceName}" synced with error: ${errorMessage}`)
      } else {
        setNotice(`Source "${sourceName}" synced. Imported: ${imported}.`)
      }
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Source sync failed')
    } finally {
      setBusy(false)
    }
  }

  async function onWizardQuickCreate() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    const profileName = wizardProjectName.trim()
    const serverName = wizardServerName.trim() || `${profileName} Main`
    const serverAddress = wizardServerAddress.trim()
    const profileSlug = wizardNormalizedSlug
    const port = Math.min(65535, Math.max(1, Number(wizardServerPort) || 25565))

    if (!profileName) {
      setError('Project name is required for quick setup.')
      return
    }

    if (!profileSlug || profileSlug.length < 2) {
      setError('Profile slug is invalid. Use latin letters, digits and dashes.')
      return
    }

    if (wizardHasSlugConflict) {
      setError(`Profile slug "${profileSlug}" already exists.`)
      return
    }

    if (!serverAddress) {
      setError('Server address is required for quick setup.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const createdProfile = await requestWithAuth<Profile>('/api/admin/profiles', {
        method: 'POST',
        body: JSON.stringify({
          name: profileName,
          slug: profileSlug,
          description: '',
          enabled: true,
          iconKey: '',
          priority: 100,
          recommendedRamMb: 2048,
          jvmArgsDefault: defaultProfileForm.jvmArgsDefault,
          gameArgsDefault: defaultProfileForm.gameArgsDefault,
          bundledJavaPath: '',
          bundledRuntimeKey: '',
        }),
      })

      await requestWithAuth<Server>('/api/admin/servers', {
        method: 'POST',
        body: JSON.stringify({
          profileId: createdProfile.id,
          name: serverName,
          address: serverAddress,
          port,
          mainJarPath: defaultServerForm.mainJarPath,
          ruProxyAddress: '',
          ruProxyPort: port,
          ruJarPath: defaultServerForm.ruJarPath,
          iconKey: '',
          loaderType: wizardLoaderType,
          mcVersion: wizardMcVersion.trim() || '1.21.1',
          buildId: '',
          enabled: true,
          order: 100,
        }),
      })

      setWizardProjectName('')
      setWizardProfileSlug('')
      setWizardServerName('')
      setWizardServerAddress('127.0.0.1')
      setWizardServerPort(25565)
      setWizardLoaderType('vanilla')
      setWizardMcVersion('1.21.1')
      setNotice(`Quick setup created profile "${createdProfile.name}" and first server.`)
      setActivePage('servers')
      await loadAdminData(token)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Quick setup failed')
    } finally {
      setBusy(false)
    }
  }

  async function onCreateHwidBan() {
    const hwidHash = hwidBanForm.hwidHash.trim().toLowerCase()
    if (!hwidHash) {
      setError('HWID hash is required.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<BanItem>('/api/admin/bans/hwid', {
        method: 'POST',
        body: JSON.stringify({
          hwidHash,
          reason: hwidBanForm.reason.trim(),
          expiresAtUtc: toUtcOrUndefined(hwidBanForm.expiresAtLocal),
        }),
      })
      setHwidBanForm(defaultHwidBanForm)
      setNotice('HWID ban created.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'HWID ban creation failed')
    } finally {
      setBusy(false)
    }
  }

  async function onCreateAccountBan() {
    const user = accountBanForm.user.trim()
    if (!user) {
      setError('Username or externalId is required.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<BanItem>(`/api/admin/bans/account/${encodeURIComponent(user)}`, {
        method: 'POST',
        body: JSON.stringify({
          reason: accountBanForm.reason.trim(),
          expiresAtUtc: toUtcOrUndefined(accountBanForm.expiresAtLocal),
        }),
      })
      setAccountBanForm(defaultAccountBanForm)
      setNotice('Account ban created.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Account ban creation failed')
    } finally {
      setBusy(false)
    }
  }

  async function onDeleteBan(banId: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<void>(`/api/admin/bans/${banId}`, { method: 'DELETE' })
      setNotice('Ban removed.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Ban delete failed')
    } finally {
      setBusy(false)
    }
  }

  async function onResetAccountHwid() {
    const user = accountBanForm.user.trim()
    if (!user) {
      setError('Username or externalId is required for HWID reset.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<{ hwidHashReset: boolean }>(`/api/admin/bans/account/${encodeURIComponent(user)}/reset-hwid`, {
        method: 'POST',
      })
      setNotice('Account HWID hash reset.')
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'HWID reset failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRefreshCrashes() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await fetchCrashes(token)
      setNotice('Crash reports refreshed.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Crash reports refresh failed')
    } finally {
      setBusy(false)
    }
  }

  function onSetCrashRangeDays(days: number) {
    const now = new Date()
    const from = new Date(now.getTime() - days * 24 * 60 * 60 * 1000)
    setCrashFromLocal(toLocalDateTimeInputValue(from))
    setCrashToLocal(toLocalDateTimeInputValue(now))
  }

  function onClearCrashFilters() {
    setCrashStatusFilter('new')
    setCrashProfileSlugFilter('')
    setCrashSearchFilter('')
    setCrashFromLocal('')
    setCrashToLocal('')
    setCrashLimit(50)
  }

  async function onUpdateCrashStatus(id: string, status: 'new' | 'resolved') {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const updated = await requestWithAuth<CrashReportItem>(`/api/admin/crashes/${id}/status`, {
        method: 'PUT',
        body: JSON.stringify({ status }),
      })
      setCrashes((prev) => prev.map((item) => (item.id === id ? updated : item)))
      setNotice(`Crash ${updated.crashId} marked as ${updated.status}.`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Crash status update failed')
    } finally {
      setBusy(false)
    }
  }

  async function onExportCrashes(format: 'json' | 'csv') {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const query = buildCrashFilterQuery()
      query.set('limit', '50000')
      query.set('format', format)
      query.delete('offset')

      const response = await fetch(`${apiBaseUrl}/api/admin/crashes/export?${query.toString()}`, {
        headers: { Authorization: `Bearer ${token}` },
      })

      if (!response.ok) {
        const text = await response.text()
        let parsedError = ''
        if (text) {
          try {
            const payload = JSON.parse(text) as ApiError
            parsedError = payload.error ?? payload.title ?? ''
          } catch {
            parsedError = text
          }
        }

        throw new Error(parsedError || 'Crash export failed.')
      }

      const blob = await response.blob()
      const extension = format === 'csv' ? 'csv' : 'json'
      const filename = `crashes-${new Date().toISOString().replace(/[:.]/g, '-')}.${extension}`
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = filename
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
      setNotice(`Crash export ready (${format.toUpperCase()}).`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Crash export failed')
    } finally {
      setBusy(false)
    }
  }

  function onDocEdit(article: DocumentationArticle) {
    setEditingDocId(article.id)
    setDocForm({
      slug: article.slug,
      title: article.title,
      category: article.category,
      summary: article.summary,
      bodyMarkdown: article.bodyMarkdown,
      order: article.order,
      published: article.published,
    })
  }

  function onDocResetForm() {
    setEditingDocId(null)
    setDocForm(defaultDocumentationForm)
  }

  async function onDocSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!docForm.slug.trim() || !docForm.title.trim() || !docForm.bodyMarkdown.trim()) {
      setError('Slug, title and markdown body are required.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const method = editingDocId ? 'PUT' : 'POST'
      const path = editingDocId ? `/api/admin/docs/${editingDocId}` : '/api/admin/docs'
      await requestWithAuth<DocumentationArticle>(path, {
        method,
        body: JSON.stringify({
          slug: docForm.slug.trim().toLowerCase(),
          title: docForm.title.trim(),
          category: docForm.category.trim().toLowerCase() || 'docs',
          summary: docForm.summary.trim(),
          bodyMarkdown: docForm.bodyMarkdown,
          order: docForm.order,
          published: docForm.published,
        }),
      })

      onDocResetForm()
      setNotice('Documentation article saved.')
      await fetchDocs(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Docs save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onDocDelete(id: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<void>(`/api/admin/docs/${id}`, { method: 'DELETE' })
      if (editingDocId === id) {
        onDocResetForm()
      }
      setNotice('Documentation article deleted.')
      await fetchDocs(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Docs delete failed')
    } finally {
      setBusy(false)
    }
  }

  async function onDocSeed() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await requestWithAuth<{ inserted: number; skipped: number }>('/api/admin/docs/seed', {
        method: 'POST',
      })
      setNotice(`Starter docs ready. Inserted: ${response.inserted}, skipped: ${response.skipped}.`)
      await fetchDocs(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Docs seed failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRefreshDocs() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await fetchDocs(token)
      setNotice('Documentation refreshed.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Docs refresh failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveAuthProviderSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<AuthProviderSettings>('/api/admin/settings/auth-provider', {
        method: 'PUT',
        body: JSON.stringify({
          authMode: authProviderSettings.authMode,
          loginUrl: authProviderSettings.loginUrl.trim(),
          loginFieldKey: authProviderSettings.loginFieldKey.trim(),
          passwordFieldKey: authProviderSettings.passwordFieldKey.trim(),
          timeoutSeconds: authProviderSettings.timeoutSeconds,
          allowDevFallback: authProviderSettings.allowDevFallback,
        }),
      })
      setAuthProviderSettings(saved)
      setAuthProbeResult(null)
      setNotice('Auth provider settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Auth provider save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onProbeAuthProvider() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<AuthProviderProbeResult>('/api/admin/settings/auth-provider/probe', {
        method: 'POST',
      })
      setAuthProbeResult(result)
      setNotice(`Auth probe ${result.success ? 'passed' : 'failed'}: ${result.message}`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Auth provider probe failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRunWizardPreflight() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const checks: WizardPreflightCheck[] = []

      try {
        const response = await fetch(`${apiBaseUrl}/health`)
        checks.push({
          id: 'api-health',
          label: 'API health',
          status: response.ok ? 'passed' : 'failed',
          message: response.ok ? 'Health endpoint is reachable.' : `HTTP ${response.status}`,
        })
      } catch (healthError) {
        checks.push({
          id: 'api-health',
          label: 'API health',
          status: 'failed',
          message: healthError instanceof Error ? healthError.message : 'Health endpoint request failed.',
        })
      }

      try {
        const storageResult = await requestWithAuth<StorageTestResult>('/api/admin/settings/s3/test', {
          method: 'POST',
          body: JSON.stringify({
            useS3: s3Settings.useS3,
            localRootPath: s3Settings.localRootPath.trim(),
            endpoint: s3Settings.endpoint.trim(),
            bucket: s3Settings.bucket.trim(),
            accessKey: s3Settings.accessKey.trim(),
            secretKey: s3Settings.secretKey.trim(),
            forcePathStyle: s3Settings.forcePathStyle,
            useSsl: s3Settings.useSsl,
            autoCreateBucket: s3Settings.autoCreateBucket,
          }),
        })

        setStorageTestResult(storageResult)
        checks.push({
          id: 'storage',
          label: 'Storage probe',
          status: storageResult.success ? 'passed' : 'failed',
          message: storageResult.message || `Round-trip ${storageResult.roundTripMs} ms`,
        })
      } catch (storageError) {
        checks.push({
          id: 'storage',
          label: 'Storage probe',
          status: 'failed',
          message: storageError instanceof Error ? storageError.message : 'Storage probe failed.',
        })
      }

      try {
        const authResult = await requestWithAuth<AuthProviderProbeResult>('/api/admin/settings/auth-provider/probe', {
          method: 'POST',
        })
        setAuthProbeResult(authResult)
        checks.push({
          id: 'auth-provider',
          label: 'Auth provider probe',
          status: authResult.success ? 'passed' : 'failed',
          message: authResult.message,
        })
      } catch (authError) {
        checks.push({
          id: 'auth-provider',
          label: 'Auth provider probe',
          status: 'failed',
          message: authError instanceof Error ? authError.message : 'Auth probe failed.',
        })
      }

      setWizardPreflightChecks(checks)
      const failed = checks.filter((check) => check.status === 'failed').length

      try {
        const savedRun = await requestWithAuth<WizardPreflightRun>('/api/admin/wizard/preflight-runs', {
          method: 'POST',
          body: JSON.stringify({ checks }),
        })

        const normalizedSavedRun = sanitizeWizardPreflightRun(savedRun)
        if (normalizedSavedRun) {
          setWizardPreflightHistory((prev) => {
            const merged = [normalizedSavedRun, ...prev.filter((item) => item.id !== normalizedSavedRun.id)]
            return merged.slice(0, maxWizardPreflightHistoryRuns)
          })
        }
      } catch {
        const run: WizardPreflightRun = {
          id: globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2, 10)}`,
          actor: 'admin',
          ranAtUtc: new Date().toISOString(),
          passedCount: checks.length - failed,
          totalCount: checks.length,
          checks,
        }
        setWizardPreflightHistory((prev) => [run, ...prev].slice(0, maxWizardPreflightHistoryRuns))
      }

      setNotice(`Pre-flight complete: ${checks.length - failed}/${checks.length} passed.`)
    } finally {
      setBusy(false)
    }
  }

  function onUseWizardPreflightRun(runId: string) {
    const run = wizardPreflightHistory.find((item) => item.id === runId)
    if (!run) {
      return
    }

    setWizardPreflightChecks(run.checks)
    setNotice(`Loaded pre-flight snapshot from ${new Date(run.ranAtUtc).toLocaleString()}.`)
    setError('')
  }

  async function onClearWizardPreflightHistory() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<{ deleted: number }>('/api/admin/wizard/preflight-runs', { method: 'DELETE' })
      setWizardPreflightHistory([])
      setNotice('Pre-flight history cleared.')
    } catch (caughtError) {
      if (caughtError instanceof ApiRequestError && caughtError.status === 404) {
        setWizardPreflightHistory([])
        setNotice('Pre-flight history cleared locally (backend endpoint unavailable).')
        return
      }

      setError(caughtError instanceof Error ? caughtError.message : 'Failed to clear pre-flight history')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveTwoFactorSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<TwoFactorSettings>('/api/admin/settings/two-factor', {
        method: 'PUT',
        body: JSON.stringify({
          enabled: twoFactorSettings.enabled,
        }),
      })
      setTwoFactorSettings(saved)
      setNotice('2FA settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : '2FA settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRefreshTwoFactorAccounts() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await fetchTwoFactorAccounts(token)
      setNotice('2FA account list refreshed.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : '2FA accounts refresh failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSetAccountTwoFactorRequired(accountId: string, required: boolean) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const updated = await requestWithAuth<TwoFactorAccount>(`/api/admin/settings/two-factor/accounts/${accountId}`, {
        method: 'PUT',
        body: JSON.stringify({
          twoFactorRequired: required,
        }),
      })
      setTwoFactorAccounts((prev) => prev.map((item) => (item.id === accountId ? updated : item)))
      setNotice(`2FA requirement ${required ? 'enabled' : 'disabled'} for ${updated.username}.`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : '2FA account update failed')
    } finally {
      setBusy(false)
    }
  }

  async function onResetAccountTwoFactor(accountId: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const updated = await requestWithAuth<TwoFactorAccount>(`/api/admin/settings/two-factor/accounts/${accountId}/reset`, {
        method: 'POST',
      })
      setTwoFactorAccounts((prev) => prev.map((item) => (item.id === accountId ? updated : item)))
      setNotice(`2FA reset for ${updated.username}. User must enroll again on next login.`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : '2FA reset failed')
    } finally {
      setBusy(false)
    }
  }

  async function onEnrollAccountTwoFactor(accountId: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<{
        account: TwoFactorAccount
        secret: string
        otpAuthUri: string
      }>(`/api/admin/settings/two-factor/accounts/${accountId}/enroll`, {
        method: 'POST',
      })
      setTwoFactorAccounts((prev) => prev.map((item) => (item.id === accountId ? result.account : item)))
      setNotice(`2FA enrollment key generated for ${result.account.username}. Secret: ${result.secret}`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : '2FA enroll failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveBrandingSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<BrandingSettings>('/api/admin/settings/branding', {
        method: 'PUT',
        body: JSON.stringify({
          productName: brandingSettings.productName.trim(),
          developerName: brandingSettings.developerName.trim(),
          tagline: brandingSettings.tagline.trim(),
          supportUrl: brandingSettings.supportUrl.trim(),
          primaryColor: brandingSettings.primaryColor.trim(),
          accentColor: brandingSettings.accentColor.trim(),
          logoText: brandingSettings.logoText.trim(),
          backgroundImageUrl: brandingSettings.backgroundImageUrl.trim(),
          backgroundOverlayOpacity: Math.min(0.95, Math.max(0, Number(brandingSettings.backgroundOverlayOpacity) || 0.55)),
          loginCardPosition: brandingSettings.loginCardPosition,
          loginCardWidth: Math.min(640, Math.max(340, Number(brandingSettings.loginCardWidth) || 460)),
        }),
      })
      setBrandingSettings(saved)
      setNotice('Branding settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Branding settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveInstallTelemetrySettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<InstallTelemetrySettings>('/api/admin/settings/install-telemetry', {
        method: 'PUT',
        body: JSON.stringify({
          enabled: installTelemetrySettings.enabled,
        }),
      })
      setInstallTelemetrySettings(saved)
      setNotice('Install telemetry settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Install telemetry settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onReloadProjectInstallStats() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const loaded = await requestWithAuth<ProjectInstallStat[]>('/api/admin/install-telemetry/projects?limit=200')
      setProjectInstallStats(loaded)
      setNotice(`Loaded ${loaded.length} tracked project installation records.`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Install telemetry list load failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveS3Settings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<S3Settings>('/api/admin/settings/s3', {
        method: 'PUT',
        body: JSON.stringify({
          useS3: s3Settings.useS3,
          localRootPath: s3Settings.localRootPath.trim(),
          endpoint: s3Settings.endpoint.trim(),
          bucket: s3Settings.bucket.trim(),
          accessKey: s3Settings.accessKey.trim(),
          secretKey: s3Settings.secretKey.trim(),
          forcePathStyle: s3Settings.forcePathStyle,
          useSsl: s3Settings.useSsl,
          autoCreateBucket: s3Settings.autoCreateBucket,
        }),
      })
      setS3Settings(saved)
      setStorageTestResult(null)
      setStorageMigrationResult(null)
      setNotice('Storage settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Storage settings save failed')
    } finally {
      setBusy(false)
    }
  }

  async function onTestStorageSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<StorageTestResult>('/api/admin/settings/s3/test', {
        method: 'POST',
      })
      setStorageTestResult(result)
      setNotice(result.message)
    } catch (caughtError) {
      setStorageTestResult(null)
      setError(caughtError instanceof Error ? caughtError.message : 'Storage test failed')
    } finally {
      setBusy(false)
    }
  }

  async function onRunStorageMigration() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const targetUseS3 = storageMigrationTargetMode === 's3'
      const result = await requestWithAuth<StorageMigrationResult>('/api/admin/settings/s3/migrate', {
        method: 'POST',
        body: JSON.stringify({
          targetUseS3,
          dryRun: storageMigrationDryRun,
          overwrite: storageMigrationOverwrite,
          maxObjects: Math.min(500000, Math.max(1, Number(storageMigrationMaxObjects) || 5000)),
          prefix: storageMigrationPrefix.trim(),
        }),
      })
      setStorageMigrationResult(result)
      setNotice(
        `${result.dryRun ? 'Dry-run' : 'Migration'} finished: scanned=${result.scanned}, copied=${result.copied}, skipped=${result.skipped}, failed=${result.failed}.`,
      )
    } catch (caughtError) {
      setStorageMigrationResult(null)
      setError(caughtError instanceof Error ? caughtError.message : 'Storage migration failed')
    } finally {
      setBusy(false)
    }
  }

  function toUtcOrUndefined(value: string): string | undefined {
    const normalized = value.trim()
    if (!normalized) {
      return undefined
    }

    const parsed = new Date(normalized)
    if (Number.isNaN(parsed.getTime())) {
      return undefined
    }

    return parsed.toISOString()
  }

  async function onProfileRebuild(profileId: string) {
    const argsErrors = [...rebuildJvmArgsAnalysis.errors, ...rebuildGameArgsAnalysis.errors]
    if (argsErrors.length > 0) {
      setError(`Args validation failed: ${argsErrors.join(' ')}`)
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const build = await requestWithAuth<BuildResponse>(`/api/admin/profiles/${profileId}/rebuild`, {
        method: 'POST',
        body: JSON.stringify({
          loaderType: rebuildLoaderType,
          mcVersion: rebuildMcVersion.trim(),
          jvmArgsDefault: rebuildJvmArgsDefault.trim(),
          gameArgsDefault: rebuildGameArgsDefault.trim(),
          sourceSubPath: rebuildSourceSubPath.trim(),
          javaRuntimePath: rebuildJavaRuntimePath.trim(),
          launchMode: rebuildLaunchMode,
          launchMainClass: rebuildLaunchMainClass.trim(),
          launchClasspath: rebuildLaunchClasspath,
          publishToServers: rebuildPublishToServers,
        }),
      })
      setNotice(`Rebuild completed: ${build.id} (${build.filesCount} files)`)
      await loadAdminData(token!)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Rebuild failed')
    } finally {
      setBusy(false)
    }
  }

  async function uploadIcon(category: 'profiles' | 'servers', file: File, entityId?: string): Promise<UploadResponse> {
    if (!token) {
      throw new Error('Missing admin token')
    }

    const formData = new FormData()
    formData.append('file', file)

    const query = new URLSearchParams({ category })
    if (entityId) {
      query.set('entityId', entityId)
    }

    const response = await fetch(`${apiBaseUrl}/api/admin/upload?${query.toString()}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    })

    if (!response.ok) {
      const payload = (await response.json()) as ApiError
      throw new Error(payload.error ?? 'Upload failed')
    }

    return (await response.json()) as UploadResponse
  }

  async function onUploadProfileIcon() {
    if (!profileIconFile) {
      setError('Choose a profile icon file first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const uploaded = await uploadIcon('profiles', profileIconFile, editingProfileId ?? undefined)
      setProfileForm((prev) => ({ ...prev, iconKey: uploaded.key }))
      setNotice(`Profile icon uploaded: ${uploaded.key}`)
      setProfileIconFile(null)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Profile icon upload failed')
    } finally {
      setBusy(false)
    }
  }

  async function onUploadServerIcon() {
    if (!serverIconFile) {
      setError('Choose a server icon file first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const uploaded = await uploadIcon('servers', serverIconFile, editingServerId ?? undefined)
      setServerForm((prev) => ({ ...prev, iconKey: uploaded.key }))
      setNotice(`Server icon uploaded: ${uploaded.key}`)
      setServerIconFile(null)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Server icon upload failed')
    } finally {
      setBusy(false)
    }
  }

  async function onUploadRuntime() {
    if (!runtimeFile) {
      setError('Choose a runtime archive/binary file first.')
      return
    }

    const profileSlug = runtimeProfileSlug.trim()
    if (!profileSlug) {
      setError('Select profile slug for runtime upload.')
      return
    }

    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const formData = new FormData()
      formData.append('file', runtimeFile)

      const query = new URLSearchParams({
        category: 'runtimes',
        entityId: profileSlug,
      })

      const response = await fetch(`${apiBaseUrl}/api/admin/upload?${query.toString()}`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
        },
        body: formData,
      })

      if (!response.ok) {
        const payload = (await response.json()) as ApiError
        throw new Error(payload.error ?? 'Runtime upload failed')
      }

      const uploaded = (await response.json()) as UploadResponse
      setRuntimeFile(null)
      setRuntimeVerifyKey(uploaded.key)
      setRuntimeVerifyResult(null)
      setRuntimeCleanupResult(null)
      const runtimeHashPreview = uploaded.runtimeSha256 ? uploaded.runtimeSha256.slice(0, 12) : ''
      setNotice(
        runtimeHashPreview
          ? `Runtime uploaded for ${profileSlug}: ${uploaded.key} (sha256 ${runtimeHashPreview}...)`
          : `Runtime uploaded for ${profileSlug}: ${uploaded.key}`
      )
      setProfileForm((prev) =>
        prev.slug.trim().toLowerCase() === profileSlug.toLowerCase()
          ? { ...prev, bundledRuntimeKey: uploaded.key }
          : prev
      )
      await loadAdminData(token)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime upload failed')
    } finally {
      setBusy(false)
    }
  }

  async function onVerifyRuntimeArtifact() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    const profileSlug = runtimeProfileSlug.trim()
    const overrideKey = runtimeVerifyKey.trim()
    if (!profileSlug && !overrideKey) {
      setError('Select profile slug or provide runtime key to verify.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const query = new URLSearchParams()
      if (profileSlug) {
        query.set('entityId', profileSlug)
      }

      if (overrideKey) {
        query.set('key', overrideKey)
      }

      const response = await fetch(`${apiBaseUrl}/api/admin/runtimes/verify?${query.toString()}`, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      })

      if (!response.ok) {
        const payload = (await response.json()) as ApiError
        throw new Error(payload.error ?? 'Runtime verify failed')
      }

      const verified = (await response.json()) as RuntimeVerifyResponse
      setRuntimeVerifyResult(verified)
      const matchSummary = verified.sha256MatchesProfile && verified.sizeMatchesProfile && verified.contentTypeMatchesProfile
        ? 'profile metadata matches storage'
        : 'profile metadata mismatch detected'
      setNotice(`Runtime verified: ${verified.key} (${matchSummary})`)
    } catch (caughtError) {
      setRuntimeVerifyResult(null)
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime verify failed')
    } finally {
      setBusy(false)
    }
  }

  async function onCleanupRuntimeArtifacts() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    const profileSlug = runtimeProfileSlug.trim()
    if (!profileSlug) {
      setError('Select profile slug for runtime cleanup.')
      return
    }

    const keepLast = Math.max(0, Math.min(100, Number(runtimeCleanupKeepLast) || 0))
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const query = new URLSearchParams({
        entityId: profileSlug,
        keepLast: String(keepLast),
        dryRun: String(runtimeCleanupDryRun),
      })

      const response = await fetch(`${apiBaseUrl}/api/admin/runtimes/cleanup?${query.toString()}`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
        },
      })

      if (!response.ok) {
        const payload = (await response.json()) as ApiError
        throw new Error(payload.error ?? 'Runtime cleanup failed')
      }

      const cleaned = (await response.json()) as RuntimeCleanupResponse
      setRuntimeCleanupResult(cleaned)
      const actionResult = runtimeCleanupDryRun
        ? `Dry-run: ${cleaned.deleteKeys.length} keys marked for delete`
        : `Deleted ${cleaned.deletedCount} old runtime keys`
      setNotice(`Runtime cleanup for ${cleaned.profileSlug}: ${actionResult}`)
      if (!runtimeCleanupDryRun) {
        await loadAdminData(token)
      }
    } catch (caughtError) {
      setRuntimeCleanupResult(null)
      setError(caughtError instanceof Error ? caughtError.message : 'Runtime cleanup failed')
    } finally {
      setBusy(false)
    }
  }

  async function uploadCosmetic(cosmeticType: 'skins' | 'capes', file: File): Promise<CosmeticUploadResponse> {
    if (!token) {
      throw new Error('Missing admin token')
    }

    const user = cosmeticsUser.trim()
    if (!user) {
      throw new Error('Enter username or externalId first.')
    }

    const formData = new FormData()
    formData.append('file', file)

    const response = await fetch(`${apiBaseUrl}/api/admin/${cosmeticType}/${encodeURIComponent(user)}/upload`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
      body: formData,
    })

    if (!response.ok) {
      const payload = (await response.json()) as ApiError
      throw new Error(payload.error ?? `${cosmeticType} upload failed`)
    }

    return (await response.json()) as CosmeticUploadResponse
  }

  async function onUploadSkin() {
    if (!skinFile) {
      setError('Choose skin file first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const uploaded = await uploadCosmetic('skins', skinFile)
      setSkinFile(null)
      setNotice(`Skin uploaded for ${uploaded.account}: ${uploaded.key}`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Skin upload failed')
    } finally {
      setBusy(false)
    }
  }

  async function onUploadCape() {
    if (!capeFile) {
      setError('Choose cape file first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const uploaded = await uploadCosmetic('capes', capeFile)
      setCapeFile(null)
      setNotice(`Cape uploaded for ${uploaded.account}: ${uploaded.key}`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Cape upload failed')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveDiscordRpcSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<DiscordRpcSettings>('/api/admin/settings/discord-rpc', {
        method: 'PUT',
        body: JSON.stringify({
          enabled: discordRpcSettings.enabled,
          privacyMode: discordRpcSettings.privacyMode,
        }),
      })
      setDiscordRpcSettings(saved)
      setNotice('Discord RPC module settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Failed to save Discord RPC module settings')
    } finally {
      setBusy(false)
    }
  }

  async function onLoadDiscordConfig() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    const scopeId = discordScopeId.trim()
    if (!scopeId) {
      setError('Select profile/server for Discord RPC config first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await fetch(`${apiBaseUrl}/api/admin/discord-rpc/${discordScopeType}/${scopeId}`, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      })

      if (response.status === 404) {
        setDiscordForm(defaultDiscordForm)
        setNotice('Discord RPC config is not set for selected scope.')
        return
      }

      if (!response.ok) {
        const payload = (await response.json()) as ApiError
        throw new Error(payload.error ?? 'Failed to load Discord RPC config')
      }

      const config = (await response.json()) as DiscordRpcConfig
      setDiscordForm({
        enabled: config.enabled,
        appId: config.appId,
        detailsText: config.detailsText,
        stateText: config.stateText,
        largeImageKey: config.largeImageKey,
        largeImageText: config.largeImageText,
        smallImageKey: config.smallImageKey,
        smallImageText: config.smallImageText,
      })
      setNotice('Discord RPC config loaded.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Failed to load Discord RPC config')
    } finally {
      setBusy(false)
    }
  }

  async function onSaveDiscordConfig() {
    const scopeId = discordScopeId.trim()
    if (!scopeId) {
      setError('Select profile/server for Discord RPC config first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<DiscordRpcConfig>(`/api/admin/discord-rpc/${discordScopeType}/${scopeId}`, {
        method: 'PUT',
        body: JSON.stringify(discordForm),
      })
      setNotice('Discord RPC config saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Failed to save Discord RPC config')
    } finally {
      setBusy(false)
    }
  }

  async function onDeleteDiscordConfig() {
    const scopeId = discordScopeId.trim()
    if (!scopeId) {
      setError('Select profile/server for Discord RPC config first.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await requestWithAuth<void>(`/api/admin/discord-rpc/${discordScopeType}/${scopeId}`, {
        method: 'DELETE',
      })
      setDiscordForm(defaultDiscordForm)
      setNotice('Discord RPC config deleted.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Failed to delete Discord RPC config')
    } finally {
      setBusy(false)
    }
  }

  async function onRefreshAuditLogs() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await fetchAuditLogs(token, 0, false)
      setNotice('Audit logs refreshed.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Audit logs refresh failed')
    } finally {
      setBusy(false)
    }
  }

  function onSetAuditRangeDays(days: number) {
    const now = new Date()
    const from = new Date(now.getTime() - days * 24 * 60 * 60 * 1000)
    setAuditLogFromLocal(toLocalDateTimeInputValue(from))
    setAuditLogToLocal(toLocalDateTimeInputValue(now))
    setAuditLogsOffset(0)
    setAuditLogsHasMore(false)
  }

  function onClearAuditFilters() {
    setAuditLogActionPrefix('')
    setAuditLogActor('')
    setAuditLogEntityType('')
    setAuditLogEntityId('')
    setAuditLogRequestId('')
    setAuditLogRemoteIp('')
    setAuditLogFromLocal('')
    setAuditLogToLocal('')
    setAuditLogSortOrder('desc')
    setAuditLogsOffset(0)
    setAuditLogsHasMore(false)
  }

  async function onExportAuditLogs(format: 'json' | 'csv') {
    if (!token) {
      setError('Missing admin token')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const query = buildAuditFilterQuery()
      query.set('limit', String(Math.min(50000, Math.max(1, Number(auditExportLimit) || 5000))))
      query.set('format', format)

      const response = await fetch(`${apiBaseUrl}/api/admin/audit-logs/export?${query.toString()}`, {
        headers: { Authorization: `Bearer ${token}` },
      })

      if (!response.ok) {
        const text = await response.text()
        let parsedError = ''
        if (text) {
          try {
            const payload = JSON.parse(text) as ApiError
            parsedError = payload.error ?? payload.title ?? ''
          } catch {
            parsedError = text
          }
        }

        throw new Error(parsedError || 'Audit export failed.')
      }

      const blob = await response.blob()
      const extension = format === 'csv' ? 'csv' : 'json'
      const filename = `admin-audit-${new Date().toISOString().replace(/[:.]/g, '-')}.${extension}`
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = filename
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
      setNotice(`Audit export ready (${format.toUpperCase()}).`)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Audit export failed')
    } finally {
      setBusy(false)
    }
  }

  function onSetAuditActionPreset(value: string) {
    setAuditLogActionPrefix(value)
    setAuditLogsOffset(0)
    setAuditLogsHasMore(false)
  }

  async function onLoadMoreAuditLogs() {
    if (!token) {
      setError('Missing admin token')
      return
    }

    if (!auditLogsHasMore) {
      setNotice('No more audit logs to load.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      await fetchAuditLogs(token, auditLogsOffset, true)
      setNotice('Loaded more audit logs.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Load more audit logs failed')
    } finally {
      setBusy(false)
    }
  }

  async function onAuditCleanup(dryRun: boolean) {
    if (!token) {
      setError('Missing admin token')
      return
    }

    const days = Math.max(1, Number(auditCleanupOlderThanDays) || 90)
    const cleanupLimit = Math.min(50000, Math.max(1, Number(auditCleanupLimit) || 5000))
    const olderThan = new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString()

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const response = await fetch(
        `${apiBaseUrl}/api/admin/audit-logs?olderThanUtc=${encodeURIComponent(olderThan)}&dryRun=${dryRun ? 'true' : 'false'}&limit=${cleanupLimit}`,
        {
          method: 'DELETE',
          headers: { Authorization: `Bearer ${token}` },
        },
      )

      if (!response.ok) {
        const text = await response.text()
        let parsedError = ''
        if (text) {
          try {
            const payload = JSON.parse(text) as ApiError
            parsedError = payload.error ?? payload.title ?? ''
          } catch {
            parsedError = text
          }
        }

        throw new Error(parsedError || 'Audit cleanup failed.')
      }

      const payload = (await response.json()) as AuditCleanupResponse
      if (dryRun) {
        setNotice(`Audit cleanup dry-run: eligible=${payload.totalEligible}, candidates=${payload.candidates}, hasMore=${String(payload.hasMore)}.`)
      } else {
        setNotice(`Audit cleanup applied: deleted=${payload.deleted}, hasMore=${String(payload.hasMore)}.`)
        await fetchAuditLogs(token, 0, false)
      }
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Audit cleanup failed')
    } finally {
      setBusy(false)
    }
  }

  function logout() {
    localStorage.removeItem('blp_admin_token')
    setToken(null)
    setActivePage('overview')
    setProfiles([])
    setServers([])
    setNewsItems([])
    setNewsSources([])
    setNewsSyncSettings(defaultNewsSyncSettings)
    setNewsRetentionSettings(defaultNewsRetentionSettings)
    setNewsRetentionDryRun(null)
    setRuntimeRetentionSettings(defaultRuntimeRetentionSettings)
    setRuntimeRetentionDryRun(null)
    setRuntimeRetentionDryRunProfileSlug('')
    setRuntimeRetentionDryRunMaxProfiles(20)
    setRuntimeRetentionDryRunKeysLimit(10)
    setBans([])
    setCrashes([])
    setCrashStatusFilter('new')
    setCrashProfileSlugFilter('')
    setCrashSearchFilter('')
    setCrashFromLocal('')
    setCrashToLocal('')
    setCrashLimit(50)
    setDocs([])
    setEditingDocId(null)
    setDocForm(defaultDocumentationForm)
    setDocSearchFilter('')
    setDocCategoryFilter('')
    setDocPublishedOnlyFilter(false)
    setAuditLogs([])
    setAuditLogsOffset(0)
    setAuditLogsHasMore(false)
    setAuditLogActionPrefix('')
    setAuditLogActor('')
    setAuditLogEntityType('')
    setAuditLogEntityId('')
    setAuditLogRequestId('')
    setAuditLogRemoteIp('')
    setAuditLogFromLocal('')
    setAuditLogToLocal('')
    setAuditLogLimit(50)
    setAuditLogSortOrder('desc')
    setAuditExportLimit(5000)
    setAuditCleanupOlderThanDays(90)
    setAuditCleanupLimit(5000)
    setEditingProfileId(null)
    setEditingServerId(null)
    setEditingNewsId(null)
    setProfileForm(defaultProfileForm)
    setServerForm(defaultServerForm)
    setNewsForm(defaultNewsForm)
    setNewsSourceForm(defaultNewsSourceForm)
    setHwidBanForm(defaultHwidBanForm)
    setAccountBanForm(defaultAccountBanForm)
    setAuthProviderSettings(defaultAuthProviderSettings)
    setAuthProbeResult(null)
    setWizardPreflightChecks([])
    setWizardPreflightHistory([])
    setTwoFactorSettings(defaultTwoFactorSettings)
    setTwoFactorAccounts([])
    setTwoFactorSearch('')
    setTwoFactorRequiredOnly(false)
    setTwoFactorLimit(100)
    setBrandingSettings(defaultBrandingSettings)
    setS3Settings(defaultS3Settings)
    setStorageTestResult(null)
    setStorageMigrationTargetMode('local')
    setStorageMigrationDryRun(true)
    setStorageMigrationOverwrite(true)
    setStorageMigrationMaxObjects(5000)
    setStorageMigrationPrefix('')
    setStorageMigrationResult(null)
    setProfileIconFile(null)
    setServerIconFile(null)
    setCosmeticsUser('')
    setSkinFile(null)
    setCapeFile(null)
    setDiscordScopeType('profile')
    setDiscordScopeId('')
    setDiscordForm(defaultDiscordForm)
    setRebuildLoaderType('vanilla')
    setRebuildMcVersion('1.21.1')
    setRebuildJvmArgsDefault('')
    setRebuildGameArgsDefault('')
    setRebuildSourceSubPath('')
    setRebuildJavaRuntimePath('')
    setRebuildLaunchMode('auto')
    setRebuildLaunchMainClass('')
    setRebuildLaunchClasspath('')
    setRebuildPublishToServers(true)
    setNotice('')
    setPhase('login')
  }

  if (phase === 'loading') {
    return <main className="shell">Loading...</main>
  }

  const activePageMeta = dashboardPages.find((page) => page.id === activePage) ?? dashboardPages[0]

  return (
    <main className="shell">
      <section className={`panel ${phase === 'dashboard' ? 'panel-dashboard' : 'panel-auth'}`}>
        <div className="panel-topbar">
          <div>
            <h1>BivLauncher Admin</h1>
            <p className="muted">API: {apiBaseUrl}</p>
          </div>
          {phase === 'dashboard' && <span className="status-pill">{busy ? 'Syncing...' : 'Live'}</span>}
        </div>

        {phase !== 'dashboard' && (
          <section className="auth-layout">
            <section className="auth-hero">
              <p className="auth-kicker">BivLauncher Control Plane</p>
              <h2>One panel for launcher, backend and content ops</h2>
              <p className="muted">
                Manage profiles, build/release, storage mode, crash analytics and security from one workspace.
              </p>
              <div className="auth-chip-grid">
                <span>Setup Wizard</span>
                <span>Storage migration</span>
                <span>Crash center</span>
                <span>2FA policy</span>
              </div>
            </section>

            <form className="form auth-form" onSubmit={phase === 'setup' ? onSetupSubmit : onLoginSubmit}>
              <h2>{phase === 'setup' ? 'First run setup' : 'Admin login'}</h2>
              <p className="muted">
                {phase === 'setup'
                  ? 'Create root administrator credentials for this project.'
                  : 'Sign in with your administrator account to open dashboard.'}
              </p>
              <label>
                Username
                <input value={username} onChange={(event) => setUsername(event.target.value)} required minLength={3} />
              </label>
              <label>
                Password
                <input
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  type="password"
                  required
                  minLength={8}
                />
              </label>
              <button disabled={busy}>
                {phase === 'setup'
                  ? (busy ? 'Creating...' : 'Create admin')
                  : (busy ? 'Signing in...' : 'Sign in')}
              </button>
              <small className="muted auth-hint">Browser origin: {currentOrigin}</small>
            </form>
          </section>
        )}

        {phase === 'dashboard' && (
          <section className="dashboard">
            <aside className="dashboard-sidebar">
              <p className="sidebar-caption">Workspace</p>
              <nav className="dashboard-nav">
                {dashboardPages.map((page) => (
                  <button
                    key={page.id}
                    type="button"
                    className={`nav-item ${activePage === page.id ? 'active' : ''}`}
                    onClick={() => setActivePage(page.id)}
                  >
                    <span>{page.title}</span>
                    <small>{page.subtitle}</small>
                  </button>
                ))}
              </nav>
              <button type="button" className="logout-btn" onClick={logout}>
                Logout
              </button>
            </aside>

            <div className="dashboard-main">
              <div className="dashboard-header">
                <div>
                  <h2>{activePageMeta.title}</h2>
                  <p className="muted">{activePageMeta.subtitle}</p>
                </div>
                <div className="quick-stats">
                  <div className="stat-card">
                    <span>Profiles</span>
                    <strong>{profiles.length}</strong>
                  </div>
                  <div className="stat-card">
                    <span>Servers</span>
                    <strong>{servers.length}</strong>
                  </div>
                  <div className="stat-card">
                    <span>News</span>
                    <strong>{newsItems.length}</strong>
                  </div>
                  <div className="stat-card">
                    <span>Bans</span>
                    <strong>{bans.length}</strong>
                  </div>
                </div>
              </div>

            <section className={`overview-hero ${activePage === 'overview' ? '' : 'is-hidden'}`}>
              <h3>Control Center</h3>
              <p>
                Use left navigation to manage each area separately: profiles/servers, build pipeline,
                integrations, content, security and audit.
              </p>
              <div className="overview-tags">
                <span>Stable API auth</span>
                <span>Per-profile runtime</span>
                <span>Live sync controls</span>
                <span>Audit export + cleanup</span>
              </div>
              <div className="button-row" style={{ marginTop: '0.75rem' }}>
                <button type="button" onClick={() => setActivePage('wizard')}>
                  Open setup wizard
                </button>
              </div>
            </section>

            <section className={`wizard-board ${activePage === 'wizard' ? '' : 'is-hidden'}`}>
              <div className="wizard-head">
                <div>
                  <h3>Setup Wizard</h3>
                  <p className="muted">
                    Complete required steps before production launch. Optional steps improve UX/support readiness.
                  </p>
                </div>
                <div className="wizard-summary">
                  <strong>{wizardRequiredPercent}%</strong>
                  <small>required ready</small>
                </div>
              </div>

              <div className="wizard-progress-line">
                <div style={{ width: `${wizardRequiredPercent}%` }} />
              </div>

              <div className="overview-tags">
                <span>Required: {wizardRequiredDone}/{wizardRequiredTotal}</span>
                <span>Optional done: {wizardOptionalDone}</span>
                <span>Total checks: {wizardSteps.length}</span>
              </div>

              <section className="wizard-quick">
                <h4>Quick Start: profile + first server</h4>
                <small className="muted">
                  Creates one enabled profile and one enabled server in a single action.
                </small>
                <div className="wizard-quick-grid">
                  <input
                    placeholder="Project / profile name"
                    value={wizardProjectName}
                    onChange={(event) => setWizardProjectName(event.target.value)}
                  />
                  <input
                    placeholder="Profile slug (optional, auto-generated)"
                    value={wizardProfileSlug}
                    onChange={(event) => setWizardProfileSlug(event.target.value)}
                  />
                </div>
                <small className={`muted ${wizardHasSlugConflict ? 'warning-note' : ''}`}>
                  Effective slug: {wizardNormalizedSlug || '(empty)'}
                  {wizardHasSlugConflict ? ' (already exists)' : ''}
                </small>
                <div className="wizard-quick-grid">
                  <input
                    placeholder="Server name (optional)"
                    value={wizardServerName}
                    onChange={(event) => setWizardServerName(event.target.value)}
                  />
                  <input
                    placeholder="Server address"
                    value={wizardServerAddress}
                    onChange={(event) => setWizardServerAddress(event.target.value)}
                  />
                </div>
                <div className="wizard-quick-grid">
                  <input
                    type="number"
                    min={1}
                    max={65535}
                    placeholder="Server port"
                    value={wizardServerPort}
                    onChange={(event) => setWizardServerPort(Math.min(65535, Math.max(1, Number(event.target.value) || 25565)))}
                  />
                  <select value={wizardLoaderType} onChange={(event) => setWizardLoaderType(event.target.value)}>
                    {supportedLoaders.map((loader) => (
                      <option key={loader} value={loader}>
                        {loader}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="wizard-quick-grid">
                  <input
                    placeholder="MC version"
                    value={wizardMcVersion}
                    onChange={(event) => setWizardMcVersion(event.target.value)}
                  />
                  <button type="button" onClick={onWizardQuickCreate} disabled={busy || !token || !wizardProjectName.trim()}>
                    Create starter topology
                  </button>
                </div>
              </section>

              <section className="wizard-preflight">
                <div className="wizard-preflight-head">
                  <h4>Pre-flight checks</h4>
                  <button type="button" onClick={onRunWizardPreflight} disabled={busy || !token}>
                    Run pre-flight
                  </button>
                </div>
                <small className="muted">
                  Runs API health, storage round-trip and auth-provider probe. Use before first public release.
                </small>
                <ul className="wizard-preflight-list">
                  {wizardPreflightChecks.map((check) => (
                    <li key={check.id} className={check.status}>
                      <span className={`wizard-state ${check.status === 'passed' ? 'done' : 'pending'}`}>
                        {check.status === 'passed' ? 'Passed' : check.status === 'failed' ? 'Failed' : 'Skipped'}
                      </span>
                      <span className="list-text">
                        {check.label}
                        <small>{check.message}</small>
                      </span>
                    </li>
                  ))}
                  {wizardPreflightChecks.length === 0 && (
                    <li className="empty">
                      <span className="list-text">
                        No checks yet
                        <small>Run pre-flight to collect live readiness status.</small>
                      </span>
                    </li>
                  )}
                </ul>
                {wizardPreflightHistory.length > 0 && (
                  <div className="wizard-preflight-history">
                    <div className="wizard-preflight-history-head">
                      <strong>Recent runs</strong>
                      <button type="button" onClick={onClearWizardPreflightHistory} disabled={busy}>
                        Clear history
                      </button>
                    </div>
                    <ul className="wizard-preflight-history-list">
                      {wizardPreflightHistory.map((run) => {
                        const runFailed = run.totalCount - run.passedCount
                        return (
                          <li key={run.id} className={runFailed > 0 ? 'failed' : 'passed'}>
                            <span className={`wizard-state ${runFailed > 0 ? 'pending' : 'done'}`}>
                              {run.passedCount}/{run.totalCount}
                            </span>
                            <span className="list-text">
                              {new Date(run.ranAtUtc).toLocaleString()}
                              <small>{runFailed > 0 ? `${runFailed} failed` : 'All checks passed'}</small>
                              <small>Actor: {run.actor}</small>
                            </span>
                            <button type="button" onClick={() => onUseWizardPreflightRun(run.id)}>
                              Load
                            </button>
                          </li>
                        )
                      })}
                    </ul>
                  </div>
                )}
              </section>

              <ul className="wizard-steps">
                {wizardSteps.map((step) => (
                  <li key={step.id} className={step.done ? 'done' : 'pending'}>
                    <div className={`wizard-state ${step.done ? 'done' : 'pending'}`}>
                      {step.done ? 'Done' : 'Pending'}
                    </div>
                    <div className="wizard-step-text">
                      <strong>
                        {step.title} {step.required ? '(required)' : '(optional)'}
                      </strong>
                      <small className="muted">{step.description}</small>
                      <small className="muted">{step.hint}</small>
                    </div>
                    <button type="button" onClick={() => setActivePage(step.page)}>
                      Open
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <div className={`grid-2 ${activePage === 'servers' ? '' : 'is-hidden'}`}>
              <form className="form form-small" onSubmit={onProfileSubmit}>
                <h3>{editingProfileId ? 'Edit profile' : 'Create profile'}</h3>
                <input
                  placeholder="Name"
                  value={profileForm.name}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, name: event.target.value }))}
                  required
                />
                <input
                  placeholder="Slug (example: main-survival)"
                  value={profileForm.slug}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, slug: event.target.value }))}
                  required
                />
                <input
                  placeholder="Description"
                  value={profileForm.description}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, description: event.target.value }))}
                />
                <input
                  placeholder="Icon key (S3 key)"
                  value={profileForm.iconKey}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, iconKey: event.target.value }))}
                />
                <div className="upload-row">
                  <input
                    type="file"
                    accept=".png,.jpg,.jpeg,.webp,.gif,.svg"
                    onChange={(event) => setProfileIconFile(event.target.files?.[0] ?? null)}
                  />
                  <button type="button" onClick={onUploadProfileIcon} disabled={busy || !token}>
                    Upload icon
                  </button>
                </div>
                <div className="grid-inline">
                  <input
                    type="number"
                    placeholder="Priority"
                    value={profileForm.priority}
                    onChange={(event) =>
                      setProfileForm((prev) => ({ ...prev, priority: Number(event.target.value) || 0 }))
                    }
                  />
                  <input
                    type="number"
                    placeholder="RAM MB"
                    value={profileForm.recommendedRamMb}
                    onChange={(event) =>
                      setProfileForm((prev) => ({ ...prev, recommendedRamMb: Number(event.target.value) || 1024 }))
                    }
                  />
                </div>
                <textarea
                  placeholder="Default JVM args for profile"
                  rows={3}
                  value={profileForm.jvmArgsDefault}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, jvmArgsDefault: event.target.value }))}
                />
                {profileJvmArgsAnalysis.warnings.length > 0 && (
                  <small className="warning-note">
                    JVM warnings: {profileJvmArgsAnalysis.warnings.join(' | ')}
                  </small>
                )}
                <textarea
                  placeholder="Default Game args for profile"
                  rows={3}
                  value={profileForm.gameArgsDefault}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, gameArgsDefault: event.target.value }))}
                />
                {profileGameArgsAnalysis.warnings.length > 0 && (
                  <small className="warning-note">
                    Game warnings: {profileGameArgsAnalysis.warnings.join(' | ')}
                  </small>
                )}
                <input
                  placeholder="Bundled Java path (relative, optional)"
                  value={profileForm.bundledJavaPath}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, bundledJavaPath: event.target.value }))}
                />
                <input
                  placeholder="Bundled runtime artifact key (S3 key, optional)"
                  value={profileForm.bundledRuntimeKey}
                  onChange={(event) => setProfileForm((prev) => ({ ...prev, bundledRuntimeKey: event.target.value }))}
                />
                {runtimeMetadataProfile ? (
                  <small>
                    Runtime metadata: {runtimeMetadataProfile.bundledRuntimeSizeBytes > 0 ? formatBytes(runtimeMetadataProfile.bundledRuntimeSizeBytes) : 'size n/a'}
                    {runtimeMetadataProfile.bundledRuntimeContentType ? ` | ${runtimeMetadataProfile.bundledRuntimeContentType}` : ''}
                    {runtimeMetadataProfile.bundledRuntimeSha256 ? ` | sha256 ${runtimeMetadataProfile.bundledRuntimeSha256}` : ''}
                  </small>
                ) : null}
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={profileForm.enabled}
                    onChange={(event) => setProfileForm((prev) => ({ ...prev, enabled: event.target.checked }))}
                  />
                  Enabled
                </label>
                <button disabled={busy || !token}>{editingProfileId ? 'Update profile' : 'Create profile'}</button>
              </form>

              <form className="form form-small" onSubmit={onServerSubmit}>
                <h3>{editingServerId ? 'Edit server' : 'Create server'}</h3>
                <select
                  value={serverForm.profileId}
                  onChange={(event) => setServerForm((prev) => ({ ...prev, profileId: event.target.value }))}
                  required
                >
                  <option value="">Select profile</option>
                  {profiles.map((profile) => (
                    <option key={profile.id} value={profile.id}>
                      {profile.name}
                    </option>
                  ))}
                </select>
                <input
                  placeholder="Name"
                  value={serverForm.name}
                  onChange={(event) => setServerForm((prev) => ({ ...prev, name: event.target.value }))}
                  required
                />
                <div className="grid-inline">
                  <input
                    placeholder="Address"
                    value={serverForm.address}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, address: event.target.value }))}
                    required
                  />
                  <input
                    type="number"
                    placeholder="Port"
                    value={serverForm.port}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, port: Number(event.target.value) || 25565 }))}
                    required
                  />
                </div>
                <div className="grid-inline">
                  <input
                    placeholder="Main jar path (DE route)"
                    value={serverForm.mainJarPath}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, mainJarPath: event.target.value }))}
                  />
                  <input
                    placeholder="RU proxy address"
                    value={serverForm.ruProxyAddress}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, ruProxyAddress: event.target.value }))}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    type="number"
                    placeholder="RU proxy port"
                    value={serverForm.ruProxyPort}
                    onChange={(event) =>
                      setServerForm((prev) => ({ ...prev, ruProxyPort: Number(event.target.value) || 25565 }))
                    }
                    required
                  />
                  <input
                    placeholder="RU jar path (proxy route)"
                    value={serverForm.ruJarPath}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, ruJarPath: event.target.value }))}
                  />
                </div>
                <div className="grid-inline">
                  <select
                    value={serverForm.loaderType}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, loaderType: event.target.value }))}
                    required
                  >
                    {supportedLoaders.map((loader) => (
                      <option key={loader} value={loader}>
                        {loader}
                      </option>
                    ))}
                  </select>
                  <input
                    placeholder="MC Version"
                    value={serverForm.mcVersion}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, mcVersion: event.target.value }))}
                    required
                  />
                </div>
                <input
                  placeholder="Icon key (S3 key)"
                  value={serverForm.iconKey}
                  onChange={(event) => setServerForm((prev) => ({ ...prev, iconKey: event.target.value }))}
                />
                <div className="upload-row">
                  <input
                    type="file"
                    accept=".png,.jpg,.jpeg,.webp,.gif,.svg"
                    onChange={(event) => setServerIconFile(event.target.files?.[0] ?? null)}
                  />
                  <button type="button" onClick={onUploadServerIcon} disabled={busy || !token}>
                    Upload icon
                  </button>
                </div>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={serverForm.enabled}
                    onChange={(event) => setServerForm((prev) => ({ ...prev, enabled: event.target.checked }))}
                  />
                  Enabled
                </label>
                <button disabled={busy || !token || profiles.length === 0}>
                  {editingServerId ? 'Update server' : 'Create server'}
                </button>
              </form>
            </div>

            <div className={`grid-2 split-build-servers ${(activePage === 'build' || activePage === 'servers') ? '' : 'is-hidden'} ${activePage === 'build' ? 'show-build' : 'show-servers'}`}>
              <section className="form form-small">
                <h3>Skins / Capes</h3>
                <input
                  placeholder="Username or ExternalId"
                  value={cosmeticsUser}
                  onChange={(event) => setCosmeticsUser(event.target.value)}
                />
                <div className="upload-row">
                  <input
                    type="file"
                    accept=".png,.jpg,.jpeg,.webp,.gif,.svg"
                    onChange={(event) => setSkinFile(event.target.files?.[0] ?? null)}
                  />
                  <button type="button" onClick={onUploadSkin} disabled={busy || !token}>
                    Upload skin
                  </button>
                </div>
                <div className="upload-row">
                  <input
                    type="file"
                    accept=".png,.jpg,.jpeg,.webp,.gif,.svg"
                    onChange={(event) => setCapeFile(event.target.files?.[0] ?? null)}
                  />
                  <button type="button" onClick={onUploadCape} disabled={busy || !token}>
                    Upload cape
                  </button>
                </div>

                <h4>Java Runtime Artifact</h4>
                <select
                  value={runtimeProfileSlug}
                  onChange={(event) => setRuntimeProfileSlug(event.target.value)}
                  disabled={profiles.length === 0}
                >
                  <option value="">Select profile slug</option>
                  {profiles.map((profile) => (
                    <option key={profile.id} value={profile.slug}>
                      {profile.name} ({profile.slug})
                    </option>
                  ))}
                </select>
                <div className="upload-row">
                  <input
                    type="file"
                    accept=".zip,.tar,.gz,.7z,.exe,.msi,.bin"
                    onChange={(event) => setRuntimeFile(event.target.files?.[0] ?? null)}
                  />
                  <button type="button" onClick={onUploadRuntime} disabled={busy || !token || profiles.length === 0}>
                    Upload runtime
                  </button>
                </div>
                <input
                  placeholder="Runtime key override for verify (optional)"
                  value={runtimeVerifyKey}
                  onChange={(event) => setRuntimeVerifyKey(event.target.value)}
                />
                <button type="button" onClick={onVerifyRuntimeArtifact} disabled={busy || !token}>
                  Verify runtime artifact
                </button>
                {runtimeVerifyResult ? (
                  <small>
                    Storage: {formatBytes(runtimeVerifyResult.storageSizeBytes)} | {runtimeVerifyResult.storageContentType || 'application/octet-stream'}
                    {runtimeVerifyResult.storageSha256 ? ` | sha256 ${runtimeVerifyResult.storageSha256.slice(0, 12)}...` : ''}
                    {' | matches:'} hash={String(runtimeVerifyResult.sha256MatchesProfile)}, size={String(runtimeVerifyResult.sizeMatchesProfile)}, type={String(runtimeVerifyResult.contentTypeMatchesProfile)}
                  </small>
                ) : null}
                <div className="grid-inline">
                  <input
                    type="number"
                    min={0}
                    max={100}
                    placeholder="Keep last N"
                    value={runtimeCleanupKeepLast}
                    onChange={(event) => setRuntimeCleanupKeepLast(Number(event.target.value) || 0)}
                  />
                  <label className="checkbox">
                    <input
                      type="checkbox"
                      checked={runtimeCleanupDryRun}
                      onChange={(event) => setRuntimeCleanupDryRun(event.target.checked)}
                    />
                    Dry run
                  </label>
                </div>
                <button type="button" onClick={onCleanupRuntimeArtifacts} disabled={busy || !token || profiles.length === 0}>
                  Cleanup old runtimes
                </button>
                {runtimeCleanupResult ? (
                  <small>
                    Cleanup: found {runtimeCleanupResult.totalFound}, keep {runtimeCleanupResult.keepKeys.length}, delete {runtimeCleanupResult.deleteKeys.length}
                    {!runtimeCleanupResult.dryRun ? `, deleted ${runtimeCleanupResult.deletedCount}` : ''}
                  </small>
                ) : null}

                <h4>Runtime Retention Schedule</h4>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={runtimeRetentionSettings.enabled}
                    onChange={(event) =>
                      setRuntimeRetentionSettings((prev) => ({
                        ...prev,
                        enabled: event.target.checked,
                      }))
                    }
                  />
                  Enable background runtime cleanup
                </label>
                <div className="grid-inline">
                  <input
                    type="number"
                    min={5}
                    max={10080}
                    placeholder="Interval minutes"
                    value={runtimeRetentionSettings.intervalMinutes}
                    onChange={(event) =>
                      setRuntimeRetentionSettings((prev) => ({
                        ...prev,
                        intervalMinutes: Math.min(10080, Math.max(5, Number(event.target.value) || 360)),
                      }))
                    }
                  />
                  <input
                    type="number"
                    min={0}
                    max={100}
                    placeholder="Keep last N"
                    value={runtimeRetentionSettings.keepLast}
                    onChange={(event) =>
                      setRuntimeRetentionSettings((prev) => ({
                        ...prev,
                        keepLast: Math.min(100, Math.max(0, Number(event.target.value) || 0)),
                      }))
                    }
                  />
                </div>
                <div className="button-row">
                  <button type="button" onClick={onSaveRuntimeRetentionSettings} disabled={busy || !token}>
                    Save runtime retention
                  </button>
                  <button type="button" onClick={onDryRunRuntimeRetentionNow} disabled={busy || !token}>
                    Dry-run
                  </button>
                  <button type="button" onClick={onRunRuntimeRetentionNow} disabled={busy || !token}>
                    Run retention now
                  </button>
                </div>
                <div className="grid-inline">
                  <input
                    placeholder="Dry-run profile slug (optional)"
                    value={runtimeRetentionDryRunProfileSlug}
                    onChange={(event) => setRuntimeRetentionDryRunProfileSlug(event.target.value)}
                  />
                  <input
                    type="number"
                    min={1}
                    max={200}
                    placeholder="Dry-run max profiles"
                    value={runtimeRetentionDryRunMaxProfiles}
                    onChange={(event) => setRuntimeRetentionDryRunMaxProfiles(Number(event.target.value) || 20)}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    type="number"
                    min={1}
                    max={100}
                    placeholder="Delete keys preview limit"
                    value={runtimeRetentionDryRunKeysLimit}
                    onChange={(event) => setRuntimeRetentionDryRunKeysLimit(Number(event.target.value) || 10)}
                  />
                  <button type="button" onClick={onExportRuntimeRetentionDryRun} disabled={busy || !token || !runtimeRetentionDryRun}>
                    Export dry-run JSON
                  </button>
                </div>
                <div className="button-row">
                  <button type="button" onClick={onApplyRuntimeRetentionFromDryRun} disabled={busy || !token || !runtimeRetentionDryRun}>
                    Apply from dry-run
                  </button>
                  <button type="button" onClick={onCopyRuntimeRetentionDeleteKeys} disabled={busy || !token || !runtimeRetentionDryRun}>
                    Copy delete keys
                  </button>
                </div>
                <small>
                  Last run: {runtimeRetentionSettings.lastRunAtUtc ? new Date(runtimeRetentionSettings.lastRunAtUtc).toLocaleString() : 'never'}
                  {' | '}profiles: {runtimeRetentionSettings.lastProfilesProcessed}
                  {' | '}deleted: {runtimeRetentionSettings.lastDeletedItems}
                  {' | '}error: {runtimeRetentionSettings.lastRunError || 'none'}
                </small>
                {runtimeRetentionDryRun ? (
                  <ul className="list">
                    <li>
                      <span className="list-text">
                        Dry-run summary
                        <small>
                          delete {runtimeRetentionDryRun.totalDeleteCandidates} | profiles {runtimeRetentionDryRun.profilesWithDeletions}/{runtimeRetentionDryRun.profilesScanned}
                        </small>
                        <small>
                          returned {runtimeRetentionDryRun.profilesReturned}/{runtimeRetentionDryRun.profilesWithDeletions}
                          {runtimeRetentionDryRun.hasMoreProfiles ? ' | more profiles hidden' : ''}
                        </small>
                        <small>{new Date(runtimeRetentionDryRun.calculatedAtUtc).toLocaleString()}</small>
                      </span>
                    </li>
                    {runtimeRetentionDryRun.profiles.map((profile) => (
                      <li key={profile.profileSlug}>
                        <span className="list-text">
                          {profile.profileSlug}
                          <small>total: {profile.totalRuntimeObjects} | keep: {profile.keepCount} | delete: {profile.deleteCount}</small>
                          <small>
                            {profile.deleteKeysPreview.length > 0 ? profile.deleteKeysPreview.join(', ') : 'no keys'}
                            {profile.hasMoreDeleteKeys ? ' ...' : ''}
                          </small>
                        </span>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </section>

              <section>
                <h3>Servers</h3>
                <ul className="list">
                  {servers.map((server) => (
                    <li key={server.id}>
                      <span className="list-text">
                        {server.name} ({server.address}:{server.port})
                        <small>
                          RU: {server.ruProxyAddress ? `${server.ruProxyAddress}:${server.ruProxyPort}` : 'not configured'}
                        </small>
                        <small>Jars: main={server.mainJarPath || 'auto'}, ru={server.ruJarPath || 'auto'}</small>
                        <small>{server.iconKey || 'no icon'}</small>
                      </span>
                      <div>
                        <button onClick={() => onServerEdit(server)}>Edit</button>
                        <button onClick={() => onServerDelete(server.id)}>Delete</button>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <div className={`grid-2 split-integrations-profiles ${(activePage === 'integrations' || activePage === 'servers') ? '' : 'is-hidden'} ${activePage === 'integrations' ? 'show-integrations' : 'show-servers'}`}>
              <section className="form form-small">
                <h3>Discord RPC</h3>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={discordRpcSettings.enabled}
                    onChange={(event) =>
                      setDiscordRpcSettings((prev) => ({ ...prev, enabled: event.target.checked }))
                    }
                  />
                  Module enabled globally
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={discordRpcSettings.privacyMode}
                    onChange={(event) =>
                      setDiscordRpcSettings((prev) => ({ ...prev, privacyMode: event.target.checked }))
                    }
                  />
                  Privacy mode (hide profile/server details)
                </label>
                <div className="button-row">
                  <button type="button" onClick={onSaveDiscordRpcSettings} disabled={busy || !token}>
                    Save module settings
                  </button>
                </div>
                <small>
                  Updated: {discordRpcSettings.updatedAtUtc ? new Date(discordRpcSettings.updatedAtUtc).toLocaleString() : 'fallback/default'}
                </small>
                <div className="grid-inline">
                  <select value={discordScopeType} onChange={(event) => setDiscordScopeType(event.target.value as 'profile' | 'server')}>
                    <option value="profile">Profile</option>
                    <option value="server">Server</option>
                  </select>
                  <select
                    value={discordScopeId}
                    onChange={(event) => setDiscordScopeId(event.target.value)}
                  >
                    <option value="">Select scope</option>
                    {discordScopeType === 'profile'
                      ? profiles.map((profile) => (
                          <option key={profile.id} value={profile.id}>
                            {profile.name}
                          </option>
                        ))
                      : servers.map((server) => (
                          <option key={server.id} value={server.id}>
                            {server.name}
                          </option>
                        ))}
                  </select>
                </div>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={discordForm.enabled}
                    onChange={(event) => setDiscordForm((prev) => ({ ...prev, enabled: event.target.checked }))}
                  />
                  Enabled
                </label>
                <input
                  placeholder="App ID"
                  value={discordForm.appId}
                  onChange={(event) => setDiscordForm((prev) => ({ ...prev, appId: event.target.value }))}
                />
                <input
                  placeholder="Details text"
                  value={discordForm.detailsText}
                  onChange={(event) => setDiscordForm((prev) => ({ ...prev, detailsText: event.target.value }))}
                />
                <input
                  placeholder="State text"
                  value={discordForm.stateText}
                  onChange={(event) => setDiscordForm((prev) => ({ ...prev, stateText: event.target.value }))}
                />
                <div className="grid-inline">
                  <input
                    placeholder="Large image key"
                    value={discordForm.largeImageKey}
                    onChange={(event) => setDiscordForm((prev) => ({ ...prev, largeImageKey: event.target.value }))}
                  />
                  <input
                    placeholder="Large image text"
                    value={discordForm.largeImageText}
                    onChange={(event) => setDiscordForm((prev) => ({ ...prev, largeImageText: event.target.value }))}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    placeholder="Small image key"
                    value={discordForm.smallImageKey}
                    onChange={(event) => setDiscordForm((prev) => ({ ...prev, smallImageKey: event.target.value }))}
                  />
                  <input
                    placeholder="Small image text"
                    value={discordForm.smallImageText}
                    onChange={(event) => setDiscordForm((prev) => ({ ...prev, smallImageText: event.target.value }))}
                  />
                </div>
                <div className="button-row">
                  <button type="button" onClick={onLoadDiscordConfig} disabled={busy || !token}>
                    Load
                  </button>
                  <button type="button" onClick={onSaveDiscordConfig} disabled={busy || !token}>
                    Save
                  </button>
                  <button type="button" onClick={onDeleteDiscordConfig} disabled={busy || !token}>
                    Delete
                  </button>
                </div>
              </section>

              <section>
                <h3>Profiles</h3>
                <div className="form form-small">
                  <h4>Rebuild options</h4>
                  <div className="grid-inline">
                    <select value={rebuildLoaderType} onChange={(event) => setRebuildLoaderType(event.target.value)}>
                      {supportedLoaders.map((loader) => (
                        <option key={loader} value={loader}>
                          {loader}
                        </option>
                      ))}
                    </select>
                    <input
                      placeholder="MC version (for loader path)"
                      value={rebuildMcVersion}
                      onChange={(event) => setRebuildMcVersion(event.target.value)}
                    />
                  </div>
                  <div className="grid-inline">
                    <select
                      value=""
                      onChange={(event) => {
                        const preset = jvmArgPresets.find((x) => x.id === event.target.value)
                        if (preset) {
                          setRebuildJvmArgsDefault(preset.value)
                        }
                      }}
                    >
                      <option value="">JVM preset...</option>
                      {jvmArgPresets.map((preset) => (
                        <option key={preset.id} value={preset.id}>
                          {preset.label}
                        </option>
                      ))}
                    </select>
                    <select
                      value=""
                      onChange={(event) => {
                        const preset = gameArgPresets.find((x) => x.id === event.target.value)
                        if (preset) {
                          setRebuildGameArgsDefault(preset.value)
                        }
                      }}
                    >
                      <option value="">Game preset...</option>
                      {gameArgPresets.map((preset) => (
                        <option key={preset.id} value={preset.id}>
                          {preset.label}
                        </option>
                      ))}
                    </select>
                  </div>
                  <textarea
                    placeholder="JVM args override for rebuild (optional)"
                    rows={3}
                    value={rebuildJvmArgsDefault}
                    onChange={(event) => setRebuildJvmArgsDefault(event.target.value)}
                  />
                  {rebuildJvmArgsAnalysis.warnings.length > 0 && (
                    <small className="warning-note">
                      JVM warnings: {rebuildJvmArgsAnalysis.warnings.join(' | ')}
                    </small>
                  )}
                  <textarea
                    placeholder="Game args override for rebuild (optional)"
                    rows={3}
                    value={rebuildGameArgsDefault}
                    onChange={(event) => setRebuildGameArgsDefault(event.target.value)}
                  />
                  {rebuildGameArgsAnalysis.warnings.length > 0 && (
                    <small className="warning-note">
                      Game warnings: {rebuildGameArgsAnalysis.warnings.join(' | ')}
                    </small>
                  )}
                  <input
                    placeholder="Source sub-path override (optional)"
                    value={rebuildSourceSubPath}
                    onChange={(event) => setRebuildSourceSubPath(event.target.value)}
                  />
                  <input
                    placeholder="Java runtime path override (optional)"
                    value={rebuildJavaRuntimePath}
                    onChange={(event) => setRebuildJavaRuntimePath(event.target.value)}
                  />
                  <div className="grid-inline">
                    <select
                      value={rebuildLaunchMode}
                      onChange={(event) => setRebuildLaunchMode(event.target.value as 'auto' | 'jar' | 'mainclass')}
                    >
                      <option value="auto">launch: auto</option>
                      <option value="jar">launch: jar</option>
                      <option value="mainclass">launch: mainclass</option>
                    </select>
                    <input
                      placeholder="Launch main class (optional)"
                      value={rebuildLaunchMainClass}
                      onChange={(event) => setRebuildLaunchMainClass(event.target.value)}
                    />
                  </div>
                  <textarea
                    placeholder="Launch classpath entries (one per line, supports globs: libraries/**/*.jar)"
                    rows={3}
                    value={rebuildLaunchClasspath}
                    onChange={(event) => setRebuildLaunchClasspath(event.target.value)}
                  />
                  <label className="checkbox">
                    <input
                      type="checkbox"
                      checked={rebuildPublishToServers}
                      onChange={(event) => setRebuildPublishToServers(event.target.checked)}
                    />
                    Publish buildId to profile servers
                  </label>
                </div>
                <ul className="list">
                  {profiles.map((profile) => (
                    <li key={profile.id}>
                      <span className="list-text">
                        {profile.name} ({profile.slug})
                        <small>{profile.iconKey || 'no icon'}</small>
                        <small>
                          jvm: {profile.jvmArgsDefault ? (profile.jvmArgsDefault.length > 96 ? `${profile.jvmArgsDefault.slice(0, 96)}...` : profile.jvmArgsDefault) : 'default (global)'}
                        </small>
                        <small>
                          game: {profile.gameArgsDefault ? (profile.gameArgsDefault.length > 96 ? `${profile.gameArgsDefault.slice(0, 96)}...` : profile.gameArgsDefault) : 'default (global)'}
                        </small>
                        <small>bundled Java: {profile.bundledJavaPath || 'not set'}</small>
                        <small>runtime key: {profile.bundledRuntimeKey || 'not set'}</small>
                        <small>
                          runtime meta: {profile.bundledRuntimeSizeBytes > 0 ? formatBytes(profile.bundledRuntimeSizeBytes) : 'n/a'}
                          {profile.bundledRuntimeContentType ? ` | ${profile.bundledRuntimeContentType}` : ''}
                          {profile.bundledRuntimeSha256 ? ` | sha256 ${profile.bundledRuntimeSha256.slice(0, 12)}...` : ''}
                        </small>
                        <small>build: {profile.latestBuildId || 'none'}</small>
                      </span>
                      <div>
                        <button onClick={() => onProfileRebuild(profile.id)}>Rebuild</button>
                        <button onClick={() => onProfileEdit(profile)}>Edit</button>
                        <button onClick={() => onProfileDelete(profile.id)}>Delete</button>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'news' ? '' : 'is-hidden'}`}>
              <form className="form form-small" onSubmit={onNewsSubmit}>
                <h3>{editingNewsId ? 'Edit news' : 'Create news'}</h3>
                <input
                  placeholder="Title"
                  value={newsForm.title}
                  onChange={(event) => setNewsForm((prev) => ({ ...prev, title: event.target.value }))}
                  required
                />
                <textarea
                  placeholder="Body (Markdown/JSON/plain text)"
                  value={newsForm.body}
                  onChange={(event) => setNewsForm((prev) => ({ ...prev, body: event.target.value }))}
                  rows={6}
                  required
                />
                <input
                  placeholder="Source (manual/rss/json/telegram/vk)"
                  value={newsForm.source}
                  onChange={(event) => setNewsForm((prev) => ({ ...prev, source: event.target.value }))}
                />
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={newsForm.pinned}
                    onChange={(event) => setNewsForm((prev) => ({ ...prev, pinned: event.target.checked }))}
                  />
                  Pinned
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={newsForm.enabled}
                    onChange={(event) => setNewsForm((prev) => ({ ...prev, enabled: event.target.checked }))}
                  />
                  Enabled
                </label>
                <div className="button-row">
                  <button disabled={busy || !token}>{editingNewsId ? 'Update news' : 'Create news'}</button>
                  <button type="button" onClick={onNewsResetForm} disabled={busy || !token}>
                    Reset
                  </button>
                </div>
              </form>

              <section>
                <h3>News</h3>
                <ul className="list">
                  {newsItems.map((item) => (
                    <li key={item.id}>
                      <span className="list-text">
                        {item.title}
                        <small>{new Date(item.createdAtUtc).toLocaleString()}</small>
                        <small>
                          {item.source} | pinned: {String(item.pinned)} | enabled: {String(item.enabled)}
                        </small>
                        <small>{item.body.slice(0, 120)}{item.body.length > 120 ? '...' : ''}</small>
                      </span>
                      <div>
                        <button onClick={() => onNewsEdit(item)}>Edit</button>
                        <button onClick={() => onNewsDelete(item.id)}>Delete</button>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'news' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>News Sources</h3>
                <input
                  placeholder="Source name"
                  value={newsSourceForm.name}
                  onChange={(event) => setNewsSourceForm((prev) => ({ ...prev, name: event.target.value }))}
                />
                <select
                  value={newsSourceForm.type}
                  onChange={(event) =>
                    setNewsSourceForm((prev) => ({ ...prev, type: event.target.value as NewsSourceForm['type'] }))
                  }
                >
                  <option value="rss">RSS</option>
                  <option value="json">JSON</option>
                  <option value="markdown">Markdown</option>
                  <option value="telegram">Telegram</option>
                  <option value="vk">VK</option>
                </select>
                <input
                  placeholder="URL"
                  value={newsSourceForm.url}
                  onChange={(event) => setNewsSourceForm((prev) => ({ ...prev, url: event.target.value }))}
                />
                <input
                  type="number"
                  min={1}
                  max={20}
                  placeholder="Max items"
                  value={newsSourceForm.maxItems}
                  onChange={(event) =>
                    setNewsSourceForm((prev) => ({ ...prev, maxItems: Math.min(20, Math.max(1, Number(event.target.value) || 5)) }))
                  }
                />
                <input
                  type="number"
                  min={1}
                  max={1440}
                  placeholder="Min fetch interval (minutes)"
                  value={newsSourceForm.minFetchIntervalMinutes}
                  onChange={(event) =>
                    setNewsSourceForm((prev) => ({
                      ...prev,
                      minFetchIntervalMinutes: Math.min(1440, Math.max(1, Number(event.target.value) || 10)),
                    }))
                  }
                />
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={newsSourceForm.enabled}
                    onChange={(event) => setNewsSourceForm((prev) => ({ ...prev, enabled: event.target.checked }))}
                  />
                  Enabled
                </label>
                <div className="button-row">
                  <button type="button" onClick={onNewsSourceSaveLocal} disabled={busy || !token}>
                    {newsSourceForm.id ? 'Update source' : 'Add source'}
                  </button>
                  <button type="button" onClick={onNewsSourceResetForm} disabled={busy || !token}>
                    Reset
                  </button>
                </div>
                <div className="button-row">
                  <button type="button" onClick={onSaveNewsSourcesSettings} disabled={busy || !token}>
                    Save source settings
                  </button>
                  <button type="button" onClick={() => onSyncNewsSources(false)} disabled={busy || !token}>
                    Sync news now
                  </button>
                  <button type="button" onClick={() => onSyncNewsSources(true)} disabled={busy || !token}>
                    Force sync now
                  </button>
                </div>
                <small>
                  If TG/VK/API source is unavailable, publish news manually in the "News" block above.
                </small>
              </section>

              <section>
                <h3>Sources Status</h3>
                <ul className="list">
                  {newsSources.map((source) => (
                    <li key={source.id}>
                      <span className="list-text">
                        {source.name} ({source.type})
                        <small>{source.url}</small>
                        <small>
                          enabled: {String(source.enabled)} | max: {source.maxItems} | min interval: {source.minFetchIntervalMinutes} min
                        </small>
                        <small>
                          last fetch attempt:{' '}
                          {source.lastFetchAttemptAtUtc ? new Date(source.lastFetchAttemptAtUtc).toLocaleString() : 'never'}
                        </small>
                        <small>
                          last sync: {source.lastSyncAtUtc ? new Date(source.lastSyncAtUtc).toLocaleString() : 'never'}
                        </small>
                        <small>
                          last content change:{' '}
                          {source.lastContentChangeAtUtc ? new Date(source.lastContentChangeAtUtc).toLocaleString() : 'unknown'}
                        </small>
                        <small>{source.lastSyncError || 'ok'}</small>
                      </span>
                      <div>
                        <button onClick={() => onNewsSourceEdit(source)}>Edit</button>
                        <button onClick={() => onSyncSingleNewsSource(source.id, source.name, false)}>Sync</button>
                        <button onClick={() => onSyncSingleNewsSource(source.id, source.name, true)}>Force</button>
                        <button onClick={() => onNewsSourceDeleteLocal(source.id)}>Delete</button>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'news' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>News Auto-sync</h3>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={newsSyncSettings.enabled}
                    onChange={(event) =>
                      setNewsSyncSettings((prev) => ({
                        ...prev,
                        enabled: event.target.checked,
                      }))
                    }
                  />
                  Enable background sync
                </label>
                <input
                  type="number"
                  min={5}
                  max={1440}
                  placeholder="Interval minutes"
                  value={newsSyncSettings.intervalMinutes}
                  onChange={(event) =>
                    setNewsSyncSettings((prev) => ({
                      ...prev,
                      intervalMinutes: Math.min(1440, Math.max(5, Number(event.target.value) || 60)),
                    }))
                  }
                />
                <div className="button-row">
                  <button type="button" onClick={onSaveNewsSyncSettings} disabled={busy || !token}>
                    Save auto-sync settings
                  </button>
                  <button type="button" onClick={onRunNewsAutoSyncNow} disabled={busy || !token}>
                    Run now
                  </button>
                </div>
              </section>

              <section>
                <h3>Auto-sync Status</h3>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Enabled / Interval
                      <small>
                        {String(newsSyncSettings.enabled)} | {newsSyncSettings.intervalMinutes} min
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Last run
                      <small>
                        {newsSyncSettings.lastRunAtUtc ? new Date(newsSyncSettings.lastRunAtUtc).toLocaleString() : 'never'}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Last error
                      <small>{newsSyncSettings.lastRunError || 'none'}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Updated
                      <small>{newsSyncSettings.updatedAtUtc ? new Date(newsSyncSettings.updatedAtUtc).toLocaleString() : 'not persisted yet'}</small>
                    </span>
                  </li>
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'news' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>News Retention</h3>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={newsRetentionSettings.enabled}
                    onChange={(event) =>
                      setNewsRetentionSettings((prev) => ({
                        ...prev,
                        enabled: event.target.checked,
                      }))
                    }
                  />
                  Enable retention policy
                </label>
                <input
                  type="number"
                  min={50}
                  max={10000}
                  placeholder="Max items"
                  value={newsRetentionSettings.maxItems}
                  onChange={(event) =>
                    setNewsRetentionSettings((prev) => ({
                      ...prev,
                      maxItems: Math.min(10000, Math.max(50, Number(event.target.value) || 500)),
                    }))
                  }
                />
                <input
                  type="number"
                  min={1}
                  max={3650}
                  placeholder="Max age days"
                  value={newsRetentionSettings.maxAgeDays}
                  onChange={(event) =>
                    setNewsRetentionSettings((prev) => ({
                      ...prev,
                      maxAgeDays: Math.min(3650, Math.max(1, Number(event.target.value) || 30)),
                    }))
                  }
                />
                <div className="button-row">
                  <button type="button" onClick={onSaveNewsRetentionSettings} disabled={busy || !token}>
                    Save retention settings
                  </button>
                  <button type="button" onClick={onDryRunNewsRetentionNow} disabled={busy || !token}>
                    Dry-run
                  </button>
                  <button type="button" onClick={onRunNewsRetentionNow} disabled={busy || !token}>
                    Run retention now
                  </button>
                </div>
              </section>

              <section>
                <h3>Retention Status</h3>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Enabled
                      <small>{String(newsRetentionSettings.enabled)}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Limits
                      <small>
                        {newsRetentionSettings.maxItems} items | {newsRetentionSettings.maxAgeDays} days
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Last apply / deleted
                      <small>
                        {newsRetentionSettings.lastAppliedAtUtc
                          ? `${new Date(newsRetentionSettings.lastAppliedAtUtc).toLocaleString()} | ${newsRetentionSettings.lastDeletedItems}`
                          : 'never'}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Last error
                      <small>{newsRetentionSettings.lastError || 'none'}</small>
                    </span>
                  </li>
                  {newsRetentionDryRun && (
                    <li>
                      <span className="list-text">
                        Dry-run preview
                        <small>
                          del: {newsRetentionDryRun.wouldDeleteTotal} (age {newsRetentionDryRun.wouldDeleteByAge}, overflow {newsRetentionDryRun.wouldDeleteByOverflow}) | remain: {newsRetentionDryRun.wouldRemainItems}
                        </small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'security' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Bans</h3>
                <h4>HWID ban</h4>
                <input
                  placeholder="HWID hash"
                  value={hwidBanForm.hwidHash}
                  onChange={(event) => setHwidBanForm((prev) => ({ ...prev, hwidHash: event.target.value }))}
                />
                <input
                  placeholder="Reason"
                  value={hwidBanForm.reason}
                  onChange={(event) => setHwidBanForm((prev) => ({ ...prev, reason: event.target.value }))}
                />
                <label>
                  Expires (optional)
                  <input
                    type="datetime-local"
                    value={hwidBanForm.expiresAtLocal}
                    onChange={(event) => setHwidBanForm((prev) => ({ ...prev, expiresAtLocal: event.target.value }))}
                  />
                </label>
                <button type="button" onClick={onCreateHwidBan} disabled={busy || !token}>
                  Ban HWID
                </button>

                <h4>Account ban</h4>
                <input
                  placeholder="Username or ExternalId"
                  value={accountBanForm.user}
                  onChange={(event) => setAccountBanForm((prev) => ({ ...prev, user: event.target.value }))}
                />
                <input
                  placeholder="Reason"
                  value={accountBanForm.reason}
                  onChange={(event) => setAccountBanForm((prev) => ({ ...prev, reason: event.target.value }))}
                />
                <label>
                  Expires (optional)
                  <input
                    type="datetime-local"
                    value={accountBanForm.expiresAtLocal}
                    onChange={(event) => setAccountBanForm((prev) => ({ ...prev, expiresAtLocal: event.target.value }))}
                  />
                </label>
                <button type="button" onClick={onCreateAccountBan} disabled={busy || !token}>
                  Ban account
                </button>
                <button type="button" onClick={onResetAccountHwid} disabled={busy || !token}>
                  Reset account HWID
                </button>
              </section>

              <section>
                <h3>Active / History bans</h3>
                <ul className="list">
                  {bans.map((ban) => (
                    <li key={ban.id}>
                      <span className="list-text">
                        {ban.accountId ? `account: ${ban.accountUsername}` : 'hwid'}
                        <small>{ban.accountId ? ban.accountExternalId : ban.hwidHash || 'n/a'}</small>
                        <small>
                          active: {String(ban.active)} | created: {new Date(ban.createdAtUtc).toLocaleString()}
                        </small>
                        <small>
                          expires: {ban.expiresAtUtc ? new Date(ban.expiresAtUtc).toLocaleString() : 'never'}
                        </small>
                        <small>{ban.reason}</small>
                      </span>
                      <div>
                        <button onClick={() => onDeleteBan(ban.id)}>Remove</button>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'security' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Two-factor authentication (2FA)</h3>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={twoFactorSettings.enabled}
                    onChange={(event) =>
                      setTwoFactorSettings((prev) => ({
                        ...prev,
                        enabled: event.target.checked,
                      }))
                    }
                  />
                  Enable global 2FA policy
                </label>
                <small className="muted">
                  Global switch must be enabled for required accounts. Use account list below to mark who must pass TOTP.
                </small>
                <button type="button" onClick={onSaveTwoFactorSettings} disabled={busy || !token}>
                  Save 2FA settings
                </button>
                <div className="grid-inline">
                  <input
                    placeholder="Search username or externalId"
                    value={twoFactorSearch}
                    onChange={(event) => setTwoFactorSearch(event.target.value)}
                  />
                  <input
                    type="number"
                    min={1}
                    max={500}
                    placeholder="Limit"
                    value={twoFactorLimit}
                    onChange={(event) => setTwoFactorLimit(Math.min(500, Math.max(1, Number(event.target.value) || 100)))}
                  />
                </div>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={twoFactorRequiredOnly}
                    onChange={(event) => setTwoFactorRequiredOnly(event.target.checked)}
                  />
                  Show required only
                </label>
                <button type="button" onClick={onRefreshTwoFactorAccounts} disabled={busy || !token}>
                  Refresh 2FA accounts
                </button>
              </section>

              <section>
                <h3>2FA Status & Accounts</h3>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Global status
                      <small>enabled: {String(twoFactorSettings.enabled)}</small>
                      <small>
                        updated: {twoFactorSettings.updatedAtUtc ? new Date(twoFactorSettings.updatedAtUtc).toLocaleString() : 'not persisted yet'}
                      </small>
                    </span>
                  </li>
                  {twoFactorAccounts.map((account) => (
                    <li key={account.id}>
                      <span className="list-text">
                        {account.username}
                        <small>external: {account.externalId}</small>
                        <small>
                          required: {String(account.twoFactorRequired)} | secret: {String(account.hasSecret)}
                        </small>
                        <small>
                          enrolled: {account.twoFactorEnrolledAtUtc ? new Date(account.twoFactorEnrolledAtUtc).toLocaleString() : 'no'}
                        </small>
                      </span>
                      <div>
                        <button
                          onClick={() => onSetAccountTwoFactorRequired(account.id, !account.twoFactorRequired)}
                          disabled={busy || !token}
                        >
                          {account.twoFactorRequired ? 'Unset required' : 'Require 2FA'}
                        </button>
                        <button onClick={() => onEnrollAccountTwoFactor(account.id)} disabled={busy || !token}>
                          Regenerate key
                        </button>
                        <button onClick={() => onResetAccountTwoFactor(account.id)} disabled={busy || !token}>
                          Reset
                        </button>
                      </div>
                    </li>
                  ))}
                  {twoFactorAccounts.length === 0 && (
                    <li>
                      <span className="list-text">
                        No accounts
                        <small>Accounts appear after player login.</small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'crashes' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Crash Reports</h3>
                <select
                  value={crashStatusFilter}
                  onChange={(event) =>
                    setCrashStatusFilter(
                      event.target.value === 'resolved'
                        ? 'resolved'
                        : event.target.value === 'all'
                          ? 'all'
                          : 'new',
                    )
                  }
                >
                  <option value="new">Status: new</option>
                  <option value="resolved">Status: resolved</option>
                  <option value="all">Status: all</option>
                </select>
                <input
                  placeholder="Profile slug filter"
                  value={crashProfileSlugFilter}
                  onChange={(event) => setCrashProfileSlugFilter(event.target.value)}
                />
                <input
                  placeholder="Search (Crash ID / reason / error type / log)"
                  value={crashSearchFilter}
                  onChange={(event) => setCrashSearchFilter(event.target.value)}
                />
                <div className="grid-inline">
                  <input
                    type="datetime-local"
                    placeholder="From (local)"
                    value={crashFromLocal}
                    onChange={(event) => setCrashFromLocal(event.target.value)}
                  />
                  <input
                    type="datetime-local"
                    placeholder="To (local)"
                    value={crashToLocal}
                    onChange={(event) => setCrashToLocal(event.target.value)}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    type="number"
                    min={1}
                    max={500}
                    placeholder="Limit"
                    value={crashLimit}
                    onChange={(event) => setCrashLimit(Math.min(500, Math.max(1, Number(event.target.value) || 50)))}
                  />
                  <small>
                    Range:
                    {' '}
                    <button type="button" onClick={() => onSetCrashRangeDays(1)}>24h</button>
                    {' '}
                    <button type="button" onClick={() => onSetCrashRangeDays(7)}>7d</button>
                    {' '}
                    <button type="button" onClick={() => onSetCrashRangeDays(30)}>30d</button>
                  </small>
                </div>
                <div className="button-row">
                  <button type="button" onClick={onRefreshCrashes} disabled={busy || !token}>
                    Refresh crashes
                  </button>
                  <button type="button" onClick={onClearCrashFilters} disabled={busy || !token}>
                    Clear filters
                  </button>
                </div>
                <div className="button-row">
                  <button type="button" onClick={() => onExportCrashes('json')} disabled={busy || !token}>
                    Export JSON
                  </button>
                  <button type="button" onClick={() => onExportCrashes('csv')} disabled={busy || !token}>
                    Export CSV
                  </button>
                </div>
                <small>Loaded: {crashes.length}</small>
              </section>

              <section>
                <h3>Crash Feed</h3>
                <ul className="list">
                  {crashes.map((crash) => (
                    <li key={crash.id}>
                      <span className="list-text">
                        {crash.crashId} | {crash.status}
                        <small>
                          profile: {crash.profileSlug || '-'} | server: {crash.serverName || '-'} | route: {crash.routeCode || '-'}
                        </small>
                        <small>
                          exit: {crash.exitCode ?? 'n/a'} | error: {crash.errorType || 'n/a'} | java: {crash.javaVersion || 'n/a'}
                        </small>
                        <small>
                          created: {new Date(crash.createdAtUtc).toLocaleString()} | resolved: {crash.resolvedAtUtc ? new Date(crash.resolvedAtUtc).toLocaleString() : 'no'}
                        </small>
                        <small>{crash.reason}</small>
                        <small>
                          {crash.logExcerpt.length > 220
                            ? `${crash.logExcerpt.slice(0, 220)}...`
                            : crash.logExcerpt || '(empty log excerpt)'}
                        </small>
                      </span>
                      <div>
                        {crash.status === 'new' ? (
                          <button onClick={() => onUpdateCrashStatus(crash.id, 'resolved')} disabled={busy || !token}>
                            Mark resolved
                          </button>
                        ) : (
                          <button onClick={() => onUpdateCrashStatus(crash.id, 'new')} disabled={busy || !token}>
                            Reopen
                          </button>
                        )}
                      </div>
                    </li>
                  ))}
                  {crashes.length === 0 && (
                    <li>
                      <span className="list-text">
                        No crash reports
                        <small>Try broadening filters or waiting for launcher uploads.</small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'docs' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>{editingDocId ? 'Edit article' : 'Create article'}</h3>
                <form className="form" onSubmit={onDocSubmit}>
                  <div className="grid-inline">
                    <input
                      placeholder="Slug (e.g. install-linux-docker)"
                      value={docForm.slug}
                      onChange={(event) => setDocForm((prev) => ({ ...prev, slug: event.target.value }))}
                      required
                    />
                    <input
                      placeholder="Category (docs/faq/operations)"
                      value={docForm.category}
                      onChange={(event) => setDocForm((prev) => ({ ...prev, category: event.target.value }))}
                    />
                  </div>
                  <input
                    placeholder="Title"
                    value={docForm.title}
                    onChange={(event) => setDocForm((prev) => ({ ...prev, title: event.target.value }))}
                    required
                  />
                  <input
                    placeholder="Summary"
                    value={docForm.summary}
                    onChange={(event) => setDocForm((prev) => ({ ...prev, summary: event.target.value }))}
                  />
                  <textarea
                    placeholder="Markdown body"
                    rows={10}
                    value={docForm.bodyMarkdown}
                    onChange={(event) => setDocForm((prev) => ({ ...prev, bodyMarkdown: event.target.value }))}
                    required
                  />
                  <small className="muted">Markdown preview:</small>
                  <pre className="markdown-preview">{docForm.bodyMarkdown || '(empty)'}</pre>
                  <div className="grid-inline">
                    <input
                      type="number"
                      placeholder="Order"
                      min={0}
                      max={10000}
                      value={docForm.order}
                      onChange={(event) => setDocForm((prev) => ({ ...prev, order: Number(event.target.value) || 0 }))}
                    />
                    <label className="checkbox">
                      <input
                        type="checkbox"
                        checked={docForm.published}
                        onChange={(event) => setDocForm((prev) => ({ ...prev, published: event.target.checked }))}
                      />
                      Published
                    </label>
                  </div>
                  <div className="button-row">
                    <button type="submit" disabled={busy || !token}>
                      {editingDocId ? 'Update article' : 'Create article'}
                    </button>
                    <button type="button" onClick={onDocResetForm} disabled={busy || !token}>
                      Reset
                    </button>
                    <button type="button" onClick={onDocSeed} disabled={busy || !token}>
                      Load starter docs
                    </button>
                  </div>
                </form>
              </section>

              <section>
                <h3>Documentation / FAQ</h3>
                <div className="form form-small">
                  <div className="grid-inline">
                    <input
                      placeholder="Search articles"
                      value={docSearchFilter}
                      onChange={(event) => setDocSearchFilter(event.target.value)}
                    />
                    <input
                      placeholder="Category filter"
                      value={docCategoryFilter}
                      onChange={(event) => setDocCategoryFilter(event.target.value)}
                    />
                  </div>
                  <div className="grid-inline">
                    <label className="checkbox">
                      <input
                        type="checkbox"
                        checked={docPublishedOnlyFilter}
                        onChange={(event) => setDocPublishedOnlyFilter(event.target.checked)}
                      />
                      Published only
                    </label>
                    <button type="button" onClick={onRefreshDocs} disabled={busy || !token}>
                      Refresh
                    </button>
                  </div>
                </div>
                <ul className="list">
                  {filteredDocs.map((article) => (
                    <li key={article.id}>
                      <span className="list-text">
                        {article.title} ({article.category})
                        <small>{article.slug}</small>
                        <small>published: {String(article.published)} | order: {article.order}</small>
                        <small>updated: {new Date(article.updatedAtUtc).toLocaleString()}</small>
                        <small>{article.summary || '(no summary)'}</small>
                      </span>
                      <div>
                        <button onClick={() => onDocEdit(article)}>Edit</button>
                        <button onClick={() => onDocDelete(article.id)}>Delete</button>
                      </div>
                    </li>
                  ))}
                  {filteredDocs.length === 0 && (
                    <li>
                      <span className="list-text">
                        No documentation articles
                        <small>Use "Load starter docs" to bootstrap installation/update/backup/FAQ content.</small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'integrations' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Auth Provider</h3>
                <select
                  value={authProviderSettings.authMode}
                  onChange={(event) =>
                    setAuthProviderSettings((prev) => ({
                      ...prev,
                      authMode: event.target.value === 'any' ? 'any' : 'external',
                    }))
                  }
                >
                  <option value="external">External provider</option>
                  <option value="any">ANY (accept any login/password)</option>
                </select>
                <input
                  placeholder="Login URL (external auth endpoint)"
                  value={authProviderSettings.loginUrl}
                  disabled={authProviderSettings.authMode === 'any'}
                  onChange={(event) =>
                    setAuthProviderSettings((prev) => ({ ...prev, loginUrl: event.target.value }))
                  }
                />
                <div className="grid-inline">
                  <input
                    placeholder="Login field key (e.g. username/login)"
                    value={authProviderSettings.loginFieldKey}
                    disabled={authProviderSettings.authMode === 'any'}
                    onChange={(event) =>
                      setAuthProviderSettings((prev) => ({ ...prev, loginFieldKey: event.target.value }))
                    }
                  />
                  <input
                    placeholder="Password field key (e.g. password/pass)"
                    value={authProviderSettings.passwordFieldKey}
                    disabled={authProviderSettings.authMode === 'any'}
                    onChange={(event) =>
                      setAuthProviderSettings((prev) => ({ ...prev, passwordFieldKey: event.target.value }))
                    }
                  />
                </div>
                <input
                  type="number"
                  placeholder="Timeout seconds"
                  min={5}
                  max={120}
                  value={authProviderSettings.timeoutSeconds}
                  onChange={(event) =>
                    setAuthProviderSettings((prev) => ({
                      ...prev,
                      timeoutSeconds: Math.min(120, Math.max(5, Number(event.target.value) || 15)),
                    }))
                  }
                />
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={authProviderSettings.allowDevFallback}
                    disabled={authProviderSettings.authMode === 'any'}
                    onChange={(event) =>
                      setAuthProviderSettings((prev) => ({ ...prev, allowDevFallback: event.target.checked }))
                    }
                  />
                  Allow dev fallback (local player auth when URL empty)
                </label>
                {authProviderSettings.authMode === 'any' && (
                  <small className="warning-note">
                    WARNING: ANY mode is simplified and insecure. Use only for test/private environments.
                  </small>
                )}
                <div className="button-row">
                  <button type="button" onClick={onSaveAuthProviderSettings} disabled={busy || !token}>
                    Save auth provider settings
                  </button>
                  <button type="button" onClick={onProbeAuthProvider} disabled={busy || !token}>
                    Probe auth endpoint
                  </button>
                </div>
              </section>

              <section>
                <h3>Auth Provider Status</h3>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Auth mode
                      <small>{authProviderSettings.authMode}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Login URL
                      <small>{authProviderSettings.loginUrl || '(empty)'}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Request field keys
                      <small>
                        {authProviderSettings.loginFieldKey} / {authProviderSettings.passwordFieldKey}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Timeout seconds
                      <small>{authProviderSettings.timeoutSeconds}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Dev fallback
                      <small>{String(authProviderSettings.allowDevFallback)}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Updated
                      <small>
                        {authProviderSettings.updatedAtUtc
                          ? new Date(authProviderSettings.updatedAtUtc).toLocaleString()
                          : 'not persisted yet'}
                      </small>
                    </span>
                  </li>
                  {authProbeResult && (
                    <li>
                      <span className="list-text">
                        Last probe
                        <small>
                          ok: {String(authProbeResult.success)} | mode: {authProbeResult.authMode} | status:{' '}
                          {authProbeResult.statusCode ?? 'n/a'}
                        </small>
                        <small>{authProbeResult.message}</small>
                        <small>{new Date(authProbeResult.checkedAtUtc).toLocaleString()}</small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'integrations' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Storage Backend</h3>
                <select
                  value={s3Settings.useS3 ? 's3' : 'local'}
                  onChange={(event) =>
                    setS3Settings((prev) => ({
                      ...prev,
                      useS3: event.target.value !== 'local',
                    }))
                  }
                >
                  <option value="s3">S3 / MinIO</option>
                  <option value="local">Local filesystem</option>
                </select>
                <input
                  placeholder="Local root path (/app/storage or C:\\launcher\\storage)"
                  value={s3Settings.localRootPath}
                  onChange={(event) => setS3Settings((prev) => ({ ...prev, localRootPath: event.target.value }))}
                  disabled={s3Settings.useS3}
                />
                <input
                  placeholder="Endpoint (http://localhost:9000)"
                  value={s3Settings.endpoint}
                  onChange={(event) => setS3Settings((prev) => ({ ...prev, endpoint: event.target.value }))}
                  disabled={!s3Settings.useS3}
                />
                <input
                  placeholder="Bucket"
                  value={s3Settings.bucket}
                  onChange={(event) => setS3Settings((prev) => ({ ...prev, bucket: event.target.value }))}
                  disabled={!s3Settings.useS3}
                />
                <div className="grid-inline">
                  <input
                    placeholder="Access key"
                    value={s3Settings.accessKey}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, accessKey: event.target.value }))}
                    disabled={!s3Settings.useS3}
                  />
                  <input
                    placeholder="Secret key"
                    value={s3Settings.secretKey}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, secretKey: event.target.value }))}
                    disabled={!s3Settings.useS3}
                  />
                </div>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={s3Settings.forcePathStyle}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, forcePathStyle: event.target.checked }))}
                    disabled={!s3Settings.useS3}
                  />
                  Force path style
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={s3Settings.useSsl}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, useSsl: event.target.checked }))}
                    disabled={!s3Settings.useS3}
                  />
                  Use SSL (https)
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={s3Settings.autoCreateBucket}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, autoCreateBucket: event.target.checked }))}
                    disabled={!s3Settings.useS3}
                  />
                  Auto create bucket
                </label>
                <small className="muted">
                  Switch backend safely: save settings, then run "Test storage". If probe fails, launcher uploads/downloads will also fail.
                </small>
                <button type="button" onClick={onSaveS3Settings} disabled={busy || !token}>
                  Save storage settings
                </button>
                <button type="button" onClick={onTestStorageSettings} disabled={busy || !token}>
                  Test storage
                </button>

                <h4>Migration</h4>
                <small className="muted">
                  Copy objects from current backend to target backend by key (safe dry-run first).
                </small>
                <select
                  value={storageMigrationTargetMode}
                  onChange={(event) =>
                    setStorageMigrationTargetMode(event.target.value === 's3' ? 's3' : 'local')
                  }
                >
                  <option value="s3">Target: S3 / MinIO</option>
                  <option value="local">Target: Local filesystem</option>
                </select>
                <input
                  placeholder="Prefix filter (optional), e.g. runtimes/ or manifests/"
                  value={storageMigrationPrefix}
                  onChange={(event) => setStorageMigrationPrefix(event.target.value)}
                />
                <input
                  type="number"
                  min={1}
                  max={500000}
                  placeholder="Max objects to scan"
                  value={storageMigrationMaxObjects}
                  onChange={(event) =>
                    setStorageMigrationMaxObjects(Math.min(500000, Math.max(1, Number(event.target.value) || 5000)))
                  }
                />
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={storageMigrationDryRun}
                    onChange={(event) => setStorageMigrationDryRun(event.target.checked)}
                  />
                  Dry-run (no writes)
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={storageMigrationOverwrite}
                    onChange={(event) => setStorageMigrationOverwrite(event.target.checked)}
                  />
                  Overwrite existing target keys
                </label>
                <button type="button" onClick={onRunStorageMigration} disabled={busy || !token}>
                  Run migration
                </button>
              </section>

              <section>
                <h3>Storage Status</h3>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Backend mode
                      <small>{s3Settings.useS3 ? 's3/minio' : 'local'}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Local root path
                      <small>{s3Settings.localRootPath || '(empty)'}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Endpoint / Bucket
                      <small>
                        {s3Settings.endpoint} | {s3Settings.bucket}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Path style / SSL
                      <small>
                        {String(s3Settings.forcePathStyle)} | {String(s3Settings.useSsl)}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Auto create bucket
                      <small>{String(s3Settings.autoCreateBucket)}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Updated
                      <small>{s3Settings.updatedAtUtc ? new Date(s3Settings.updatedAtUtc).toLocaleString() : 'not persisted yet'}</small>
                    </span>
                  </li>
                  {storageTestResult && (
                    <li>
                      <span className="list-text">
                        Last probe
                        <small>
                          ok: {String(storageTestResult.success)} | mode: {storageTestResult.useS3 ? 's3/minio' : 'local'} | {storageTestResult.roundTripMs} ms
                        </small>
                        <small>{storageTestResult.message}</small>
                        <small>{new Date(storageTestResult.testedAtUtc).toLocaleString()}</small>
                      </span>
                    </li>
                  )}
                  {storageMigrationResult && (
                    <li>
                      <span className="list-text">
                        Last migration
                        <small>
                          {storageMigrationResult.dryRun ? 'dry-run' : 'apply'} | {storageMigrationResult.sourceUseS3 ? 's3' : 'local'} → {storageMigrationResult.targetUseS3 ? 's3' : 'local'}
                        </small>
                        <small>
                          scanned: {storageMigrationResult.scanned} | copied: {storageMigrationResult.copied} | skipped: {storageMigrationResult.skipped} | failed: {storageMigrationResult.failed}
                        </small>
                        <small>
                          bytes: {storageMigrationResult.copiedBytes} | duration: {storageMigrationResult.durationMs} ms | truncated: {String(storageMigrationResult.truncated)}
                        </small>
                        {storageMigrationResult.errors.slice(0, 3).map((errorLine, index) => (
                          <small key={`${index}-${errorLine}`}>{errorLine}</small>
                        ))}
                        <small>{new Date(storageMigrationResult.finishedAtUtc).toLocaleString()}</small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'settings' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Branding</h3>
                <input
                  placeholder="Product name"
                  value={brandingSettings.productName}
                  onChange={(event) => setBrandingSettings((prev) => ({ ...prev, productName: event.target.value }))}
                />
                <input
                  placeholder="Developer name"
                  value={brandingSettings.developerName}
                  onChange={(event) => setBrandingSettings((prev) => ({ ...prev, developerName: event.target.value }))}
                />
                <input
                  placeholder="Tagline"
                  value={brandingSettings.tagline}
                  onChange={(event) => setBrandingSettings((prev) => ({ ...prev, tagline: event.target.value }))}
                />
                <input
                  placeholder="Support URL"
                  value={brandingSettings.supportUrl}
                  onChange={(event) => setBrandingSettings((prev) => ({ ...prev, supportUrl: event.target.value }))}
                />
                <div className="grid-inline">
                  <input
                    placeholder="Primary color (#RRGGBB)"
                    value={brandingSettings.primaryColor}
                    onChange={(event) => setBrandingSettings((prev) => ({ ...prev, primaryColor: event.target.value }))}
                  />
                  <input
                    placeholder="Accent color (#RRGGBB)"
                    value={brandingSettings.accentColor}
                    onChange={(event) => setBrandingSettings((prev) => ({ ...prev, accentColor: event.target.value }))}
                  />
                </div>
                <input
                  placeholder="Logo text"
                  value={brandingSettings.logoText}
                  onChange={(event) => setBrandingSettings((prev) => ({ ...prev, logoText: event.target.value }))}
                />
                <input
                  placeholder="Background image URL (optional)"
                  value={brandingSettings.backgroundImageUrl}
                  onChange={(event) => setBrandingSettings((prev) => ({ ...prev, backgroundImageUrl: event.target.value }))}
                />
                <label>
                  Background overlay opacity
                  <input
                    type="range"
                    min={0}
                    max={0.95}
                    step={0.05}
                    value={brandingSettings.backgroundOverlayOpacity}
                    onChange={(event) =>
                      setBrandingSettings((prev) => ({
                        ...prev,
                        backgroundOverlayOpacity: Math.min(0.95, Math.max(0, Number(event.target.value) || 0)),
                      }))
                    }
                  />
                </label>
                <div className="grid-inline">
                  <select
                    value={brandingSettings.loginCardPosition}
                    onChange={(event) =>
                      setBrandingSettings((prev) => ({
                        ...prev,
                        loginCardPosition:
                          event.target.value === 'left'
                            ? 'left'
                            : event.target.value === 'right'
                              ? 'right'
                              : 'center',
                      }))
                    }
                  >
                    <option value="left">Login card: left</option>
                    <option value="center">Login card: center</option>
                    <option value="right">Login card: right</option>
                  </select>
                  <input
                    type="number"
                    min={340}
                    max={640}
                    placeholder="Login card width"
                    value={brandingSettings.loginCardWidth}
                    onChange={(event) =>
                      setBrandingSettings((prev) => ({
                        ...prev,
                        loginCardWidth: Math.min(640, Math.max(340, Number(event.target.value) || 460)),
                      }))
                    }
                  />
                </div>
                <button type="button" onClick={onSaveBrandingSettings} disabled={busy || !token}>
                  Save branding settings
                </button>
              </section>

              <section>
                <h3>Branding Preview</h3>
                <div
                  className="branding-preview"
                  style={{
                    borderColor: brandingSettings.accentColor || '#20C997',
                    backgroundImage: brandingSettings.backgroundImageUrl
                      ? `linear-gradient(rgba(6,10,18,${Math.min(0.95, Math.max(0, Number(brandingSettings.backgroundOverlayOpacity) || 0.55))}), rgba(6,10,18,${Math.min(0.95, Math.max(0, Number(brandingSettings.backgroundOverlayOpacity) || 0.55))})), url(${brandingSettings.backgroundImageUrl})`
                      : `linear-gradient(140deg, ${brandingSettings.primaryColor || '#2F6FED'}, ${brandingSettings.accentColor || '#20C997'})`,
                  }}
                >
                  <div className={`branding-preview-login branding-preview-login-${brandingSettings.loginCardPosition}`}>
                    <div style={{ width: `${brandingSettings.loginCardWidth}px`, maxWidth: '100%' }}>
                      <strong>{brandingSettings.productName || 'BivLauncher'}</strong>
                      <small>{brandingSettings.tagline || 'Managed launcher platform'}</small>
                    </div>
                  </div>
                </div>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Product / Developer
                      <small>
                        {brandingSettings.productName} / {brandingSettings.developerName}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Tagline
                      <small>{brandingSettings.tagline}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Colors
                      <small>
                        {brandingSettings.primaryColor} | {brandingSettings.accentColor}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Background / Overlay
                      <small>
                        {brandingSettings.backgroundImageUrl || '(none)'} | {brandingSettings.backgroundOverlayOpacity}
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Login card
                      <small>
                        {brandingSettings.loginCardPosition} | {brandingSettings.loginCardWidth}px
                      </small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Support / Logo
                      <small>
                        {brandingSettings.supportUrl} | {brandingSettings.logoText}
                      </small>
                    </span>
                  </li>
                </ul>

                <h3>Developer Support (Read-only)</h3>
                <small>This block is fixed for launcher owners and cannot be edited from the panel.</small>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Name
                      <small>{developerSupportInfo.displayName}</small>
                    </span>
                  </li>
                  <li>
                    <span className="list-text">
                      Telegram
                      <small>{developerSupportInfo.telegram}</small>
                    </span>
                    <button type="button" onClick={() => onCopyToClipboard(developerSupportInfo.telegram, 'Telegram')}>
                      Copy
                    </button>
                  </li>
                  <li>
                    <span className="list-text">
                      Discord
                      <small>{developerSupportInfo.discord}</small>
                    </span>
                    <button type="button" onClick={() => onCopyToClipboard(developerSupportInfo.discord, 'Discord')}>
                      Copy
                    </button>
                  </li>
                  <li>
                    <span className="list-text">
                      Website
                      <small>{developerSupportInfo.website}</small>
                    </span>
                    <button type="button" onClick={() => onCopyToClipboard(developerSupportInfo.website, 'Website')}>
                      Copy
                    </button>
                  </li>
                  <li>
                    <span className="list-text">
                      Notes
                      <small>{developerSupportInfo.notes}</small>
                    </span>
                  </li>
                </ul>

                <h3>Project Install Telemetry</h3>
                <small>
                  Tracks project name + launcher version only. No login, password, IP, token, or server list data is stored.
                </small>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={installTelemetrySettings.enabled}
                    onChange={(event) =>
                      setInstallTelemetrySettings((prev) => ({ ...prev, enabled: event.target.checked }))
                    }
                  />
                  Enable project install telemetry
                </label>
                <div className="button-row">
                  <button type="button" onClick={onSaveInstallTelemetrySettings} disabled={busy || !token}>
                    Save telemetry setting
                  </button>
                  <button type="button" onClick={onReloadProjectInstallStats} disabled={busy || !token}>
                    Refresh project list
                  </button>
                </div>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Updated
                      <small>
                        {installTelemetrySettings.updatedAtUtc
                          ? new Date(installTelemetrySettings.updatedAtUtc).toLocaleString()
                          : 'default/fallback'}
                      </small>
                    </span>
                  </li>
                  {projectInstallStats.map((item) => (
                    <li key={item.id}>
                      <span className="list-text">
                        {item.projectName}
                        <small>launcher: {item.lastLauncherVersion || 'unknown'}</small>
                        <small>
                          seen: {item.seenCount} | first: {new Date(item.firstSeenAtUtc).toLocaleString()} | last:{' '}
                          {new Date(item.lastSeenAtUtc).toLocaleString()}
                        </small>
                      </span>
                    </li>
                  ))}
                  {projectInstallStats.length === 0 && (
                    <li>
                      <span className="list-text">
                        No installation records yet
                        <small>Entries appear after launcher sends first install event.</small>
                      </span>
                    </li>
                  )}
                </ul>
              </section>
            </div>

            <div className={`grid-2 ${activePage === 'audit' ? '' : 'is-hidden'}`}>
              <section className="form form-small">
                <h3>Admin Audit Logs</h3>
                <small>
                  Runtime, CRUD, settings and auth actions with export/cleanup tooling ({auditLogs.length} entries loaded).
                </small>
                <div className="grid-inline">
                  <input
                    placeholder="Action prefix (e.g. runtime)"
                    value={auditLogActionPrefix}
                    onChange={(event) => setAuditLogActionPrefix(event.target.value)}
                  />
                  <input
                    placeholder="Actor (exact)"
                    value={auditLogActor}
                    onChange={(event) => setAuditLogActor(event.target.value)}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    placeholder="Entity type (exact)"
                    value={auditLogEntityType}
                    onChange={(event) => setAuditLogEntityType(event.target.value)}
                  />
                  <input
                    placeholder="Entity id contains"
                    value={auditLogEntityId}
                    onChange={(event) => setAuditLogEntityId(event.target.value)}
                  />
                  <input
                    placeholder="Request id (exact)"
                    value={auditLogRequestId}
                    onChange={(event) => setAuditLogRequestId(event.target.value)}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    placeholder="Remote IP (exact)"
                    value={auditLogRemoteIp}
                    onChange={(event) => setAuditLogRemoteIp(event.target.value)}
                  />
                  <input
                    type="datetime-local"
                    placeholder="From (local)"
                    value={auditLogFromLocal}
                    onChange={(event) => setAuditLogFromLocal(event.target.value)}
                  />
                </div>
                <div className="grid-inline">
                  <input
                    type="datetime-local"
                    placeholder="To (local)"
                    value={auditLogToLocal}
                    onChange={(event) => setAuditLogToLocal(event.target.value)}
                  />
                  <input
                    type="number"
                    min={1}
                    max={500}
                    placeholder="Limit"
                    value={auditLogLimit}
                    onChange={(event) => setAuditLogLimit(Number(event.target.value) || 50)}
                  />
                </div>
                <div className="grid-inline">
                  <small>
                    Range:
                    {' '}
                    <button type="button" onClick={() => onSetAuditRangeDays(1)}>24h</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditRangeDays(7)}>7d</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditRangeDays(30)}>30d</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditRangeDays(90)}>90d</button>
                    {' '}
                    <button type="button" onClick={() => { setAuditLogFromLocal(''); setAuditLogToLocal('') }}>all</button>
                  </small>
                  <button type="button" onClick={onClearAuditFilters}>
                    Clear filters
                  </button>
                </div>
                <div className="grid-inline">
                  <select
                    value={auditLogSortOrder}
                    onChange={(event) => setAuditLogSortOrder(event.target.value === 'asc' ? 'asc' : 'desc')}
                  >
                    <option value="desc">Sort: newest first</option>
                    <option value="asc">Sort: oldest first</option>
                  </select>
                  <small>
                    Presets:
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime')}>runtime*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('profile')}>profiles*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('server')}>servers*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('news')}>news*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('ban')}>bans*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('settings')}>settings*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('admin')}>auth*</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime.upload')}>upload</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime.verify')}>verify</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime.cleanup')}>cleanup</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime.retention.run')}>run</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime.retention.dry-run')}>dry-run</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('runtime.retention.run-from-preview')}>apply</button>
                    {' '}
                    <button type="button" onClick={() => onSetAuditActionPreset('')}>all</button>
                  </small>
                </div>
                <button type="button" onClick={onRefreshAuditLogs} disabled={busy || !token}>
                  Refresh logs
                </button>
                <button type="button" onClick={onLoadMoreAuditLogs} disabled={busy || !token || !auditLogsHasMore}>
                  Load more
                </button>
                <div className="grid-inline">
                  <input
                    type="number"
                    min={1}
                    max={50000}
                    placeholder="Export limit"
                    value={auditExportLimit}
                    onChange={(event) => setAuditExportLimit(Number(event.target.value) || 5000)}
                  />
                  <button type="button" onClick={() => onExportAuditLogs('json')} disabled={busy || !token}>
                    Export JSON
                  </button>
                </div>
                <div className="grid-inline">
                  <button type="button" onClick={() => onExportAuditLogs('csv')} disabled={busy || !token}>
                    Export CSV
                  </button>
                  <small>Export uses current filters and sort.</small>
                </div>
                <div className="grid-inline">
                  <input
                    type="number"
                    min={1}
                    placeholder="Cleanup older than (days)"
                    value={auditCleanupOlderThanDays}
                    onChange={(event) => setAuditCleanupOlderThanDays(Number(event.target.value) || 90)}
                  />
                  <input
                    type="number"
                    min={1}
                    max={50000}
                    placeholder="Cleanup batch limit"
                    value={auditCleanupLimit}
                    onChange={(event) => setAuditCleanupLimit(Number(event.target.value) || 5000)}
                  />
                </div>
                <div className="grid-inline">
                  <button type="button" onClick={() => onAuditCleanup(true)} disabled={busy || !token}>
                    Cleanup dry-run
                  </button>
                  <button type="button" onClick={() => onAuditCleanup(false)} disabled={busy || !token}>
                    Cleanup apply
                  </button>
                </div>
                <small>
                  Loaded: {auditLogs.length} | next offset: {auditLogsOffset} | has more: {String(auditLogsHasMore)} | sort: {auditLogSortOrder}
                </small>
              </section>

              <section>
                <h3>Audit Feed</h3>
                <ul className="list">
                  {auditLogs.map((log) => (
                    <li key={log.id}>
                      <span className="list-text">
                        {log.action}
                        <small>
                          actor: {log.actor} | entity: {log.entityType}:{log.entityId || 'n/a'}
                        </small>
                        <small>
                          req: {log.requestId || 'n/a'} | ip: {log.remoteIp || 'n/a'}
                        </small>
                        <small>{new Date(log.createdAtUtc).toLocaleString()}</small>
                        <small>
                          ua: {log.userAgent ? (log.userAgent.length > 120 ? `${log.userAgent.slice(0, 120)}...` : log.userAgent) : 'n/a'}
                        </small>
                        <small>
                          {log.detailsJson.length > 220
                            ? `${log.detailsJson.slice(0, 220)}...`
                            : log.detailsJson || '{}'}
                        </small>
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            </div>
            </div>
          </section>
        )}

        {notice && <p className="notice">{notice}</p>}
        {error && <p className="error">{error}</p>}
      </section>
    </main>
  )
}

export default App
