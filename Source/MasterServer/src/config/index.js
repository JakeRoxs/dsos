const path = require("node:path");
const fs = require("node:fs");

const raw = fs.readFileSync(path.join(__dirname, "config.json"), "utf8");
const fileConfig = JSON.parse(raw);

function parsePositiveInt(envVar, fallback) {
    if (!envVar) return fallback;
    const parsed = Number.parseInt(envVar, 10);
    if (Number.isNaN(parsed) || parsed <= 0) return fallback;
    return parsed;
}

const port = (() => {
    const env = process.env.MASTER_SERVER_PORT;
    return parsePositiveInt(env, fileConfig.port);
})();

const pollIntervalMs = (() => {
    const env = process.env.MASTER_SERVER_POLL_INTERVAL_MS;
    return parsePositiveInt(env, fileConfig.poll_interval_ms || 30000);
})();

const serverTimeoutMs = (() => {
    const env = process.env.MASTER_SERVER_TIMEOUT_MS;
    return parsePositiveInt(env, fileConfig.server_timeout_ms);
})();

/**
 * Parse a configured CORS origin list.
 *
 * The configuration can be provided as:
 * - An array of origins (from JSON config)
 * - A comma-separated string of origins (from ENV)
 *
 * Returns a clean array of non-empty trimmed origins.
 */
function parseCorsOrigins(value) {
    if (!value) return [];
    if (Array.isArray(value)) {
        return value.map((v) => (v || "").toString().trim()).filter(Boolean);
    }
    return value
        .toString()
        .split(",")
        .map((s) => s.trim())
        .filter(Boolean);
}

const corsOrigins = (() => {
    const env = process.env.MASTER_SERVER_CORS_ORIGINS;
    const fileCorsOrigins = fileConfig.cors_origins;
    return parseCorsOrigins(env ?? fileCorsOrigins);
})();

module.exports = {
    port,
    pollIntervalMs,
    serverTimeoutMs,
    corsOrigins,
};
