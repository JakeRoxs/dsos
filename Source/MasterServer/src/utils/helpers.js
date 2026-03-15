function formatDuration(ms) {
	const seconds = Math.floor(ms / 1000);
	const minutes = Math.floor(seconds / 60);
	const hours = Math.floor(minutes / 60);
	const days = Math.floor(hours / 24);

	const parts = [];
	if (days) parts.push(`${days}d`);
	if (hours % 24) parts.push(`${hours % 24}h`);
	if (minutes % 60) parts.push(`${minutes % 60}m`);
	parts.push(`${seconds % 60}s`);

	return parts.join(' ');
}

function getClientIp(req) {
	// Express provides req.ip when trust proxy is enabled; normalize IPv4-mapped IPv6.
	const raw = req.ip || req.connection.remoteAddress || '';
	return String(raw).replace(/^::ffff:/, '');
}

function sendError(res, statusCode, message) {
	res.status(statusCode).json({ status: 'error', message });
}

function requireField(req, res, field) {
	if (!(field in req.body) || req.body[field] === null || req.body[field] === undefined || req.body[field] === '') {
		sendError(res, 400, `Expected ${field.toLowerCase()} in body.`);
		return false;
	}
	return true;
}

module.exports = {
	formatDuration,
	getClientIp,
	sendError,
	requireField,
};
