const crypto = require('node:crypto');
const { badGateway, badRequest } = require('./errors');

function base64UrlEncode(input) {
  return Buffer.from(input)
    .toString('base64')
    .replace(/=/g, '')
    .replace(/\+/g, '-')
    .replace(/\//g, '_');
}

function signJwt(signingInput, privateKeyPem, algorithm) {
  if (algorithm === 'EdDSA') {
    return crypto.sign(null, Buffer.from(signingInput), privateKeyPem);
  }
  if (algorithm === 'ES256') {
    return crypto.sign('sha256', Buffer.from(signingInput), privateKeyPem);
  }
  throw badRequest(`Unsupported JWT algorithm: ${algorithm}`);
}

function createJwt({ apiKeyName, privateKeyPem, algorithm, method, host, path }) {
  const header = {
    alg: algorithm,
    typ: 'JWT',
    kid: apiKeyName,
  };

  const now = Math.floor(Date.now() / 1000);
  const payload = {
    iss: 'cdp',
    sub: apiKeyName,
    iat: now,
    nbf: now,
    exp: now + 120,
    uri: `${method.toUpperCase()} ${host}${path}`,
    nonce: crypto.randomUUID(),
  };

  const encodedHeader = base64UrlEncode(JSON.stringify(header));
  const encodedPayload = base64UrlEncode(JSON.stringify(payload));
  const signingInput = `${encodedHeader}.${encodedPayload}`;
  const signature = signJwt(signingInput, privateKeyPem, algorithm);

  return `${signingInput}.${base64UrlEncode(signature)}`;
}

async function placeMarketOrder({ state, order, env = process.env, fetchImpl = fetch }) {
  const host = state.coinbase.apiHost || env.COINBASE_API_HOST || 'api.coinbase.com';
  const apiKeyName = env.COINBASE_API_KEY_NAME;
  const privateKeyPem = env.COINBASE_PRIVATE_KEY;
  const algorithm = env.COINBASE_JWT_ALGORITHM || state.coinbase.jwtAlgorithm || 'ES256';

  if (!apiKeyName || !privateKeyPem) {
    throw badRequest('Coinbase credentials are not configured in the environment');
  }

  const path = '/api/v3/brokerage/orders';
  const jwt = createJwt({
    apiKeyName,
    privateKeyPem,
    algorithm,
    method: 'POST',
    host,
    path,
  });

  const payload = {
    client_order_id: crypto.randomUUID(),
    product_id: order.productId,
    side: String(order.side || '').toUpperCase(),
    order_configuration: {
      market_market_ioc: order.baseSize
        ? { base_size: String(order.baseSize) }
        : { quote_size: String(order.quoteSizeUsd) },
    },
  };

  const response = await fetchImpl(`https://${host}${path}`, {
    method: 'POST',
    headers: {
      Authorization: `${'Bearer'} ${jwt}`,
      'CB-ACCESS-KEY': apiKeyName,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(payload),
  });

  const rawText = await response.text();
  let parsedBody;
  try {
    parsedBody = rawText ? JSON.parse(rawText) : {};
  } catch {
    parsedBody = { rawText };
  }

  if (!response.ok) {
    throw badGateway(`Coinbase order failed (${response.status})`, 'Coinbase order request failed');
  }

  return {
    request: payload,
    response: parsedBody,
  };
}

module.exports = {
  createJwt,
  placeMarketOrder,
};
