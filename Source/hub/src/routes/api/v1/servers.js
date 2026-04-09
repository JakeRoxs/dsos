/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

const express = require("express");
const rateLimit = require("express-rate-limit");
const router = express.Router();

const config = require("../../../config");
const {
  formatDuration,
  getClientIp,
  sendError,
  requireField,
} = require("../../../utils/helpers");

const serverTimeoutMs = config.serverTimeoutMs;
const MASTER_SERVER_WRITE_SECRET = String(
  process.env.MASTER_SERVER_WRITE_SECRET ?? "",
).trim();

if (!MASTER_SERVER_WRITE_SECRET) {
  console.error(
    "[MASTER SERVER] MASTER_SERVER_WRITE_SECRET is not configured. " +
      "Write operations require this secret and the service will reject them.",
  );
  // Optional: throw to fail startup early instead of runtime 500 behavior.
  // throw new Error("MASTER_SERVER_WRITE_SECRET is required");
}

const activeServers = new Map();
let lastCleanupTime = 0;
let isPruning = false;
const CLEANUP_INTERVAL_MS = 15_000; // avoid running cleanup too frequently

const statusLimiter = rateLimit({
  windowMs: 60_000,
  max: 60,
  standardHeaders: true,
  legacyHeaders: false,
});

const writeLimiter = rateLimit({
  windowMs: 60_000,
  max: 20,
  standardHeaders: true,
  legacyHeaders: false,
});

// Use lowercase patterns only.

// Filter list will block servers showing up in the loader.
const filters = [];

// Censor list will replace name and description of server with a censored message.
const censors = [];

// List of public hostnames that are allowed to mark their servers as supporting sharding.
// This can be overridden via the SHARDING_ALLOWLIST environment variable (comma-separated).
const shardingAllowList = (function () {
  const defaults = [
    "https://rekindled-ds2s.jakeesws.xyz",
    "https://rekindled-ds3.jakeesws.xyz",
  ];

  const env = process.env.SHARDING_ALLOWLIST;
  if (!env || env.trim().length === 0) {
    const list = defaults;
    console.log(`Sharding allowlist (default): ${list.join(", ")}`);
    return list;
  }

  const envEntries = env
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);

  // Env overrides defaults entirely (no merge) so that users can fully control what is allowed.
  const list = envEntries;
  console.log(`Sharding allowlist (env): ${list.join(", ")}`);
  return list;
})();

const oldestSupportedVersion = 2;
const allowedGameTypes = new Set(["DarkSouls3", "DarkSouls2"]);

function isFiltered(name) {
  const nameLower = String(name).toLowerCase();
  for (const filter of filters) {
    if (nameLower.includes(filter)) {
      return true;
    }
  }

  return false;
}

function isCensored(name) {
  const nameLower = String(name).toLowerCase();
  for (const censor of censors) {
    if (nameLower.includes(censor)) {
      return true;
    }
  }

  return false;
}

function isServerFilter(serverInfo) {
  if ((serverInfo?.Version ?? 0) < oldestSupportedVersion) {
    return true;
  }

  return (
    isFiltered(serverInfo.Name) ||
    isFiltered(serverInfo.Description) ||
    isFiltered(serverInfo.Hostname)
  );
}

function isServerCensored(serverInfo) {
  return (
    isCensored(serverInfo.Name) ||
    isCensored(serverInfo.Description) ||
    isCensored(serverInfo.Hostname)
  );
}

function isServerAllowedToShard(serverInfo) {
  const hostnameLower = String(serverInfo?.Hostname ?? "").toLowerCase();
  for (const allowed of shardingAllowList) {
    if (hostnameLower === allowed) {
      return true;
    }
  }

  return false;
}

function normalizeString(value) {
  let sanitized = String(value ?? "");

  // Shared base sanitization for both field values and log output.
  // This handles defense-in-depth control-character removal only.
  // Length limits are enforced by the caller after all field-specific filtering.
  sanitized = sanitized.replaceAll(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]+/g, "");

  return sanitized;
}

function sanitizeField(value, maxLength = 256) {
  let sanitized = normalizeString(value);

  // Remove possible HTML tag constructs to reduce risk of stored XSS by bug.
  let inTag = false;
  let filtered = "";
  for (const ch of sanitized) {
    if (ch === "<") {
      inTag = true;
      continue;
    }

    if (ch === ">") {
      inTag = false;
      continue;
    }

    if (!inTag) {
      filtered += ch;
    }
  }
  sanitized = filtered;

  // Remove any remaining angle brackets (e.g. incomplete tags like <script or >foo) to prevent bypass.
  sanitized = sanitized.replaceAll(/[<>]+/g, "");

  if (sanitized.length > maxLength) {
    sanitized = sanitized.slice(0, maxLength);
  }

  return sanitized;
}

function sanitizeForLog(value, maxLength = 256) {
  let sanitized = String(value ?? "");
  sanitized = sanitized.replaceAll(/[\r\n\t]+/g, " ");
  sanitized = normalizeString(sanitized);

  if (sanitized.length > maxLength) {
    sanitized = sanitized.slice(0, maxLength);
  }

  return sanitized;
}

function safeLogValue(value, maxLength = 256) {
  return JSON.stringify(sanitizeForLog(value, maxLength));
}

function safeLogErrorMessage(error, maxLength = 512) {
  const message = error instanceof Error ? error.message : String(error);
  return JSON.stringify(sanitizeForLog(message, maxLength));
}

function validatePublicKey(value) {
  let key = String(value ?? "").trim();
  if (key.length === 0 || key.length > 4096) {
    return null;
  }

  // Reject clearly dangerous characters immediately.
  if (/[<>\\`]/.test(key)) {
    return null;
  }

  // Guard against ReDoS and extreme payloads by enforcing a stricter max length. (already checked above)
  // If PEM wrapper exists, use inner value; otherwise treat as raw base64.
  let keyContent = key;

  const upperKey = key.toUpperCase();
  const beginMarker = "-----BEGIN PUBLIC KEY-----";
  const endMarker = "-----END PUBLIC KEY-----";
  const beginIndex = upperKey.indexOf(beginMarker);
  const endIndex = upperKey.lastIndexOf(endMarker);

  if (beginIndex !== -1 && endIndex !== -1 && endIndex > beginIndex) {
    const inner = key.slice(beginIndex + beginMarker.length, endIndex).trim();
    if (inner.length > 0) {
      keyContent = inner;
    }
  }

  const keyNormalized = keyContent.replaceAll(/[\r\n\t ]+/g, "");

  // Base64 characters only (padding only allowed at end, max 2 chars).
  if (!/^[A-Za-z0-9+/]+={0,2}$/.test(keyNormalized)) {
    return null;
  }

  // Enforce 4-byte alignment for RFC 4648 base64 strings.
  if (keyNormalized.length % 4 !== 0) {
    return null;
  }

  return key;
}

function validateWebAddress(value) {
  const url = String(value ?? "").trim();
  if (url.length === 0) {
    return "";
  }

  try {
    const parsed = new URL(url);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
      return "";
    }

    // canonicalize by dropping credentials
    parsed.username = "";
    parsed.password = "";
    return parsed.toString();
  } catch (err) {
    const safeUrl = safeLogValue(url, 256);
    const safeError = safeLogErrorMessage(err, 512);
    console.warn(
      `Invalid WebAddress provided during validation. url=${safeUrl} error=${safeError}`,
    );
    return "";
  }
}

function normalizeServerId(value) {
  const id = String(value ?? "").trim();
  if (id.length === 0 || id.length > 128) {
    return null;
  }

  // Accept only common safe ID characters: alphanumeric, dot, dash, underscore, colon.
  // Reject spaces and non-printable chars to avoid logging/injection vector abuse.
  if (!/^[A-Za-z0-9._:-]+$/.test(id) || id.includes("..")) {
    return null;
  }

  return id;
}

function normalizePort(value) {
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    return null;
  }

  return port;
}

function normalizeGameType(value) {
  const gameType = String(value ?? "").trim();
  return allowedGameTypes.has(gameType) ? gameType : "DarkSouls3";
}

function checkRequiredFields(req, res, fields) {
  for (const field of fields) {
    if (!requireField(req, res, field)) {
      return false;
    }
  }
  return true;
}

function parseIntField(req, res, fieldName, minValue, errorMessage) {
  const value = Number.parseInt(req.body[fieldName], 10);
  if (Number.isNaN(value) || value < minValue) {
    sendError(res, 400, errorMessage);
    return null;
  }
  return value;
}

function getServerIdFromRequest(req) {
  const normalizedClientServerId = normalizeServerId(getClientIp(req));
  if (!normalizedClientServerId) {
    return null;
  }

  if ("ServerId" in req.body) {
    const normalizedServerId = normalizeServerId(req.body["ServerId"]);
    if (normalizedServerId) {
      return normalizedServerId;
    }
  }

  return normalizedClientServerId;
}

function getPortFromRequest(req) {
  if ("Port" in req.body) {
    const normalizedPort = normalizePort(req.body["Port"]);
    if (normalizedPort !== null) {
      return normalizedPort;
    }
  }

  return 50050;
}

function getAllowShardingFromRequest(req) {
  return (
    req.body["AllowSharding"] == "1" || req.body["AllowSharding"] == "true"
  );
}

function getIsShardFromRequest(req) {
  return req.body["IsShard"] == "1" || req.body["IsShard"] == "true";
}

const crypto = require("node:crypto");

function requireWriteAuth(req, res) {
  if (!MASTER_SERVER_WRITE_SECRET) {
    return sendError(res, 500, "Server write secret not configured");
  }

  const providedSecret = String(
    req.get("x-master-server-write-secret") ?? "",
  ).trim();

  const secretA = Buffer.from(providedSecret, "utf8");
  const secretB = Buffer.from(MASTER_SERVER_WRITE_SECRET, "utf8");

  const length = Math.max(secretA.length, secretB.length);
  const paddedA = Buffer.alloc(length);
  const paddedB = Buffer.alloc(length);
  secretA.copy(paddedA);
  secretB.copy(paddedB);

  if (!crypto.timingSafeEqual(paddedA, paddedB)) {
    return sendError(res, 401, "Unauthorized");
  }

  return null;
}

function removeServer(id) {
  activeServers.delete(id);
}

function buildServerObject({
  id,
  ipAddress,
  hostname,
  privateHostname,
  description,
  name,
  publicKey,
  playerCount,
  password,
  modsWhiteList,
  modsBlackList,
  modsRequiredList,
  version,
  allowSharding,
  webAddress,
  port,
  isShard,
  gameType,
}) {
  return {
    Id: id,
    IpAddress: ipAddress,
    Port: port,
    Hostname: sanitizeField(hostname, 128),
    PrivateHostname: sanitizeField(privateHostname, 128),
    Description: sanitizeField(description, 256),
    Name: sanitizeField(name, 128),
    PublicKey: publicKey,
    PlayerCount: playerCount,
    Password: password,
    ModsWhiteList: modsWhiteList,
    ModsBlackList: modsBlackList,
    ModsRequiredList: modsRequiredList,
    AllowSharding: allowSharding,
    IsShard: isShard,
    GameType: gameType,
    WebAddress: webAddress,
    UpdatedTime: Date.now(),
    Version: version,
    Censored: false,
  };
}

function persistServer(serverObj) {
  const {
    Id: id,
    IpAddress: ipAddress,
    Port: port,
    GameType: gameType,
    Name: name,
  } = serverObj;

  const safeId = safeLogValue(id, 128);
  const safeIpAddress = safeLogValue(ipAddress, 128);
  const safePort = safeLogValue(port, 16);
  const safeGameType = safeLogValue(gameType, 32);
  const safeName = safeLogValue(name, 128);

  if (activeServers.has(id)) {
    activeServers.set(id, serverObj);
    return;
  }

  activeServers.set(id, serverObj);
  console.log(
    `Adding server: id=${safeId} ip=${safeIpAddress} port=${safePort} type=${safeGameType} name=${safeName}`,
  );
  console.log(`Total servers is now ${activeServers.size}`);
}

function addServer(serverData) {
  const serverObj = buildServerObject(serverData);

  const safeId = safeLogValue(serverObj.Id, 128);
  const safeHostname = safeLogValue(serverObj.Hostname, 128);
  const safeIpAddress = safeLogValue(serverObj.IpAddress, 128);
  const safePort = safeLogValue(serverObj.Port, 16);
  const safeGameType = safeLogValue(serverObj.GameType, 32);
  const safeName = safeLogValue(serverObj.Name, 128);

  if (!isServerAllowedToShard(serverObj) && serverObj.AllowSharding) {
    console.log(
      `Dropped server, marked to allow sharding but not whitelisted: id=${safeId} ip=${safeIpAddress} port=${safePort} type=${safeGameType} name=${safeName}`,
    );
    return;
  }

  if (serverObj.AllowSharding) {
    console.log(
      `Sharding enabled & allowed for server: id=${safeId} hostname=${safeHostname} ip=${safeIpAddress} port=${safePort} type=${safeGameType} name=${safeName}`,
    );
  }

  if (isServerFilter(serverObj)) {
    return;
  }

  if (isServerCensored(serverObj)) {
    serverObj.Censored = true;
  }

  persistServer(serverObj);
}

function removeTimedOutServers() {
  let timeoutOccurred = false;
  const timeoutThreshold = Date.now() - serverTimeoutMs;

  for (const [id, server] of activeServers.entries()) {
    if (server.UpdatedTime < timeoutThreshold) {
      const safeId = safeLogValue(id, 128);
      const safeIpAddress = safeLogValue(server.IpAddress, 128);
      console.log(
        `Removing server that timed out: id=${safeId} ip=${safeIpAddress}`,
      );
      activeServers.delete(id);
      timeoutOccurred = true;
    }
  }

  if (timeoutOccurred) {
    console.log(`Total servers is now ${activeServers.size}`);
  }
}

// @route GET api/v1/servers
// @description Get a list of all active servers.
// @access Public
router.get("/", async (req, res) => {
  removeTimedOutServers();

  const serverInfo = [];
  for (const server of activeServers.values()) {
    let displayName = server["Name"];
    let displayDescription = server["Description"];

    if (server.Censored) {
      displayName = "[Censored]";
      displayDescription = "Censored";
    }

    serverInfo.push({
      Id: server["Id"],
      IpAddress: server["IpAddress"],
      Port: server["Port"],
      Hostname: server["Hostname"],
      PrivateHostname: server["PrivateHostname"],
      Description: displayDescription,
      Name: displayName,
      PlayerCount: server["PlayerCount"],
      PasswordRequired: String(server["Password"] ?? "").length > 0,
      ModsWhiteList: server["ModsWhiteList"],
      ModsBlackList: server["ModsBlackList"],
      ModsRequiredList: server["ModsRequiredList"],
      AllowSharding: server["AllowSharding"],
      IsShard: server["IsShard"],
      GameType: server["GameType"],
      WebAddress: server["WebAddress"],
    });
  }

  res.json({ status: "success", servers: serverInfo });
});

// @route GET api/v1/servers/status
// @description Get master server status info for dashboards.
// @access Public
router.get("/status", statusLimiter, async (req, res) => {
  res.json({ status: "success", statusData: getStatus() });
});

// @route POST api/v1/servers/:id/public_key
// @description Get the public key of a given server.
// @access Public
router.post("/:id/public_key", writeLimiter, async (req, res) => {
  const authError = requireWriteAuth(req, res);
  if (authError) {
    return authError;
  }

  if (!("password" in req.body)) {
    return sendError(res, 400, "Expected password in body.");
  }

  const password = req.body["password"];

  const server = activeServers.get(req.params.id);
  if (server) {
    if (password === server.Password) {
      res.json({ status: "success", PublicKey: server.PublicKey });
    } else {
      return sendError(res, 401, "Password was incorrect.");
    }
    return;
  }

  return sendError(res, 404, "Failed to find server.");
});

// @route POST api/v1/servers
// @description Adds or updates the server registered to the clients ip.
// @access Public
function buildServerPayload(req, res) {
  const requiredFields = [
    "Hostname",
    "PrivateHostname",
    "Description",
    "Name",
    "PublicKey",
    "PlayerCount",
    "Password",
    "ModsWhiteList",
    "ModsBlackList",
    "ModsRequiredList",
  ];

  if (!checkRequiredFields(req, res, requiredFields)) {
    return null;
  }

  const playerCount = parseIntField(
    req,
    res,
    "PlayerCount",
    0,
    "Invalid player_count",
  );
  if (playerCount === null) {
    return null;
  }

  const name = sanitizeField(req.body["Name"], 128);
  const description = sanitizeField(req.body["Description"], 256);
  const hostname = sanitizeField(req.body["Hostname"], 128);
  const privateHostname = sanitizeField(req.body["PrivateHostname"], 128);

  const rawPublicKey = req.body["PublicKey"];
  const publicKey = validatePublicKey(rawPublicKey);
  if (!publicKey) {
    sendError(res, 400, "Invalid PublicKey");
    return null;
  }

  const password = String(req.body["Password"]);
  if (password.length === 0 || password.length > 1024) {
    sendError(res, 400, "Invalid password length");
    return null;
  }

  const webAddress =
    "WebAddress" in req.body && req.body["WebAddress"] != null
      ? validateWebAddress(req.body["WebAddress"])
      : "";

  const modsWhiteList = req.body["ModsWhiteList"];
  const modsBlackList = req.body["ModsBlackList"];
  const modsRequiredList = req.body["ModsRequiredList"];

  const serverId = getServerIdFromRequest(req);
  if (!serverId) {
    sendError(res, 500, "Invalid client IP for server ID");
    return null;
  }

  const port = getPortFromRequest(req);
  const allowSharding = getAllowShardingFromRequest(req);
  const isShard = getIsShardFromRequest(req);
  const gameType =
    "GameType" in req.body
      ? normalizeGameType(req.body["GameType"])
      : "DarkSouls3";

  const version =
    "ServerVersion" in req.body
      ? Number.parseInt(req.body["ServerVersion"])
      : 1;

  return {
    id: serverId,
    ipAddress: getClientIp(req),
    hostname,
    privateHostname,
    description,
    name,
    publicKey,
    playerCount,
    password,
    modsWhiteList,
    modsBlackList,
    modsRequiredList,
    version,
    allowSharding,
    webAddress,
    port,
    isShard,
    gameType,
  };
}

router.post("/", writeLimiter, async (req, res) => {
  const authError = requireWriteAuth(req, res);
  if (authError) {
    return authError;
  }

  const serverData = buildServerPayload(req, res);
  if (!serverData) {
    return;
  }

  addServer(serverData);
  res.json({ status: "success" });
});

// @route DELETE api/v1/servers
// @description Delete the server registered to the clients ip.
// @access Public
router.delete("/", writeLimiter, (req, res) => {
  const authError = requireWriteAuth(req, res);
  if (authError) {
    return authError;
  }

  const normalizedClientServerId = normalizeServerId(getClientIp(req));
  if (!normalizedClientServerId) {
    return sendError(res, 500, "Invalid client IP for server ID");
  }

  let serverId = normalizedClientServerId;
  if ("ServerId" in req.body) {
    const normalizedServerId = normalizeServerId(req.body["ServerId"]);
    if (!normalizedServerId) {
      return sendError(res, 400, "Invalid ServerId");
    }
    serverId = normalizedServerId;
  }

  removeServer(serverId);
  res.json({ status: "success" });
});

function pruneStaleServers() {
  if (isPruning) {
    return;
  }

  const now = Date.now();
  if (now - lastCleanupTime < CLEANUP_INTERVAL_MS) {
    return;
  }

  isPruning = true;
  try {
    lastCleanupTime = now;
    removeTimedOutServers();
  } finally {
    isPruning = false;
  }
}

function getStatus() {
  pruneStaleServers();
  return {
    activeServerCount: activeServers.size,
    uptime: formatDuration(process.uptime() * 1000),
    shardingAllowList: shardingAllowList,
    filters: filters,
    censors: censors,
    oldestSupportedVersion: oldestSupportedVersion,
  };
}

module.exports = router;
module.exports.getStatus = getStatus;
module.exports.validatePublicKey = validatePublicKey;
module.exports.normalizeServerId = normalizeServerId;
module.exports.requireWriteAuth = requireWriteAuth;
