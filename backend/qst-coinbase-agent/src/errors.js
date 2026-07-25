function httpError(statusCode, message, publicMessage = message) {
  const error = new Error(message);
  error.statusCode = statusCode;
  error.publicMessage = publicMessage;
  return error;
}

function badRequest(message) {
  return httpError(400, message);
}

function badGateway(message, publicMessage = 'Coinbase order request failed') {
  return httpError(502, message, publicMessage);
}

module.exports = {
  httpError,
  badRequest,
  badGateway,
};
