function logInfo(event, details = {}) {
  writeLog("info", event, details);
}

function logWarn(event, details = {}) {
  writeLog("warn", event, details);
}

function writeLog(level, event, details) {
  console[level](
    JSON.stringify({
      timestamp: new Date().toISOString(),
      level,
      event,
      ...details
    })
  );
}

module.exports = {
  logInfo,
  logWarn
};
