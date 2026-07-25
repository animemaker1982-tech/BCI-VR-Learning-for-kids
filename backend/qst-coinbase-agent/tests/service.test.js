const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { startServer } = require('../src/server');

async function createTestContext(options = {}) {
  const runtimeDir = await fs.mkdtemp(path.join(os.tmpdir(), 'qst-coinbase-agent-'));
  const { privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const env = {
    QST_DATA_DIR: runtimeDir,
    COINBASE_API_KEY_NAME: 'organizations/test/apiKeys/test-key',
    COINBASE_PRIVATE_KEY: privateKey.export({ type: 'pkcs8', format: 'pem' }).toString(),
    COINBASE_ALLOW_LIVE_TRADING: options.allowLiveTrading ? 'true' : 'false',
    COINBASE_JWT_ALGORITHM: 'ES256',
  };

  const requests = [];
  const fetchImpl = async (url, requestOptions) => {
    requests.push({ url, requestOptions });
    return {
      ok: true,
      status: 200,
      async text() {
        return JSON.stringify({ success: true, order_id: 'mock-order-id' });
      },
    };
  };

  const server = await startServer({ port: 0, env, fetchImpl });
  const address = server.address();
  const baseUrl = `http://127.0.0.1:${address.port}`;

  async function request(pathname, init = {}) {
    const response = await fetch(`${baseUrl}${pathname}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...(init.headers || {}),
      },
    });
    const text = await response.text();
    return {
      status: response.status,
      body: text ? JSON.parse(text) : {},
    };
  }

  return {
    env,
    requests,
    request,
    async close() {
      await new Promise((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
      await fs.rm(runtimeDir, { recursive: true, force: true });
    },
  };
}

async function seedTradingReady(context) {
  await context.request('/api/historical-data/upload', {
    method: 'POST',
    body: JSON.stringify({
      symbol: 'BTC-USD',
      candles: [
        { date: '2024-01-01', open: 100, high: 110, low: 90, close: 105, volume: 10 },
        { date: '2024-01-02', open: 105, high: 115, low: 95, close: 110, volume: 12 },
      ],
    }),
  });
  await context.request('/api/config/risk', {
    method: 'POST',
    body: JSON.stringify({
      maxPositionUsd: 250,
      maxDailyLossUsd: 100,
      allowedProducts: ['BTC-USD'],
    }),
  });
  await context.request('/api/trading/approve', {
    method: 'POST',
    body: JSON.stringify({ approvedBy: 'qa' }),
  });
}

test('status reports missing live trading permission until prerequisites exist', async () => {
  const context = await createTestContext({ allowLiveTrading: false });
  try {
    const response = await context.request('/api/status');
    assert.equal(response.status, 200);
    assert.equal(response.body.readiness.ready, false);
    assert.equal(response.body.readiness.checks.historicalDataImported, false);
    assert.equal(response.body.readiness.checks.liveTradingAllowed, false);
  } finally {
    await context.close();
  }
});

test('trading start is blocked before manual approval', async () => {
  const context = await createTestContext({ allowLiveTrading: true });
  try {
    await context.request('/api/historical-data/upload', {
      method: 'POST',
      body: JSON.stringify({
        symbol: 'BTC-USD',
        candles: [{ date: '2024-01-01', open: 1, high: 2, low: 0.5, close: 1.5, volume: 5 }],
      }),
    });
    await context.request('/api/config/risk', {
      method: 'POST',
      body: JSON.stringify({
        maxPositionUsd: 250,
        maxDailyLossUsd: 100,
        allowedProducts: ['BTC-USD'],
      }),
    });

    const start = await context.request('/api/trading/start', {
      method: 'POST',
      body: JSON.stringify({ startedBy: 'qa' }),
    });

    assert.equal(start.status, 400);
    assert.match(start.body.error, /manualApprovalGranted/);
  } finally {
    await context.close();
  }
});

test('trading can start only after historical import, risk config, approval, and live flag', async () => {
  const context = await createTestContext({ allowLiveTrading: true });
  try {
    await seedTradingReady(context);
    const start = await context.request('/api/trading/start', {
      method: 'POST',
      body: JSON.stringify({ startedBy: 'qa' }),
    });

    assert.equal(start.status, 200);
    assert.equal(start.body.trading.enabled, true);
    assert.equal(start.body.readiness.ready, true);
  } finally {
    await context.close();
  }
});

test('market orders call Coinbase only when trading is enabled', async () => {
  const context = await createTestContext({ allowLiveTrading: true });
  try {
    await seedTradingReady(context);
    await context.request('/api/trading/start', {
      method: 'POST',
      body: JSON.stringify({ startedBy: 'qa' }),
    });

    const order = await context.request('/api/orders/market', {
      method: 'POST',
      body: JSON.stringify({
        productId: 'BTC-USD',
        side: 'BUY',
        quoteSizeUsd: 125,
      }),
    });

    assert.equal(order.status, 201);
    assert.equal(order.body.coinbase.success, true);
    assert.equal(context.requests.length, 1);
    assert.match(context.requests[0].requestOptions.headers.Authorization, /^Bearer\s.+/);
  } finally {
    await context.close();
  }
});
