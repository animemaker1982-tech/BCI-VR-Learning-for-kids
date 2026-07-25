const fs = require('node:fs/promises');
const path = require('node:path');

function getRuntimeDir(runtimeDir = process.env.QST_DATA_DIR) {
  return runtimeDir || path.join(__dirname, '..', 'data', 'runtime');
}

function getStateFile(runtimeDir) {
  return path.join(getRuntimeDir(runtimeDir), 'state.json');
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

async function ensureRuntimeDir(runtimeDir) {
  await fs.mkdir(path.join(getRuntimeDir(runtimeDir), 'historical'), { recursive: true });
}

async function loadState(runtimeDir) {
  await ensureRuntimeDir(runtimeDir);
  try {
    const content = await fs.readFile(getStateFile(runtimeDir), 'utf8');
    return { ...defaultState(), ...JSON.parse(content) };
  } catch (error) {
    if (error.code === 'ENOENT') {
      return defaultState();
    }
    throw error;
  }
}

async function saveState(state, runtimeDir) {
  await ensureRuntimeDir(runtimeDir);
  const stateFile = getStateFile(runtimeDir);
  const tempFile = `${stateFile}.tmp`;
  await fs.writeFile(tempFile, JSON.stringify(state, null, 2));
  await fs.rename(tempFile, stateFile);
}

async function updateState(updater, runtimeDir) {
  const state = await loadState(runtimeDir);
  const nextState = await updater(state);
  await saveState(nextState, runtimeDir);
  return nextState;
}

async function saveHistoricalImport(symbol, record, runtimeDir) {
  await ensureRuntimeDir(runtimeDir);
  const fileName = `${symbol.replace(/[^A-Za-z0-9_-]/g, '_')}-${Date.now()}.json`;
  const filePath = path.join(getRuntimeDir(runtimeDir), 'historical', fileName);
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
