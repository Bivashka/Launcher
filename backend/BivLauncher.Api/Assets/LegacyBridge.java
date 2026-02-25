package com.mojang.authlib.yggdrasil;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.lang.management.ManagementFactory;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class LegacyBridge {
    private static final String DEFAULT_AUTH_BASE = "https://authserver.mojang.com";
    private static final String DEFAULT_SESSION_BASE = "https://sessionserver.mojang.com";
    private static final String YGGDRASIL_PROPERTY = "biv.auth.yggdrasil";
    private static final String YGGDRASIL_PROPERTY_FALLBACK = "authlibinjector.yggdrasil";
    private static final Pattern TOKEN_PATTERN = Pattern.compile("(?:^|:)token:([^:]+):?([0-9a-fA-F]{32})?$", Pattern.CASE_INSENSITIVE);
    private static final Pattern UUID_PATTERN = Pattern.compile("[0-9a-fA-F]{32}");

    private LegacyBridge() {
    }

    public static String getUsername() {
        return getProp("biv.auth.username");
    }

    public static String getAccessToken() {
        return getProp("biv.auth.token");
    }

    public static String getSessionId() {
        return getProp("biv.auth.session");
    }

    public static String getSkinURL(String username) {
        return "";
    }

    public static String getCloakURL(String username) {
        return "";
    }

    public static String getServerUrl() {
        return resolveAuthBase();
    }

    public static String joinServer(String username, String sessionId, String serverId) {
        try {
            String authBase = resolveAuthBase();
            if (authBase.isEmpty()) {
                return "Bad login";
            }

            String token = extractToken(sessionId);
            if (token.isEmpty()) {
                token = getAccessToken();
            }
            if (token.isEmpty()) {
                return "Bad login";
            }

            String profileId = extractProfileId(sessionId);
            if (profileId.isEmpty()) {
                profileId = normalizeUuid(getProp("biv.auth.uuid"));
            }

            String payload = "{\"accessToken\":\"" + esc(token) + "\",\"selectedProfile\":\"" + esc(profileId) + "\",\"serverId\":\"" + esc(serverId) + "\"}";
            int status = postJson(authBase + "/session/minecraft/join", payload);
            return status >= 200 && status < 300 ? "OK" : "Bad login";
        } catch (Exception ex) {
            return "Bad login";
        }
    }

    public static boolean checkServer(String username, String serverId) {
        return "YES".equalsIgnoreCase(checkServer(username, serverId, ""));
    }

    public static String checkServer(String username, String serverId, String ip) {
        try {
            String sessionBase = resolveSessionBase();
            if (sessionBase.isEmpty()) {
                return "NO";
            }

            StringBuilder query = new StringBuilder(sessionBase)
                .append("/session/minecraft/hasJoined?username=")
                .append(URLEncoder.encode(nullToEmpty(username), "UTF-8"))
                .append("&serverId=")
                .append(URLEncoder.encode(nullToEmpty(serverId), "UTF-8"));

            if (ip != null && !ip.trim().isEmpty()) {
                query.append("&ip=").append(URLEncoder.encode(ip.trim(), "UTF-8"));
            }

            HttpURLConnection connection = (HttpURLConnection) new URL(query.toString()).openConnection();
            connection.setConnectTimeout(5000);
            connection.setReadTimeout(5000);
            connection.setRequestMethod("GET");
            connection.setUseCaches(false);

            if (connection.getResponseCode() != 200) {
                return "NO";
            }

            String body = readBody(connection.getInputStream());
            return body.contains("\"id\"") && body.contains("\"name\"") ? "YES" : "NO";
        } catch (Exception ex) {
            return "NO";
        }
    }

    private static String extractToken(String raw) {
        if (raw == null) {
            return "";
        }

        String value = raw.trim();
        if (value.isEmpty()) {
            return "";
        }

        Matcher matcher = TOKEN_PATTERN.matcher(value);
        if (matcher.find()) {
            return nullToEmpty(matcher.group(1));
        }

        if (value.startsWith("token:")) {
            String[] parts = value.split(":");
            if (parts.length >= 2) {
                return nullToEmpty(parts[1]);
            }
        }

        return value;
    }

    private static String extractProfileId(String raw) {
        if (raw == null) {
            return "";
        }

        Matcher matcher = UUID_PATTERN.matcher(raw);
        if (matcher.find()) {
            return normalizeUuid(matcher.group());
        }

        return "";
    }

    private static String normalizeUuid(String value) {
        String normalized = nullToEmpty(value).replace("-", "").trim();
        Matcher matcher = UUID_PATTERN.matcher(normalized);
        if (!matcher.find()) {
            return "";
        }

        return matcher.group().toLowerCase();
    }

    private static String resolveAuthBase() {
        String yggdrasilBase = resolveYggdrasilBase();
        if (yggdrasilBase.isEmpty()) {
            return DEFAULT_AUTH_BASE;
        }

        String lower = yggdrasilBase.toLowerCase();
        if (lower.endsWith("/authserver")) {
            return yggdrasilBase;
        }

        return yggdrasilBase + "/authserver";
    }

    private static String resolveSessionBase() {
        String yggdrasilBase = resolveYggdrasilBase();
        if (yggdrasilBase.isEmpty()) {
            return DEFAULT_SESSION_BASE;
        }

        String lower = yggdrasilBase.toLowerCase();
        if (lower.endsWith("/sessionserver")) {
            return yggdrasilBase;
        }

        return yggdrasilBase + "/sessionserver";
    }

    private static String resolveYggdrasilBase() {
        String fromProperty = normalizeYggdrasilBase(getProp(YGGDRASIL_PROPERTY));
        if (!fromProperty.isEmpty()) {
            return fromProperty;
        }

        String fromFallbackProperty = normalizeYggdrasilBase(getProp(YGGDRASIL_PROPERTY_FALLBACK));
        if (!fromFallbackProperty.isEmpty()) {
            return fromFallbackProperty;
        }

        try {
            List<String> inputArguments = ManagementFactory.getRuntimeMXBean().getInputArguments();
            for (String argument : inputArguments) {
                String normalizedArgument = nullToEmpty(argument);
                if (!normalizedArgument.startsWith("-javaagent:")) {
                    continue;
                }

                int equalsIndex = normalizedArgument.indexOf('=');
                if (equalsIndex <= "-javaagent:".length() || equalsIndex >= normalizedArgument.length() - 1) {
                    continue;
                }

                String agentPath = normalizedArgument.substring("-javaagent:".length(), equalsIndex).toLowerCase();
                if (!agentPath.contains("authlib") && !agentPath.contains("launcher")) {
                    continue;
                }

                String candidate = normalizeYggdrasilBase(normalizedArgument.substring(equalsIndex + 1));
                if (!candidate.isEmpty()) {
                    return candidate;
                }
            }
        } catch (Throwable ignored) {
        }

        return "";
    }

    private static String normalizeYggdrasilBase(String raw) {
        String value = nullToEmpty(raw);
        if (value.isEmpty()) {
            return "";
        }

        while (value.endsWith("/")) {
            value = value.substring(0, value.length() - 1).trim();
        }

        if (value.isEmpty()) {
            return "";
        }

        String lower = value.toLowerCase();
        if (!lower.startsWith("https://") && !lower.startsWith("http://")) {
            return "";
        }

        if (lower.endsWith("/authserver")) {
            return value.substring(0, value.length() - "/authserver".length());
        }

        if (lower.endsWith("/sessionserver")) {
            return value.substring(0, value.length() - "/sessionserver".length());
        }

        return value;
    }

    private static int postJson(String url, String json) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
        connection.setConnectTimeout(5000);
        connection.setReadTimeout(5000);
        connection.setRequestMethod("POST");
        connection.setUseCaches(false);
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");

        byte[] payload = json.getBytes(StandardCharsets.UTF_8);
        connection.setFixedLengthStreamingMode(payload.length);

        try (OutputStream output = connection.getOutputStream()) {
            output.write(payload);
            output.flush();
        }

        int status = connection.getResponseCode();
        InputStream stream = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
        if (stream != null) {
            try (InputStream ignored = stream) {
                readBody(ignored);
            }
        }

        return status;
    }

    private static String readBody(InputStream input) throws Exception {
        BufferedReader reader = new BufferedReader(new InputStreamReader(input, StandardCharsets.UTF_8));
        StringBuilder body = new StringBuilder();
        String line;
        while ((line = reader.readLine()) != null) {
            body.append(line);
        }
        return body.toString();
    }

    private static String getProp(String key) {
        return nullToEmpty(System.getProperty(key, ""));
    }

    private static String nullToEmpty(String value) {
        return value == null ? "" : value.trim();
    }

    private static String esc(String value) {
        return nullToEmpty(value).replace("\\", "\\\\").replace("\"", "\\\"");
    }
}
