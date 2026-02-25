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
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class LegacyBridge {
    private static final String DEFAULT_AUTH_BASE = "https://authserver.mojang.com";
    private static final String DEFAULT_SESSION_BASE = "https://sessionserver.mojang.com";
    private static final String YGGDRASIL_PROPERTY = "biv.auth.yggdrasil";
    private static final String YGGDRASIL_PROPERTY_FALLBACK = "authlibinjector.yggdrasil";

    private static final Pattern TOKEN_PATTERN = Pattern.compile("(?:^|:)token:([^:]+)(?::([0-9a-fA-F]{32}))?$", Pattern.CASE_INSENSITIVE);
    private static final Pattern UUID_PATTERN = Pattern.compile("[0-9a-fA-F]{32}");
    private static final Pattern STRICT_UUID_PATTERN = Pattern.compile("^[0-9a-fA-F]{32}$");
    private static final Pattern JWT_SEGMENT_PATTERN = Pattern.compile("^[A-Za-z0-9_-]+$");
    private static final Pattern USERNAME_PATTERN = Pattern.compile("^[A-Za-z0-9_]{2,16}$");
    private static final Pattern IPV4_PATTERN = Pattern.compile("^\\d{1,3}(?:\\.\\d{1,3}){3}$");
    private static final String DEBUG_PROPERTY = "biv.auth.debug";

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

    public static String joinServer(String first, String second, String third) {
        try {
            debug("joinServer args: first='" + nullToEmpty(first) + "', second='" + nullToEmpty(second) + "', third='" + nullToEmpty(third) + "'");
            String authBase = resolveAuthBase();
            if (authBase.isEmpty()) {
                debug("joinServer auth base is empty");
                return "Bad login";
            }

            List<String> tokenCandidates = collectTokenCandidates(
                first,
                second,
                third,
                getAccessToken(),
                getSessionId(),
                getProp("biv.auth.token"),
                getProp("biv.auth.session"));
            if (tokenCandidates.isEmpty()) {
                debug("joinServer token candidates are empty");
                return "Bad login";
            }

            List<String> profileIdCandidates = collectProfileIdCandidates(
                first,
                second,
                third,
                getProp("biv.auth.uuid"));
            if (profileIdCandidates.isEmpty()) {
                profileIdCandidates.add("");
            }

            List<String> serverIdCandidates = collectServerIdCandidates(first, second, third, tokenCandidates);
            if (serverIdCandidates.isEmpty()) {
                addIfNotEmpty(serverIdCandidates, third);
                addIfNotEmpty(serverIdCandidates, second);
                addIfNotEmpty(serverIdCandidates, first);
            }

            debug("joinServer authBase=" + authBase + ", tokenCandidates=" + tokenCandidates + ", profileIdCandidates=" + profileIdCandidates + ", serverIdCandidates=" + serverIdCandidates);

            for (String token : tokenCandidates) {
                for (String serverId : serverIdCandidates) {
                    if (nullToEmpty(token).isEmpty() || nullToEmpty(serverId).isEmpty()) {
                        continue;
                    }

                    for (String profileId : profileIdCandidates) {
                        String payload = "{\"accessToken\":\"" + esc(token) + "\",\"selectedProfile\":\"" + esc(profileId) + "\",\"serverId\":\"" + esc(serverId) + "\"}";
                        int status = postJson(authBase + "/session/minecraft/join", payload);
                        debug("joinServer POST /session/minecraft/join tokenLen=" + nullToEmpty(token).length() + ", serverId='" + serverId + "', profileId='" + nullToEmpty(profileId) + "', status=" + status);
                        if (status >= 200 && status < 300) {
                            return "OK";
                        }
                    }
                }
            }

            debug("joinServer all attempts failed");
            return "Bad login";
        } catch (Exception ex) {
            debug("joinServer exception: " + ex.getClass().getSimpleName() + ": " + nullToEmpty(ex.getMessage()));
            return "Bad login";
        }
    }

    public static boolean checkServer(String first, String second) {
        return "YES".equalsIgnoreCase(checkServer(first, second, ""));
    }

    public static String checkServer(String first, String second, String third) {
        try {
            debug("checkServer args: first='" + nullToEmpty(first) + "', second='" + nullToEmpty(second) + "', third='" + nullToEmpty(third) + "'");
            String sessionBase = resolveSessionBase();
            String authBase = resolveAuthBase();
            if (sessionBase.isEmpty()) {
                debug("checkServer session base is empty");
                return "NO";
            }

            List<String> tokenCandidates = collectTokenCandidates(
                first,
                second,
                third,
                getAccessToken(),
                getSessionId(),
                getProp("biv.auth.token"),
                getProp("biv.auth.session"));
            List<String> serverIdCandidates = collectServerIdCandidates(first, second, third, tokenCandidates);
            if (serverIdCandidates.isEmpty()) {
                addIfNotEmpty(serverIdCandidates, second);
                addIfNotEmpty(serverIdCandidates, first);
                addIfNotEmpty(serverIdCandidates, third);
            }
            List<String> profileIdCandidates = collectProfileIdCandidates(
                first,
                second,
                third,
                getProp("biv.auth.uuid"));
            if (profileIdCandidates.isEmpty()) {
                profileIdCandidates.add("");
            }

            List<String> usernameCandidates = collectUsernameCandidates(
                first,
                second,
                third,
                getUsername(),
                serverIdCandidates,
                tokenCandidates);
            if (usernameCandidates.isEmpty()) {
                addIfNotEmpty(usernameCandidates, getUsername());
                addIfNotEmpty(usernameCandidates, first);
                addIfNotEmpty(usernameCandidates, second);
                addIfNotEmpty(usernameCandidates, third);
            }

            String ip = looksLikeIpAddress(third) ? nullToEmpty(third) : "";

            debug(
                "checkServer authBase=" + authBase +
                ", sessionBase=" + sessionBase +
                ", usernameCandidates=" + usernameCandidates +
                ", serverIdCandidates=" + serverIdCandidates +
                ", tokenCandidates=" + tokenCandidates +
                ", profileIdCandidates=" + profileIdCandidates +
                ", ip='" + ip + "'");

            if (!authBase.isEmpty()) {
                // Legacy clients may skip explicit join call. Do it here
                // using launcher-provided token properties before hasJoined/check.
                tryJoinCandidates(authBase, tokenCandidates, serverIdCandidates, profileIdCandidates);
            }

            for (String username : usernameCandidates) {
                for (String serverId : serverIdCandidates) {
                    if (nullToEmpty(username).isEmpty() || nullToEmpty(serverId).isEmpty()) {
                        continue;
                    }

                    if (queryHasJoined(sessionBase, username, serverId, ip) ||
                        queryLegacyCheckServer(sessionBase, username, serverId, ip)) {
                        return "YES";
                    }
                }
            }

            List<String> rawCandidates = collectDistinctNonEmpty(first, second, third, getUsername());
            for (String username : rawCandidates) {
                if (!isLikelyUsername(username)) {
                    continue;
                }

                for (String serverId : rawCandidates) {
                    if (username.equals(serverId)) {
                        continue;
                    }

                    if (queryHasJoined(sessionBase, username, serverId, ip) ||
                        queryLegacyCheckServer(sessionBase, username, serverId, ip)) {
                        return "YES";
                    }
                }
            }

            debug("checkServer all attempts failed");
            return "NO";
        } catch (Exception ex) {
            debug("checkServer exception: " + ex.getClass().getSimpleName() + ": " + nullToEmpty(ex.getMessage()));
            return "NO";
        }
    }

    private static void tryJoinCandidates(
        String authBase,
        List<String> tokenCandidates,
        List<String> serverIdCandidates,
        List<String> profileIdCandidates) {
        for (String token : tokenCandidates) {
            for (String serverId : serverIdCandidates) {
                if (nullToEmpty(token).isEmpty() || nullToEmpty(serverId).isEmpty()) {
                    continue;
                }

                for (String profileId : profileIdCandidates) {
                    try {
                        String payload = "{\"accessToken\":\"" + esc(token) + "\",\"selectedProfile\":\"" + esc(profileId) + "\",\"serverId\":\"" + esc(serverId) + "\"}";
                        int status = postJson(authBase + "/session/minecraft/join", payload);
                        debug("checkServer pre-join tokenLen=" + nullToEmpty(token).length() + ", serverId='" + serverId + "', profileId='" + nullToEmpty(profileId) + "', status=" + status);
                    } catch (Exception ex) {
                        debug("checkServer pre-join exception: " + ex.getClass().getSimpleName() + ": " + nullToEmpty(ex.getMessage()));
                    }
                }
            }
        }
    }

    private static boolean queryHasJoined(String sessionBase, String username, String serverId, String ip) {
        try {
            StringBuilder query = new StringBuilder(sessionBase)
                .append("/session/minecraft/hasJoined?username=")
                .append(URLEncoder.encode(nullToEmpty(username), "UTF-8"))
                .append("&serverId=")
                .append(URLEncoder.encode(nullToEmpty(serverId), "UTF-8"));

            if (!nullToEmpty(ip).isEmpty()) {
                query.append("&ip=").append(URLEncoder.encode(ip.trim(), "UTF-8"));
            }

            HttpURLConnection connection = (HttpURLConnection) new URL(query.toString()).openConnection();
            connection.setConnectTimeout(5000);
            connection.setReadTimeout(5000);
            connection.setRequestMethod("GET");
            connection.setUseCaches(false);

            if (connection.getResponseCode() != 200) {
                debug("queryHasJoined non-200: url=" + query + ", status=" + connection.getResponseCode());
                return false;
            }

            String body = readBody(connection.getInputStream());
            boolean ok = body.contains("\"id\"") && body.contains("\"name\"");
            debug("queryHasJoined status=200, url=" + query + ", ok=" + ok);
            return ok;
        } catch (Exception ex) {
            debug("queryHasJoined exception: " + ex.getClass().getSimpleName() + ": " + nullToEmpty(ex.getMessage()));
            return false;
        }
    }

    private static boolean queryLegacyCheckServer(String sessionBase, String username, String serverId, String ip) {
        return queryLegacyCheckServerPath(sessionBase, "/checkserver.jsp", username, serverId, ip) ||
               queryLegacyCheckServerPath(sessionBase, "/game/checkserver.jsp", username, serverId, ip);
    }

    private static boolean queryLegacyCheckServerPath(String sessionBase, String path, String username, String serverId, String ip) {
        try {
            StringBuilder query = new StringBuilder(sessionBase)
                .append(path)
                .append("?user=")
                .append(URLEncoder.encode(nullToEmpty(username), "UTF-8"))
                .append("&serverId=")
                .append(URLEncoder.encode(nullToEmpty(serverId), "UTF-8"));

            if (!nullToEmpty(ip).isEmpty()) {
                query.append("&ip=").append(URLEncoder.encode(ip.trim(), "UTF-8"));
            }

            HttpURLConnection connection = (HttpURLConnection) new URL(query.toString()).openConnection();
            connection.setConnectTimeout(5000);
            connection.setReadTimeout(5000);
            connection.setRequestMethod("GET");
            connection.setUseCaches(false);

            int status = connection.getResponseCode();
            if (status != 200) {
                debug("queryLegacyCheck non-200: url=" + query + ", status=" + status);
                return false;
            }

            String body = readBody(connection.getInputStream());
            String normalizedBody = nullToEmpty(body);
            boolean ok = "YES".equalsIgnoreCase(normalizedBody) || (body.contains("\"id\"") && body.contains("\"name\""));
            debug("queryLegacyCheck status=200, url=" + query + ", body='" + normalizedBody + "', ok=" + ok);
            return ok;
        } catch (Exception ex) {
            debug("queryLegacyCheck exception: " + ex.getClass().getSimpleName() + ": " + nullToEmpty(ex.getMessage()));
            return false;
        }
    }

    private static List<String> collectTokenCandidates(
        String first,
        String second,
        String third,
        String fourth,
        String fifth,
        String sixth,
        String seventh) {
        Set<String> result = new LinkedHashSet<String>();
        addTokenCandidates(result, first);
        addTokenCandidates(result, second);
        addTokenCandidates(result, third);
        addTokenCandidates(result, fourth);
        addTokenCandidates(result, fifth);
        addTokenCandidates(result, sixth);
        addTokenCandidates(result, seventh);
        return new ArrayList<String>(result);
    }

    private static void addTokenCandidates(Set<String> result, String raw) {
        String value = nullToEmpty(raw);
        if (value.isEmpty()) {
            return;
        }

        if (value.regionMatches(true, 0, "token:", 0, "token:".length())) {
            String[] parts = value.split(":");
            for (int i = 1; i < parts.length; i++) {
                String part = nullToEmpty(parts[i]);
                if (looksLikeJwt(part)) {
                    result.add(part);
                }
            }

            for (int i = 1; i < parts.length; i++) {
                String part = nullToEmpty(parts[i]);
                if (isLikelyToken(part)) {
                    result.add(part);
                }
            }
        }

        if (looksLikeJwt(value)) {
            result.add(value);
        }

        String extracted = extractToken(value);
        if (isLikelyToken(extracted)) {
            result.add(extracted);
        }
    }

    private static List<String> collectProfileIdCandidates(String first, String second, String third, String fourth) {
        Set<String> result = new LinkedHashSet<String>();
        addProfileIdCandidates(result, first);
        addProfileIdCandidates(result, second);
        addProfileIdCandidates(result, third);
        addProfileIdCandidates(result, fourth);
        return new ArrayList<String>(result);
    }

    private static void addProfileIdCandidates(Set<String> result, String raw) {
        String normalized = normalizeUuid(raw);
        if (!normalized.isEmpty()) {
            result.add(normalized);
        }

        String value = nullToEmpty(raw);
        if (!value.regionMatches(true, 0, "token:", 0, "token:".length())) {
            return;
        }

        String[] parts = value.split(":");
        for (int i = 1; i < parts.length; i++) {
            String part = normalizeUuid(parts[i]);
            if (!part.isEmpty()) {
                result.add(part);
            }
        }
    }

    private static List<String> collectServerIdCandidates(String first, String second, String third, List<String> tokenCandidates) {
        Set<String> blocked = new LinkedHashSet<String>();
        for (String token : tokenCandidates) {
            if (!nullToEmpty(token).isEmpty()) {
                blocked.add(token);
            }
        }

        Set<String> result = new LinkedHashSet<String>();
        addServerIdCandidate(result, blocked, first);
        addServerIdCandidate(result, blocked, second);
        addServerIdCandidate(result, blocked, third);
        return new ArrayList<String>(result);
    }

    private static void addServerIdCandidate(Set<String> result, Set<String> blocked, String raw) {
        String value = nullToEmpty(raw);
        if (value.isEmpty()) {
            return;
        }

        if (blocked.contains(value)) {
            return;
        }

        if (value.regionMatches(true, 0, "token:", 0, "token:".length())) {
            return;
        }

        if (looksLikeJwt(value) || looksLikeUuid(value) || looksLikeIpAddress(value) || isLikelyUsername(value)) {
            return;
        }

        result.add(value);
    }

    private static List<String> collectUsernameCandidates(
        String first,
        String second,
        String third,
        String fallback,
        List<String> serverIdCandidates,
        List<String> tokenCandidates) {
        Set<String> blocked = new LinkedHashSet<String>();
        for (String value : serverIdCandidates) {
            if (!nullToEmpty(value).isEmpty()) {
                blocked.add(value);
            }
        }

        for (String value : tokenCandidates) {
            if (!nullToEmpty(value).isEmpty()) {
                blocked.add(value);
            }
        }

        Set<String> result = new LinkedHashSet<String>();
        addUsernameCandidate(result, blocked, fallback);
        addUsernameCandidate(result, blocked, first);
        addUsernameCandidate(result, blocked, second);
        addUsernameCandidate(result, blocked, third);

        if (!result.isEmpty()) {
            return new ArrayList<String>(result);
        }

        addFallbackUsername(result, blocked, first);
        addFallbackUsername(result, blocked, second);
        addFallbackUsername(result, blocked, third);
        return new ArrayList<String>(result);
    }

    private static List<String> collectDistinctNonEmpty(String first, String second, String third, String fourth) {
        Set<String> result = new LinkedHashSet<String>();
        addIfNotEmpty(result, first);
        addIfNotEmpty(result, second);
        addIfNotEmpty(result, third);
        addIfNotEmpty(result, fourth);
        return new ArrayList<String>(result);
    }

    private static void addUsernameCandidate(Set<String> result, Set<String> blocked, String raw) {
        String value = nullToEmpty(raw);
        if (value.isEmpty() || blocked.contains(value)) {
            return;
        }

        if (isLikelyUsername(value)) {
            result.add(value);
        }
    }

    private static void addFallbackUsername(Set<String> result, Set<String> blocked, String raw) {
        String value = nullToEmpty(raw);
        if (value.isEmpty() || blocked.contains(value)) {
            return;
        }

        if (value.regionMatches(true, 0, "token:", 0, "token:".length())) {
            return;
        }

        if (looksLikeJwt(value) || looksLikeUuid(value) || looksLikeIpAddress(value)) {
            return;
        }

        result.add(value);
    }

    private static boolean isLikelyToken(String value) {
        String normalized = nullToEmpty(value);
        if (normalized.isEmpty()) {
            return false;
        }

        if (looksLikeJwt(normalized)) {
            return true;
        }

        if (looksLikeUuid(normalized) || looksLikeIpAddress(normalized) || isLikelyUsername(normalized)) {
            return false;
        }

        return normalized.length() >= 16;
    }

    private static boolean isLikelyUsername(String value) {
        String normalized = nullToEmpty(value);
        return !normalized.isEmpty() && USERNAME_PATTERN.matcher(normalized).matches();
    }

    private static boolean looksLikeJwt(String value) {
        String normalized = nullToEmpty(value);
        if (normalized.isEmpty()) {
            return false;
        }

        String[] parts = normalized.split("\\.");
        if (parts.length != 3) {
            return false;
        }

        for (String part : parts) {
            if (part == null || part.isEmpty() || !JWT_SEGMENT_PATTERN.matcher(part).matches()) {
                return false;
            }
        }

        return true;
    }

    private static boolean looksLikeUuid(String value) {
        String normalized = nullToEmpty(value).replace("-", "");
        return STRICT_UUID_PATTERN.matcher(normalized).matches();
    }

    private static boolean looksLikeIpAddress(String value) {
        String normalized = nullToEmpty(value);
        if (normalized.isEmpty() || !IPV4_PATTERN.matcher(normalized).matches()) {
            return false;
        }

        String[] parts = normalized.split("\\.");
        if (parts.length != 4) {
            return false;
        }

        for (String part : parts) {
            try {
                int octet = Integer.parseInt(part);
                if (octet < 0 || octet > 255) {
                    return false;
                }
            } catch (NumberFormatException ex) {
                return false;
            }
        }

        return true;
    }

    private static String extractToken(String raw) {
        if (raw == null) {
            return "";
        }

        String value = raw.trim();
        if (value.isEmpty()) {
            return "";
        }

        if (value.regionMatches(true, 0, "token:", 0, "token:".length())) {
            String[] parts = value.split(":");
            for (int i = 1; i < parts.length; i++) {
                String part = nullToEmpty(parts[i]);
                if (looksLikeJwt(part)) {
                    return part;
                }
            }

            if (parts.length >= 2) {
                return nullToEmpty(parts[1]);
            }
        }

        Matcher matcher = TOKEN_PATTERN.matcher(value);
        if (matcher.find()) {
            return nullToEmpty(matcher.group(1));
        }

        return value;
    }

    private static String normalizeUuid(String value) {
        String normalized = nullToEmpty(value).replace("-", "").trim();
        if (normalized.isEmpty()) {
            return "";
        }

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
            debug("resolveYggdrasilBase from property " + YGGDRASIL_PROPERTY + ": " + fromProperty);
            return fromProperty;
        }

        String fromFallbackProperty = normalizeYggdrasilBase(getProp(YGGDRASIL_PROPERTY_FALLBACK));
        if (!fromFallbackProperty.isEmpty()) {
            debug("resolveYggdrasilBase from property " + YGGDRASIL_PROPERTY_FALLBACK + ": " + fromFallbackProperty);
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

                String candidate = normalizeYggdrasilBase(normalizedArgument.substring(equalsIndex + 1));
                if (!candidate.isEmpty()) {
                    debug("resolveYggdrasilBase from javaagent arg: " + candidate);
                    return candidate;
                }
            }
        } catch (Throwable ignored) {
        }

        debug("resolveYggdrasilBase: empty");
        return "";
    }

    private static String normalizeYggdrasilBase(String raw) {
        String value = nullToEmpty(raw);
        if (value.isEmpty()) {
            return "";
        }

        if ((value.startsWith("\"") && value.endsWith("\"")) ||
            (value.startsWith("'") && value.endsWith("'"))) {
            value = nullToEmpty(value.substring(1, value.length() - 1));
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

    private static void addIfNotEmpty(List<String> values, String raw) {
        String normalized = nullToEmpty(raw);
        if (!normalized.isEmpty() && !values.contains(normalized)) {
            values.add(normalized);
        }
    }

    private static void addIfNotEmpty(Set<String> values, String raw) {
        String normalized = nullToEmpty(raw);
        if (!normalized.isEmpty()) {
            values.add(normalized);
        }
    }

    private static void debug(String message) {
        if (!Boolean.parseBoolean(System.getProperty(DEBUG_PROPERTY, "false"))) {
            return;
        }

        System.out.println("[BivLegacyBridge] " + nullToEmpty(message));
    }
}
