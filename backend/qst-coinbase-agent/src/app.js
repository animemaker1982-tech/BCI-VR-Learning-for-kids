const { URL } = require('node:url');
const { extractPayload, summarizeCandles } = require('./historical-data');
const { loadState, saveHistoricalImport, updateState } = require('./state-store');
const { validateRiskConfig, getCredentialStatus, getReadiness, assertTradingReady, assertOrderAllowed } = require('./guardrails');
const { placeMarketOrder } = require('./coinbase-client');

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(body, null, 2));
}

async function readBody(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString('utf8');
}

function publicState(state) {
  return {
    historicalData: state.historicalData,
    coinbase: state.coinbase,
    risk: state.risk,
    trading: state.trading,
    orders: state.orders,
  };
}

function resetTradingState(state, reason) {
  return {
    ...state,
    trading: {
      ...state.trading,
      enabled: false,
      startedAt: null,
      startedBy: null,
      stopReason: reason,
      manualApproval: null,
    },
  };
}

async function createApp({ env = process.env, fetchImpl = fetch } = {}) {
  return async function app(request, response) {
    const url = new URL(request.url, 'http://localhost');

    try {
      if (request.method === 'GET' && url.pathname === '/health') {
        return sendJson(response, 200, { ok: true, service: 'qst-coinbase-agent' });
      }

      if (request.method === 'GET' && url.pathname === '/api/status') {
        const state = await loadState();
        return sendJson(response, 200, {
          ok: true,
          readiness: getReadiness(state, env),
          credentials: getCredentialStatus(env),
          state: publicState(state),
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/historical-data/upload') {
        const rawBody = await readBody(request);
        const upload = extractPayload(rawBody, request.headers['content-type'], Object.fromEntries(url.searchParams.entries()));
        const summary = summarizeCandles(upload.symbol, upload.source, upload.candles, upload.metadata);
        const filePath = await saveHistoricalImport(upload.symbol, summary);
        const nextState = await updateState((state) => {
          const imports = [summary, ...state.historicalData.imports].slice(0, 10);
          return {
            ...resetTradingState(state, 'historical_data_updated'),
            historicalData: {
              latestImport: { ...summary, filePath },
              imports: imports.map((entry) => ({
                symbol: entry.symbol,
                source: entry.source,
                importedAt: entry.importedAt,
                rowCount: entry.rowCount,
                dateRange: entry.dateRange,
                closeRange: entry.closeRange,
              })),
            },
          };
        });
        return sendJson(response, 201, {
          ok: true,
          message: 'Historical data imported; manual approval has been reset until trading is reviewed again.',
          readiness: getReadiness(nextState, env),
          latestImport: nextState.historicalData.latestImport,
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/config/coinbase') {
        const body = JSON.parse((await readBody(request)) || '{}');
        const nextState = await updateState((state) => ({
          ...resetTradingState(state, 'coinbase_configuration_updated'),
          coinbase: {
            apiHost: body.apiHost || state.coinbase.apiHost || 'api.coinbase.com',
            jwtAlgorithm: body.jwtAlgorithm || state.coinbase.jwtAlgorithm || 'ES256',
            updatedAt: new Date().toISOString(),
          },
        }));
        return sendJson(response, 200, {
          ok: true,
          message: 'Coinbase runtime configuration updated; manual approval has been reset.',
          readiness: getReadiness(nextState, env),
          coinbase: nextState.coinbase,
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/config/risk') {
        const body = JSON.parse((await readBody(request)) || '{}');
        const risk = validateRiskConfig(body);
        const nextState = await updateState((state) => ({
          ...resetTradingState(state, 'risk_configuration_updated'),
          risk,
        }));
        return sendJson(response, 200, {
          ok: true,
          message: 'Risk configuration saved; manual approval has been reset.',
          readiness: getReadiness(nextState, env),
          risk,
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/trading/approve') {
        const body = JSON.parse((await readBody(request)) || '{}');
        const state = await loadState();
        if (!state.historicalData.latestImport) {
          throw new Error('Historical data must be imported before approval can be granted');
        }
        if (!state.risk) {
          throw new Error('Risk configuration must be saved before approval can be granted');
        }
        const nextState = await updateState((current) => ({
          ...current,
          trading: {
            ...current.trading,
            enabled: false,
            startedAt: null,
            startedBy: null,
            stopReason: 'awaiting_manual_start',
            manualApproval: {
              approvedBy: body.approvedBy || 'unknown',
              notes: body.notes || '',
              approvedAt: new Date().toISOString(),
              historicalImportAt: current.historicalData.latestImport?.importedAt || null,
            },
          },
        }));
        return sendJson(response, 200, {
          ok: true,
          readiness: getReadiness(nextState, env),
          trading: nextState.trading,
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/trading/start') {
        const body = JSON.parse((await readBody(request)) || '{}');
        const state = await loadState();
        const readiness = assertTradingReady(state, env);
        const nextState = await updateState((current) => ({
          ...current,
          trading: {
            ...current.trading,
            enabled: true,
            startedAt: new Date().toISOString(),
            startedBy: body.startedBy || 'system',
            stopReason: null,
          },
        }));
        return sendJson(response, 200, {
          ok: true,
          message: 'Trading is enabled.',
          readiness,
          trading: nextState.trading,
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/trading/stop') {
        const body = JSON.parse((await readBody(request)) || '{}');
        const nextState = await updateState((state) => ({
          ...state,
          trading: {
            ...state.trading,
            enabled: false,
            stopReason: body.reason || 'manual_stop',
          },
        }));
        return sendJson(response, 200, {
          ok: true,
          trading: nextState.trading,
        });
      }

      if (request.method === 'POST' && url.pathname === '/api/orders/market') {
        const body = JSON.parse((await readBody(request)) || '{}');
        const state = await loadState();
        const side = String(body.side || '').toUpperCase();
        if (!['BUY', 'SELL'].includes(side)) {
          throw new Error('side must be BUY or SELL');
        }
        const order = {
          productId: body.productId,
          side,
          quoteSizeUsd: body.quoteSizeUsd,
          baseSize: body.baseSize,
          estimatedNotionalUsd: body.estimatedNotionalUsd,
        };
        const notionalUsd = assertOrderAllowed(state, order);
        const result = await placeMarketOrder({ state, order, env, fetchImpl });
        const nextState = await updateState((current) => ({
          ...current,
          trading: {
            ...current.trading,
            lastOrder: {
              productId: order.productId,
              side,
              notionalUsd,
              placedAt: new Date().toISOString(),
            },
          },
          orders: [
            {
              productId: order.productId,
              side,
              notionalUsd,
              placedAt: new Date().toISOString(),
              request: result.request,
              response: result.response,
            },
            ...current.orders,
          ].slice(0, 20),
        }));
        return sendJson(response, 201, {
          ok: true,
          order: nextState.trading.lastOrder,
          coinbase: result.response,
        });
      }

      return sendJson(response, 404, { ok: false, error: 'Not found' });
    } catch (error) {
      return sendJson(response, 400, {
        ok: false,
        error: error.message,
      });
    }
  };
}

module.exports = {
  createApp,
};
