/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

process.env.UV_THREADPOOL_SIZE = 2;

const express = require("express");
const rateLimit = require("express-rate-limit");
const cors = require("cors");
const path = require("node:path");

const servers = require("./routes/api/v1/servers");
const { formatDuration } = require("./utils/helpers");
const config = require("./config");

const port = config.port;

const app = express();
app.set("trust proxy", true);
app.disable("x-powered-by");

const limiter = rateLimit({
    windowMs: 1 * 60 * 1000,
    max: 300,
    standardHeaders: true,
    legacyHeaders: false,
});

const corsOptions = {
    origin: (origin, callback) => {
        // Allow requests with no Origin (curl, server-to-server, same-origin).
        if (!origin) return callback(null, true);

        // When configured with '*', allow any origin (open CORS policy).
        if (config.corsOrigins.includes("*")) return callback(null, "*");

        if (config.corsOrigins.includes(origin)) {
            return callback(null, origin);
        }

        callback(new Error("Not allowed by CORS"));
    },
    optionsSuccessStatus: 200,
};

app.use(cors(corsOptions));
app.use(express.json({}));
app.use(limiter);

const serverStartTime = Date.now();
const pollIntervalMs = config.pollIntervalMs;

app.set("views", path.join(__dirname, "templates"));
app.set("view engine", "ejs");

app.get("/", (req, res) => {
    const status = servers.getStatus();
    const uptime = formatDuration(Date.now() - serverStartTime);

    res.render("dashboard", { status, uptime, port, pollIntervalMs });
});

app.use("/api/v1/servers", servers);

app.listen(port, () => {
    console.log(`This service is now listening on port ${port}!`);
});
