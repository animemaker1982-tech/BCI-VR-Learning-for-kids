const fs = require('node:fs/promises');
const path = require('node:path');

function getRuntimeDir() {
  return process.env.QST_DATA_DIR || path.join(__dirname, '..', 'data', 'runtime');
}

function getStateFile() {
  return path.join(getRuntimeDir(), 'state.json');
}

function defaultState() {
  return {
    historicalData: {
      latestImport: null,
      imports: [],
    },
    coinbase: {
      apiHost: 'api.coinbase.com',
      jwtAlgorithm: 'ES256',
      updatedAt: null,
    },
    risk: null,
    trading: {
      manualApproval: null,
      enabled: false,
      startedAt: null,
      startedBy: null,
      stopReason: null,
      lastOrder: null,
    },
    orders: [],
  };
}

async function ensureRuntimeDir() {
  await fs.mkdir(path.join(getRuntimeDir(), 'historical'), { recursive: true });
}

async function loadState() {
  await ensureRuntimeDir();
  try {
    const content = await fs.readFile(getStateFile(), 'utf8');
    return { ...defaultState(), ...JSON.parse(content) };
  } catch (error) {
    if (error.code === 'ENOENT') {
      return defaultState();
    }
    throw error;
  }
}

async function saveState(state) {
  await ensureRuntimeDir();
  const stateFile = getStateFile();
  const tempFile = `${stateFile}.tmp`;
  await fs.writeFile(tempFile, JSON.stringify(state, null, 2));
  await fs.rename(tempFile, stateFile);
}

async function updateState(updater) {
  const state = await loadState();
  const nextState = await updater(state);
  await saveState(nextState);
  return nextState;
}

async function saveHistoricalImport(symbol, record) {
  await ensureRuntimeDir();
  const fileName = `${symbol.replace(/[^A-Za-z0-9_-]/g, '_')}-${Date.now()}.json`;
  const filePath = path.join(getRuntimeDir(), 'historical', fileName);
  await fs.writeFile(filePath, JSON.stringify(record, null, 2));
  return filePath;
}

module.exports = {
  defaultState,
  loadState,
  saveState,
  updateState,
  saveHistoricalImport,
  getRuntimeDir,
};
