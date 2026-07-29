const { badRequest } = require('./errors');

function validateRiskConfig(input) {
  if (!input || typeof input !== 'object') {
    throw badRequest('Risk configuration body is required');
  }

  const maxPositionUsd = Number(input.maxPositionUsd);
  const maxDailyLossUsd = Number(input.maxDailyLossUsd);
  const allowedProducts = Array.isArray(input.allowedProducts)
    ? input.allowedProducts.map((product) => String(product).trim()).filter(Boolean)
    : [];

  if (!Number.isFinite(maxPositionUsd) || maxPositionUsd <= 0) {
    throw badRequest('maxPositionUsd must be a positive number');
  }
  if (!Number.isFinite(maxDailyLossUsd) || maxDailyLossUsd <= 0) {
    throw badRequest('maxDailyLossUsd must be a positive number');
  }
  if (allowedProducts.length === 0) {
    throw badRequest('allowedProducts must contain at least one Coinbase product id');
  }

  return {
    maxPositionUsd,
    maxDailyLossUsd,
    allowedProducts,
    requireManualApproval: input.requireManualApproval !== false,
    updatedAt: new Date().toISOString(),
  };
}

function getCredentialStatus(env = process.env) {
  return {
    apiKeyConfigured: Boolean(env.COINBASE_API_KEY_NAME),
    privateKeyConfigured: Boolean(env.COINBASE_PRIVATE_KEY),
    liveTradingAllowed: env.COINBASE_ALLOW_LIVE_TRADING === 'true',
  };
}

function getReadiness(state, env = process.env) {
  const credentials = getCredentialStatus(env);
  const checks = {
    historicalDataImported: Boolean(state.historicalData.latestImport?.rowCount),
    riskConfigured: Boolean(state.risk),
    manualApprovalGranted: Boolean(state.trading.manualApproval),
    coinbaseCredentialsConfigured: credentials.apiKeyConfigured && credentials.privateKeyConfigured,
    liveTradingAllowed: credentials.liveTradingAllowed,
  };

  return {
    ready: Object.values(checks).every(Boolean),
    checks,
  };
}

function assertTradingReady(state, env = process.env) {
  const readiness = getReadiness(state, env);
  if (!readiness.ready) {
    throw badRequest(`Trading is not ready: ${JSON.stringify(readiness.checks)}`);
  }
  return readiness;
}

function assertOrderAllowed(state, order) {
  if (!state.trading.enabled) {
    throw badRequest('Trading is not enabled');
  }
  if (!state.risk.allowedProducts.includes(order.productId)) {
    throw badRequest(`Product ${order.productId} is not in allowedProducts`);
  }

  const notional = Number(order.quoteSizeUsd ?? order.estimatedNotionalUsd);
  if (!Number.isFinite(notional) || notional <= 0) {
    throw badRequest('quoteSizeUsd or estimatedNotionalUsd must be a positive number');
  }
  if (notional > state.risk.maxPositionUsd) {
    throw badRequest(`Requested order exceeds maxPositionUsd of ${state.risk.maxPositionUsd}`);
  }

  return notional;
}

module.exports = {
  validateRiskConfig,
  getCredentialStatus,
  getReadiness,
  assertTradingReady,
  assertOrderAllowed,
};
