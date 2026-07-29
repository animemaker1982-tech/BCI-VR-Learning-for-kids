const { badRequest } = require('./errors');

function parseNumber(value, fieldName) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    throw badRequest(`Invalid numeric field: ${fieldName}`);
  }
  return parsed;
}

function normalizeCandle(row) {
  const date = row.date || row.timestamp || row.datetime;
  if (!date) {
    throw badRequest('Each candle requires a date field');
  }

  return {
    date: new Date(date).toISOString(),
    open: parseNumber(row.open, 'open'),
    high: parseNumber(row.high, 'high'),
    low: parseNumber(row.low, 'low'),
    close: parseNumber(row.close, 'close'),
    adjustedClose: row.adjusted_close === undefined || row.adjusted_close === null ? null : parseNumber(row.adjusted_close, 'adjusted_close'),
    volume: row.volume === undefined || row.volume === null ? null : parseNumber(row.volume, 'volume'),
  };
}

function parseCsv(text) {
  const lines = text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);

  if (lines.length < 2) {
    throw badRequest('CSV payload must include a header and at least one data row');
  }

  const headers = lines[0].split(',').map((header) => header.trim());
  return lines.slice(1).map((line) => {
    const values = line.split(',').map((value) => value.trim());
    const row = {};
    headers.forEach((header, index) => {
      row[header] = values[index];
    });
    return row;
  });
}

function extractPayload(body, contentType, query) {
  const type = (contentType || '').split(';')[0].trim().toLowerCase();
  if (type === 'text/csv') {
    return {
      source: 'eodhd',
      symbol: query.symbol,
      candles: parseCsv(body),
    };
  }

  const parsed = typeof body === 'string' ? JSON.parse(body || '{}') : body;
  if (Array.isArray(parsed)) {
    return {
      source: 'eodhd',
      symbol: query.symbol,
      candles: parsed,
    };
  }

  if (typeof parsed !== 'object' || parsed === null) {
    throw badRequest('Upload body must be a JSON object, JSON array, or CSV payload');
  }

  const csv = typeof parsed.csv === 'string' ? parseCsv(parsed.csv) : null;
  return {
    source: parsed.source || 'eodhd',
    symbol: parsed.symbol || query.symbol,
    candles: csv || parsed.candles,
    metadata: parsed.metadata || {},
  };
}

function summarizeCandles(symbol, source, candles, metadata = {}) {
  if (!symbol || typeof symbol !== 'string') {
    throw badRequest('A symbol is required for historical data imports');
  }
  if (!Array.isArray(candles) || candles.length === 0) {
    throw badRequest('Historical upload must contain at least one candle');
  }

  const normalized = candles.map(normalizeCandle).sort((a, b) => a.date.localeCompare(b.date));
  return {
    source,
    symbol,
    importedAt: new Date().toISOString(),
    rowCount: normalized.length,
    dateRange: {
      start: normalized[0].date,
      end: normalized[normalized.length - 1].date,
    },
    closeRange: {
      min: Math.min(...normalized.map((entry) => entry.close)),
      max: Math.max(...normalized.map((entry) => entry.close)),
    },
    metadata,
    candles: normalized,
  };
}

module.exports = {
  extractPayload,
  summarizeCandles,
};
