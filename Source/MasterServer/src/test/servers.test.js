const assert = require("node:assert");
const test = require("node:test");

process.env.MASTER_SERVER_WRITE_SECRET = "test-secret";

delete require.cache[require.resolve("../routes/api/v1/servers")];
const {
    validatePublicKey,
    normalizeServerId,
    requireWriteAuth,
} = require("../routes/api/v1/servers");

function makeRes() {
    const res = {
        statusCode: null,
        body: null,
        statusCalled: null,
        jsonCalled: null,
    };
    res.status = function (code) {
        res.statusCode = code;
        res.statusCalled = true;
        return res;
    };
    res.json = function (body) {
        res.body = body;
        res.jsonCalled = true;
        return res;
    };
    return res;
}

test("validatePublicKey accepts normalized base64 with PEM wrapper", () => {
    const key = "-----BEGIN PUBLIC KEY-----\nQUJD\n-----END PUBLIC KEY-----";
    const result = validatePublicKey(key);
    assert.strictEqual(result, key);
});

test("validatePublicKey rejects invalid padding", () => {
    assert.strictEqual(validatePublicKey("QUJD="), null);
    assert.strictEqual(validatePublicKey("Q==="), null);
});

test("normalizeServerId accepts safe IDs and rejects unsafe characters", () => {
    assert.strictEqual(normalizeServerId("abc_123-."), "abc_123-.");
    assert.strictEqual(normalizeServerId("with space"), null);
    assert.strictEqual(normalizeServerId("../evil"), null);
});

test("requireWriteAuth returns unauthorized for wrong header", () => {
    const req = { get: () => "bad-secret" };
    const res = makeRes();
    requireWriteAuth(req, res);
    assert.strictEqual(res.statusCode, 401);
});
