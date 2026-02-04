import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

type Phase = 'loading' | 'setup' | 'login' | 'dashboard'

type SetupStatusResponse = { needsSetup: boolean }
type LoginResponse = { token: string; tokenType: string; username: string }
type ApiError = { error?: string; title?: string }
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

type AuthProviderSettings = {
  loginUrl: string
  timeoutSeconds: number
  allowDevFallback: boolean
  updatedAtUtc?: string | null
}

type BrandingSettings = {
  productName: string
  developerName: string
  tagline: string
  supportUrl: string
  primaryColor: string
  accentColor: string
  logoText: string
}

type S3Settings = {
  endpoint: string
  bucket: string
  accessKey: string
  secretKey: string
  forcePathStyle: boolean
  useSsl: boolean
  autoCreateBucket: boolean
  updatedAtUtc?: string | null
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
  type: 'rss' | 'json' | 'markdown'
  url: string
  enabled: boolean
  maxItems: number
  lastSyncAtUtc?: string | null
  lastSyncError: string
  updatedAtUtc: string
}

type NewsSourceForm = {
  id: string | null
  name: string
  type: 'rss' | 'json' | 'markdown'
  url: string
  enabled: boolean
  maxItems: number
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
  bundledJavaPath: string
  bundledRuntimeKey: string
}
type ServerForm = Omit<Server, 'id'>
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

const defaultProfileForm: ProfileForm = {
  name: '',
  slug: '',
  description: '',
  enabled: true,
  iconKey: '',
  priority: 100,
  recommendedRamMb: 2048,
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
  loginUrl: '',
  timeoutSeconds: 15,
  allowDevFallback: true,
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
}

const defaultS3Settings: S3Settings = {
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

function App() {
  const apiBaseUrl = useMemo(() => import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080', [])

  const [phase, setPhase] = useState<Phase>('loading')
  const [username, setUsername] = useState('admin')
  const [password, setPassword] = useState('')
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('blp_admin_token'))
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

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
  const [brandingSettings, setBrandingSettings] = useState<BrandingSettings>(defaultBrandingSettings)
  const [s3Settings, setS3Settings] = useState<S3Settings>(defaultS3Settings)
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
  const [discordForm, setDiscordForm] = useState(defaultDiscordForm)
  const [rebuildLoaderType, setRebuildLoaderType] = useState<string>('vanilla')
  const [rebuildMcVersion, setRebuildMcVersion] = useState('1.21.1')
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

  async function determineStartPhase() {
    setError('')
    setNotice('')
    try {
      const response = await fetch(`${apiBaseUrl}/api/admin/setup/status`)
      if (!response.ok) {
        throw new Error('Failed to fetch setup status')
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
      await loadAdminData(savedToken)
    } catch {
      setPhase('login')
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

      if (text) {
        try {
          const payload = JSON.parse(text) as ApiError
          parsedError = payload.error ?? payload.title ?? ''
        } catch {
          parsedError = text
        }
      }

      throw new Error(parsedError || `Request failed (${response.status})`)
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

  async function loadAdminData(activeToken: string) {
    setError('')
    try {
      const [profileData, serverData, newsData, newsSourcesData, newsSyncData, newsRetentionData, runtimeRetentionData, bansData, authProviderData, brandingData, s3Data] = await Promise.all([
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
        fetch(`${apiBaseUrl}/api/admin/settings/auth-provider`, {
          headers: { Authorization: `Bearer ${activeToken}` },
        }),
        fetch(`${apiBaseUrl}/api/admin/settings/branding`, {
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
        !authProviderData.ok ||
        !brandingData.ok ||
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
      const loadedAuthProviderSettings = (await authProviderData.json()) as AuthProviderSettings
      const loadedBranding = (await brandingData.json()) as BrandingSettings
      const loadedS3Settings = (await s3Data.json()) as S3Settings
      setProfiles(loadedProfiles)
      setServers(loadedServers)
      setNewsItems(loadedNews)
      setNewsSources(loadedNewsSources)
      setNewsSyncSettings(loadedNewsSyncSettings)
      setNewsRetentionSettings(loadedNewsRetentionSettings)
      setRuntimeRetentionSettings(loadedRuntimeRetentionSettings)
      setBans(loadedBans)
      setAuthProviderSettings(loadedAuthProviderSettings)
      setBrandingSettings(loadedBranding)
      setS3Settings(loadedS3Settings)

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
      await loadAdminData(payload.token)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Login failed')
    } finally {
      setBusy(false)
    }
  }

  async function onProfileSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
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
    setProfileForm({
      name: profile.name,
      slug: profile.slug,
      description: profile.description,
      enabled: profile.enabled,
      iconKey: profile.iconKey,
      priority: profile.priority,
      recommendedRamMb: profile.recommendedRamMb,
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
        lastSyncAtUtc: prev.find((x) => x.id === newsSourceForm.id)?.lastSyncAtUtc ?? null,
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

  async function onSyncNewsSources() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<NewsSourcesSyncResponse>('/api/admin/settings/news-sources/sync', {
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

  async function onSyncSingleNewsSource(sourceId: string, sourceName: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await requestWithAuth<NewsSourcesSyncResponse>(
        `/api/admin/settings/news-sources/sync?sourceId=${encodeURIComponent(sourceId)}`,
        {
          method: 'POST',
        },
      )

      const imported = result.results[0]?.imported ?? 0
      const errorMessage = result.results[0]?.error ?? ''
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

  async function onSaveAuthProviderSettings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<AuthProviderSettings>('/api/admin/settings/auth-provider', {
        method: 'PUT',
        body: JSON.stringify({
          loginUrl: authProviderSettings.loginUrl.trim(),
          timeoutSeconds: authProviderSettings.timeoutSeconds,
          allowDevFallback: authProviderSettings.allowDevFallback,
        }),
      })
      setAuthProviderSettings(saved)
      setNotice('Auth provider settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Auth provider save failed')
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

  async function onSaveS3Settings() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const saved = await requestWithAuth<S3Settings>('/api/admin/settings/s3', {
        method: 'PUT',
        body: JSON.stringify({
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
      setNotice('S3 settings saved.')
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'S3 settings save failed')
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
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const build = await requestWithAuth<BuildResponse>(`/api/admin/profiles/${profileId}/rebuild`, {
        method: 'POST',
        body: JSON.stringify({
          loaderType: rebuildLoaderType,
          mcVersion: rebuildMcVersion.trim(),
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
    setBrandingSettings(defaultBrandingSettings)
    setS3Settings(defaultS3Settings)
    setProfileIconFile(null)
    setServerIconFile(null)
    setCosmeticsUser('')
    setSkinFile(null)
    setCapeFile(null)
    setDiscordScopeType('profile')
    setDiscordScopeId('')
    setDiscordForm(defaultDiscordForm)
    setNotice('')
    setPhase('login')
  }

  if (phase === 'loading') {
    return <main className="shell">Loading...</main>
  }

  return (
    <main className="shell">
      <section className="panel">
        <h1>BivLauncher Admin</h1>
        <p className="muted">API: {apiBaseUrl}</p>

        {phase === 'setup' && (
          <form className="form" onSubmit={onSetupSubmit}>
            <h2>First run setup</h2>
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
            <button disabled={busy}>{busy ? 'Creating...' : 'Create admin'}</button>
          </form>
        )}

        {phase === 'login' && (
          <form className="form" onSubmit={onLoginSubmit}>
            <h2>Admin login</h2>
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
            <button disabled={busy}>{busy ? 'Signing in...' : 'Sign in'}</button>
          </form>
        )}

        {phase === 'dashboard' && (
          <section className="dashboard">
            <div className="dashboard-header">
              <h2>Dashboard</h2>
              <button onClick={logout}>Logout</button>
            </div>
            <ul>
              <li>Profiles: {profiles.length}</li>
              <li>Servers: {servers.length}</li>
              <li>News: {newsItems.length}</li>
              <li>Bans: {bans.length}</li>
            </ul>

            <div className="grid-2">
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

            <div className="grid-2">
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

            <div className="grid-2">
              <section className="form form-small">
                <h3>Discord RPC</h3>
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

            <div className="grid-2">
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
                  placeholder="Source (manual/rss/json)"
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

            <div className="grid-2">
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
                  <button type="button" onClick={onSyncNewsSources} disabled={busy || !token}>
                    Sync news now
                  </button>
                </div>
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
                          enabled: {String(source.enabled)} | max: {source.maxItems}
                        </small>
                        <small>
                          last sync: {source.lastSyncAtUtc ? new Date(source.lastSyncAtUtc).toLocaleString() : 'never'}
                        </small>
                        <small>{source.lastSyncError || 'ok'}</small>
                      </span>
                      <div>
                        <button onClick={() => onNewsSourceEdit(source)}>Edit</button>
                        <button onClick={() => onSyncSingleNewsSource(source.id, source.name)}>Sync</button>
                        <button onClick={() => onNewsSourceDeleteLocal(source.id)}>Delete</button>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </div>

            <div className="grid-2">
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

            <div className="grid-2">
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

            <div className="grid-2">
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

            <div className="grid-2">
              <section className="form form-small">
                <h3>Auth Provider</h3>
                <input
                  placeholder="Login URL (external auth endpoint)"
                  value={authProviderSettings.loginUrl}
                  onChange={(event) =>
                    setAuthProviderSettings((prev) => ({ ...prev, loginUrl: event.target.value }))
                  }
                />
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
                    onChange={(event) =>
                      setAuthProviderSettings((prev) => ({ ...prev, allowDevFallback: event.target.checked }))
                    }
                  />
                  Allow dev fallback (local player auth when URL empty)
                </label>
                <button type="button" onClick={onSaveAuthProviderSettings} disabled={busy || !token}>
                  Save auth provider settings
                </button>
              </section>

              <section>
                <h3>Auth Provider Status</h3>
                <ul className="list">
                  <li>
                    <span className="list-text">
                      Login URL
                      <small>{authProviderSettings.loginUrl || '(empty)'}</small>
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
                </ul>
              </section>
            </div>

            <div className="grid-2">
              <section className="form form-small">
                <h3>S3 Storage</h3>
                <input
                  placeholder="Endpoint (http://localhost:9000)"
                  value={s3Settings.endpoint}
                  onChange={(event) => setS3Settings((prev) => ({ ...prev, endpoint: event.target.value }))}
                />
                <input
                  placeholder="Bucket"
                  value={s3Settings.bucket}
                  onChange={(event) => setS3Settings((prev) => ({ ...prev, bucket: event.target.value }))}
                />
                <div className="grid-inline">
                  <input
                    placeholder="Access key"
                    value={s3Settings.accessKey}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, accessKey: event.target.value }))}
                  />
                  <input
                    placeholder="Secret key"
                    value={s3Settings.secretKey}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, secretKey: event.target.value }))}
                  />
                </div>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={s3Settings.forcePathStyle}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, forcePathStyle: event.target.checked }))}
                  />
                  Force path style
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={s3Settings.useSsl}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, useSsl: event.target.checked }))}
                  />
                  Use SSL (https)
                </label>
                <label className="checkbox">
                  <input
                    type="checkbox"
                    checked={s3Settings.autoCreateBucket}
                    onChange={(event) => setS3Settings((prev) => ({ ...prev, autoCreateBucket: event.target.checked }))}
                  />
                  Auto create bucket
                </label>
                <button type="button" onClick={onSaveS3Settings} disabled={busy || !token}>
                  Save S3 settings
                </button>
              </section>

              <section>
                <h3>S3 Status</h3>
                <ul className="list">
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
                </ul>
              </section>
            </div>

            <div className="grid-2">
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
                <button type="button" onClick={onSaveBrandingSettings} disabled={busy || !token}>
                  Save branding settings
                </button>
              </section>

              <section>
                <h3>Branding Preview</h3>
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
                      Support / Logo
                      <small>
                        {brandingSettings.supportUrl} | {brandingSettings.logoText}
                      </small>
                    </span>
                  </li>
                </ul>
              </section>
            </div>

            <div className="grid-2">
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
          </section>
        )}

        {notice && <p className="notice">{notice}</p>}
        {error && <p className="error">{error}</p>}
      </section>
    </main>
  )
}

export default App
