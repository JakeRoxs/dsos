const path = require('path');
const fs = require('fs');

const raw = fs.readFileSync(path.join(__dirname, 'config.json'), 'utf8');
const fileConfig = JSON.parse(raw);

function parsePositiveInt(envVar, fallback) {
	if (!envVar) return fallback;
	const parsed = parseInt(envVar, 10);
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

const corsOrigin = (process.env.MASTER_SERVER_CORS_ORIGIN || fileConfig.cors_origin || '*');

module.exports = {
	port,
	pollIntervalMs,
	serverTimeoutMs,
	corsOrigin,
};
