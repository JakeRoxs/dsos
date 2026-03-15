/*
 * Dark Souls 3 - Open Server
 * Copyright (C) 2021 Tim Leonard
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

const express = require('express');
const rateLimit = require('express-rate-limit');
const router = express.Router();

const config = require("../../../config");
const { formatDuration, getClientIp, sendError, requireField } = require("../../../utils/helpers");

const serverTimeoutMs = config.serverTimeoutMs;

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
const filters = [
    
];

// Censor list will replace name and description of server with a censored message.
const censors = [ 

];

// List of public hostnames that are allowed to mark their servers as supporting sharding.
// This can be overridden via the SHARDING_ALLOWLIST environment variable (comma-separated).
const shardingAllowList = (function()
{
    const defaults = [
        "172.105.11.166"
    ];

    const env = process.env.SHARDING_ALLOWLIST;
    if (!env || env.trim().length === 0)
    {
        const list = defaults;
        console.log(`Sharding allowlist (default): ${list.join(', ')}`);
        return list;
    }

    const envEntries = env.split(',')
        .map(s => s.trim().toLowerCase())
        .filter(Boolean);

    // Env overrides defaults entirely (no merge) so that users can fully control what is allowed.
    const list = envEntries;
    console.log(`Sharding allowlist (env): ${list.join(', ')}`);
    return list;
})();

const oldestSupportedVersion = 2;

function isFiltered(name)
{
    const nameLower = String(name).toLowerCase();
    for (let i = 0; i < filters.length; i++)
    {
        if (nameLower.includes(filters[i]))
        {
            return true;
        }
    }

    return false;
}

function isCensored(name)
{
    const nameLower = String(name).toLowerCase();
    for (let i = 0; i < censors.length; i++)
    {
        if (nameLower.includes(censors[i]))
        {
            return true;
        }
    }

    return false;
}

function isServerFilter(serverInfo)
{
    if ((serverInfo?.Version ?? 0) < oldestSupportedVersion)
    {
        return true;
    }

    return  isFiltered(serverInfo.Name) || 
            isFiltered(serverInfo.Description) || 
            isFiltered(serverInfo.Hostname);
}

function isServerCensored(serverInfo)
{
    return  isCensored(serverInfo.Name) || 
            isCensored(serverInfo.Description) || 
            isCensored(serverInfo.Hostname);
}

function isServerAllowedToShard(serverInfo)
{
    const hostnameLower = String(serverInfo?.Hostname ?? '').toLowerCase();
    for (let i = 0; i < shardingAllowList.length; i++)
    {
        if (hostnameLower === shardingAllowList[i])
        {
            return true;
        }
    }

    return false;
}

function removeServer(id)
{
    activeServers.delete(id);
}

function addServer(id, ipAddress, hostname, privateHostname, description, name, publicKey, playerCount, password, modsWhiteList, modsBlackList, modsRequiredList, version, allowSharding, webAddress, port, isShard, gameType)
{
    const serverObj = {
        "Id": id,
        "IpAddress": ipAddress,
        "Port": port,
        "Hostname": hostname,
        "PrivateHostname": privateHostname,
        "Description": description,
        "Name": name,
        "PublicKey": publicKey,
        "PlayerCount": playerCount,
        "Password": password,
        "ModsWhiteList": modsWhiteList,
        "ModsBlackList": modsBlackList,
        "ModsRequiredList": modsRequiredList,
        "AllowSharding": allowSharding,
        "IsShard": isShard,
        "GameType": gameType,
        "WebAddress": webAddress,
        "UpdatedTime": Date.now(),
        "Version": version,
        "Censored": false
    };

    if (!isServerAllowedToShard(serverObj) && allowSharding)
    {
        console.log(`Dropped server, marked to allow sharding but not whitelisted: id=${id} ip=${ipAddress} port=${port} type=${gameType} name=${name}`);
        return;
    }

    if (allowSharding)
    {
        console.log(`Sharding enabled & allowed for server: id=${id} hostname=${hostname} ip=${ipAddress} port=${port} type=${gameType} name=${name}`);
    }

    if (isServerFilter(serverObj))
    {
        return;
    }

    if (isServerCensored(serverObj))
    {
        serverObj["Censored"] = true;
    }

    if (activeServers.has(id))
    {
        activeServers.set(id, serverObj);
        return;
    }

    activeServers.set(id, serverObj);

    console.log(`Adding server: id=${id} ip=${ipAddress} port=${port} type=${gameType} name=${name}`);
    console.log(`Total servers is now ${activeServers.size}`);
}

function removeTimedOutServers()
{
    let timeoutOccurred = false;
    const timeoutThreshold = Date.now() - serverTimeoutMs;

    for (const [id, server] of activeServers.entries())
    {
        if (server.UpdatedTime < timeoutThreshold)
        {
            console.log(`Removing server that timed out: id=${id} ip=${server.IpAddress}`);
            activeServers.delete(id);
            timeoutOccurred = true;
        }
    }

    if (timeoutOccurred)
    {
        console.log(`Total servers is now ${activeServers.size}`);
    }
}

// @route GET api/v1/servers
// @description Get a list of all active servers.
// @access Public
router.get('/', async (req, res) => {
    removeTimedOutServers();

    const serverInfo = [];
    for (const server of activeServers.values())
    {
        let displayName = server["Name"];
        let displayDescription = server["Description"];

        if (server.Censored)
        {
            displayName = "[Censored]";
            displayDescription = "Censored";
            
        }

        serverInfo.push({
            "Id": server["Id"],
            "IpAddress": server["IpAddress"],
            "Port": server["Port"],
            "Hostname": server["Hostname"],
            "PrivateHostname": server["PrivateHostname"],
            "Description": displayDescription,
            "Name": displayName,
            "PlayerCount": server["PlayerCount"],
            "PasswordRequired": server["Password"].length > 0,
            "ModsWhiteList": server["ModsWhiteList"],
            "ModsBlackList": server["ModsBlackList"],
            "ModsRequiredList": server["ModsRequiredList"],
            "AllowSharding": server["AllowSharding"],
            "IsShard": server["IsShard"],
            "GameType": server["GameType"],
            "WebAddress": server["WebAddress"]
        });
    }

    res.json({ "status":"success", "servers": serverInfo });
});

// @route GET api/v1/servers/status
// @description Get master server status info for dashboards.
// @access Public
router.get('/status', statusLimiter, async (req, res) => {
    res.json({ "status":"success", "statusData": getStatus() });
});

// @route POST api/v1/servers/:id/public_key
// @description Get the public key of a given server.
// @access Public
router.post('/:id/public_key', writeLimiter, async (req, res) => { 
    if (!('password' in req.body))
    {
        return sendError(res, 400, "Expected password in body.");
    }

    const password = req.body["password"];

    const server = activeServers.get(req.params.id);
    if (server)
    {
        if (password === server.Password)
        {
            res.json({ "status":"success", "PublicKey": server.PublicKey });
        }
        else
        {
            return sendError(res, 401, "Password was incorrect.");
        }
        return;
    }

    return sendError(res, 404, "Failed to find server.");
});

// @route POST api/v1/servers
// @description Adds or updates the server registered to the clients ip.
// @access Public
router.post('/', writeLimiter, async (req, res) => {
    if (!requireField(req, res, 'Hostname')) return;
    if (!requireField(req, res, 'PrivateHostname')) return;
    if (!requireField(req, res, 'Description')) return;
    if (!requireField(req, res, 'Name')) return;
    if (!requireField(req, res, 'PublicKey')) return;
    if (!requireField(req, res, 'PlayerCount')) return;
    if (!requireField(req, res, 'Password')) return;
    if (!requireField(req, res, 'ModsWhiteList')) return;
    if (!requireField(req, res, 'ModsBlackList')) return;
    if (!requireField(req, res, 'ModsRequiredList')) return;

    const playerCount = parseInt(req.body["PlayerCount"], 10);
    if (Number.isNaN(playerCount) || playerCount < 0) {
        return sendError(res, 400, "Invalid player_count");
    }

    const name = String(req.body["Name"]).slice(0, 128);
    const description = String(req.body["Description"]).slice(0, 256);
    const hostname = String(req.body["Hostname"]).slice(0, 128);
    const privateHostname = String(req.body["PrivateHostname"]).slice(0, 128);

    const publicKey = String(req.body["PublicKey"]);
    const password = String(req.body["Password"]);
    const modsWhiteList = req.body["ModsWhiteList"];
    const modsBlackList = req.body["ModsBlackList"];
    const modsRequiredList = req.body["ModsRequiredList"];
    let allowSharding = false;
    let isShard = false;
    let gameType = "DarkSouls3";
    let webAddress = "";
    let serverId = getClientIp(req);
    let port = 50050;

    if ('AllowSharding' in req.body)
    {
        allowSharding = (req.body["AllowSharding"] == "1" || req.body["AllowSharding"] == "true");
    }
    if ('WebAddress' in req.body)
    {
        webAddress = req.body["WebAddress"];
    }
    if ('ServerId' in req.body)
    {
        serverId = req.body["ServerId"];
    }
    if ('Port' in req.body)
    {
        port = req.body["Port"];
    }
    if ('IsShard' in req.body)
    {
        isShard = (req.body["IsShard"] == "1" || req.body["IsShard"] == "true");
    }
    if ('GameType' in req.body)
    {
        gameType = req.body["GameType"];
    }

    const version = ('ServerVersion' in req.body) ? parseInt(req.body['ServerVersion']) : 1;

    addServer(
        serverId,
        getClientIp(req),
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
        gameType
    );

    res.json({ "status":"success" });
});

// @route DELETE api/v1/servers
// @description Delete the server registered to the clients ip.
// @access Public
router.delete('/', writeLimiter, (req, res) => { 
    
    let serverId = getClientIp(req);
    if ('ServerId' in req.body)
    {
        serverId = req.body["ServerId"];
    }

    removeServer(serverId);
    res.json({ "status":"success" });
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
		serverTimeoutMs: serverTimeoutMs,
		uptime: formatDuration(process.uptime() * 1000),
		shardingAllowList: shardingAllowList,
		filters: filters,
		censors: censors,
		oldestSupportedVersion: oldestSupportedVersion
	};
}

module.exports = router;
module.exports.getStatus = getStatus;