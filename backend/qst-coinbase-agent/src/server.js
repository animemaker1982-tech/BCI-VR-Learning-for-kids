const http = require('node:http');
const { createApp } = require('./app');

async function startServer({ port = Number(process.env.PORT || 8787), env = process.env, fetchImpl = fetch } = {}) {
  const app = await createApp({ env, fetchImpl });
  const server = http.createServer(app);

  await new Promise((resolve) => server.listen(port, resolve));
  return server;
}

if (require.main === module) {
  startServer()
    .then((server) => {
      const address = server.address();
      console.log(`QST Coinbase agent listening on port ${address.port}`);
    })
    .catch((error) => {
      console.error(error);
      process.exitCode = 1;
    });
}

module.exports = {
  startServer,
};
