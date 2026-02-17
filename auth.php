<?php
declare(strict_types=1);

/*
 * External auth endpoint for BivLauncher.
 *
 * Request (POST JSON):
 * {
 *   "username": "player",
 *   "password": "secret",
 *   "hwidHash": "optional"
 * }
 *
 * Response (success):
 * {
 *   "success": true,
 *   "externalId": "123",
 *   "username": "player",
 *   "roles": ["player"],
 *   "banned": false
 * }
 *
 * Response (error):
 * {
 *   "success": false,
 *   "error": "Invalid username or password."
 * }
 *
 * Configure DB and column names via environment variables:
 * AUTH_DB_DSN, AUTH_DB_USER, AUTH_DB_PASSWORD, AUTH_DB_TABLE,
 * AUTH_ID_COLUMN, AUTH_LOGIN_COLUMN, AUTH_USERNAME_COLUMN,
 * AUTH_PASSWORD_COLUMN, AUTH_ROLES_COLUMN, AUTH_BANNED_COLUMN,
 * AUTH_ACTIVE_COLUMN, AUTH_ACTIVE_VALUE, AUTH_PASSWORD_MODE,
 * AUTH_DEFAULT_ROLES, AUTH_REQUEST_LOGIN_FIELD, AUTH_REQUEST_PASSWORD_FIELD
 */

header('Content-Type: application/json; charset=utf-8');

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    jsonError(405, 'Method not allowed. Use POST.');
}

$config = [
    'dsn' => envValue('AUTH_DB_DSN', 'mysql:host=127.0.0.1;dbname=site;charset=utf8mb4'),
    'db_user' => envValue('AUTH_DB_USER', 'site_user'),
    'db_password' => envValue('AUTH_DB_PASSWORD', 'site_password'),
    'table' => envValue('AUTH_DB_TABLE', 'users'),
    'id_column' => envValue('AUTH_ID_COLUMN', 'id'),
    'login_column' => envValue('AUTH_LOGIN_COLUMN', 'username'),
    'username_column' => envValue('AUTH_USERNAME_COLUMN', 'username'),
    'password_column' => envValue('AUTH_PASSWORD_COLUMN', 'password_hash'),
    'roles_column' => envValue('AUTH_ROLES_COLUMN', 'roles'),
    'banned_column' => envValue('AUTH_BANNED_COLUMN', 'is_banned'),
    'active_column' => trim(envValue('AUTH_ACTIVE_COLUMN', '')),
    'active_value' => envValue('AUTH_ACTIVE_VALUE', '1'),
    'password_mode' => strtolower(trim(envValue('AUTH_PASSWORD_MODE', 'password_hash'))),
    'default_roles' => envValue('AUTH_DEFAULT_ROLES', 'player'),
    'request_login_field' => trim(envValue('AUTH_REQUEST_LOGIN_FIELD', 'username')),
    'request_password_field' => trim(envValue('AUTH_REQUEST_PASSWORD_FIELD', 'password')),
];

$table = safeIdentifier($config['table']);
$idColumn = safeIdentifier($config['id_column']);
$loginColumn = safeIdentifier($config['login_column']);
$usernameColumn = safeIdentifier($config['username_column']);
$passwordColumn = safeIdentifier($config['password_column']);
$rolesColumn = trim($config['roles_column']) === '' ? '' : safeIdentifier($config['roles_column']);
$bannedColumn = trim($config['banned_column']) === '' ? '' : safeIdentifier($config['banned_column']);
$activeColumn = trim($config['active_column']) === '' ? '' : safeIdentifier($config['active_column']);

$input = readJsonBody();
$username = getStringField(
    $input,
    [
        $config['request_login_field'],
        'username',
        'login',
        'email',
        'user',
    ]
);
$password = getStringField(
    $input,
    [
        $config['request_password_field'],
        'password',
        'pass',
    ]
);

if ($username === '' || $password === '') {
    jsonError(422, 'Username and password are required.');
}

$rolesSelect = $rolesColumn !== '' ? $rolesColumn : "''";
$bannedSelect = $bannedColumn !== '' ? $bannedColumn : '0';

$sql = "SELECT {$idColumn} AS external_id, {$usernameColumn} AS username, {$passwordColumn} AS password_hash, {$rolesSelect} AS roles_raw, {$bannedSelect} AS banned_raw FROM {$table} WHERE {$loginColumn} = :login";
if ($activeColumn !== '') {
    $sql .= " AND {$activeColumn} = :active";
}
$sql .= ' LIMIT 1';

try {
    $pdo = new PDO(
        $config['dsn'],
        $config['db_user'],
        $config['db_password'],
        [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES => false,
        ]
    );

    $statement = $pdo->prepare($sql);
    $statement->bindValue(':login', $username, PDO::PARAM_STR);
    if ($activeColumn !== '') {
        $statement->bindValue(':active', $config['active_value'], PDO::PARAM_STR);
    }
    $statement->execute();

    $user = $statement->fetch();
    if (!is_array($user)) {
        jsonError(401, 'Invalid username or password.');
    }

    $storedHash = (string)($user['password_hash'] ?? '');
    if ($storedHash === '' || !verifyPassword($password, $storedHash, $config['password_mode'])) {
        jsonError(401, 'Invalid username or password.');
    }

    $resolvedUsername = trim((string)($user['username'] ?? $username));
    if ($resolvedUsername === '') {
        $resolvedUsername = $username;
    }

    $externalId = trim((string)($user['external_id'] ?? $resolvedUsername));
    if ($externalId === '') {
        $externalId = $resolvedUsername;
    }

    $roles = normalizeRoles($user['roles_raw'] ?? null, $config['default_roles']);
    $banned = normalizeBool($user['banned_raw'] ?? false);

    echo json_encode(
        [
            'success' => true,
            'externalId' => $externalId,
            'username' => $resolvedUsername,
            'roles' => $roles,
            'banned' => $banned,
        ],
        JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES
    );
} catch (PDOException) {
    jsonError(500, 'Database error.');
} catch (Throwable) {
    jsonError(500, 'Unexpected server error.');
}

function envValue(string $name, string $default): string
{
    $raw = getenv($name);
    if ($raw === false) {
        return $default;
    }

    $value = trim((string)$raw);
    return $value === '' ? $default : $value;
}

function readJsonBody(): array
{
    $raw = file_get_contents('php://input');
    if ($raw === false || trim($raw) === '') {
        jsonError(400, 'Request body is empty.');
    }

    $decoded = json_decode($raw, true);
    if (!is_array($decoded)) {
        jsonError(400, 'Request body must be valid JSON.');
    }

    return $decoded;
}

function getStringField(array $payload, array $keys): string
{
    foreach ($keys as $key) {
        $normalizedKey = trim((string)$key);
        if ($normalizedKey === '' || !array_key_exists($normalizedKey, $payload)) {
            continue;
        }

        $value = trim((string)$payload[$normalizedKey]);
        if ($value !== '') {
            return $value;
        }
    }

    return '';
}

function verifyPassword(string $password, string $storedHash, string $mode): bool
{
    switch ($mode) {
        case 'password_hash':
            return password_verify($password, $storedHash);
        case 'md5':
            return hash_equals(strtolower($storedHash), md5($password));
        case 'sha1':
            return hash_equals(strtolower($storedHash), sha1($password));
        case 'plain':
            return hash_equals($storedHash, $password);
        default:
            return false;
    }
}

function normalizeRoles($rolesRaw, string $defaultRolesCsv): array
{
    $roles = [];

    if (is_array($rolesRaw)) {
        $roles = $rolesRaw;
    } elseif (is_string($rolesRaw)) {
        $raw = trim($rolesRaw);
        if ($raw !== '') {
            if (startsWith($raw, '[') && endsWith($raw, ']')) {
                $decoded = json_decode($raw, true);
                if (is_array($decoded)) {
                    $roles = $decoded;
                } else {
                    $roles = preg_split('/[,\s]+/', $raw) ?: [];
                }
            } else {
                $roles = preg_split('/[,\s]+/', $raw) ?: [];
            }
        }
    }

    if (count($roles) === 0) {
        $roles = preg_split('/[,\s]+/', trim($defaultRolesCsv)) ?: [];
    }

    $normalized = [];
    foreach ($roles as $role) {
        $value = trim((string)$role);
        if ($value === '') {
            continue;
        }
        $normalized[strtolower($value)] = $value;
    }

    if (count($normalized) === 0) {
        return ['player'];
    }

    return array_values($normalized);
}

function normalizeBool($value): bool
{
    if (is_bool($value)) {
        return $value;
    }

    if (is_int($value) || is_float($value)) {
        return (int)$value !== 0;
    }

    $normalized = strtolower(trim((string)$value));
    return in_array($normalized, ['1', 'true', 'yes', 'y', 'on', 'banned'], true);
}

function safeIdentifier(string $name): string
{
    $identifier = trim($name);
    if (!preg_match('/^[A-Za-z_][A-Za-z0-9_]*$/', $identifier)) {
        jsonError(500, 'Unsafe SQL identifier in auth.php config.');
    }

    return $identifier;
}

function jsonError(int $statusCode, string $message)
{
    http_response_code($statusCode);
    echo json_encode(
        [
            'success' => false,
            'error' => $message,
        ],
        JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES
    );
    exit;
}

function startsWith(string $haystack, string $needle): bool
{
    if ($needle === '') {
        return true;
    }

    return substr($haystack, 0, strlen($needle)) === $needle;
}

function endsWith(string $haystack, string $needle): bool
{
    if ($needle === '') {
        return true;
    }

    $needleLength = strlen($needle);
    if ($needleLength > strlen($haystack)) {
        return false;
    }

    return substr($haystack, -$needleLength) === $needle;
}
