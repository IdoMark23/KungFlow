const crypto = require("node:crypto");

function createAccessToken() {
  return crypto.randomBytes(32).toString("hex");
}

module.exports = { createAccessToken };
