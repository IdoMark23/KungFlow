const sql = require("mssql/msnodesqlv8");

function createSqlServerStore({
  connectionString = process.env.SQLSERVER_CONNECTION_STRING
} = {}) {
  const server = process.env.SQLSERVER_SERVER || ".\\SQLEXPRESS";
  const database = process.env.SQLSERVER_DATABASE || "KungFlowDB";
  const driver = process.env.SQLSERVER_DRIVER || "ODBC Driver 17 for SQL Server";
  const connectionConfig = {
    connectionString:
      connectionString ||
      `Driver={${driver}};Server=${server};Database=${database};Trusted_Connection=Yes;Encrypt=no;`
  };
  let poolPromise = null;

  async function request() {
    if (!poolPromise) {
      poolPromise = sql.connect(connectionConfig);
    }

    const pool = await poolPromise;
    return pool.request();
  }

  return {
    async saveUser(user) {
      const result = await (await request())
        .input("Id", sql.NVarChar(64), user.id)
        .input("Email", sql.NVarChar(255), user.email)
        .input("Username", sql.NVarChar(255), user.username)
        .input("PasswordHash", sql.NVarChar(255), user.passwordHash)
        .input("CreatedAt", sql.DateTime2, new Date(user.createdAt))
        .execute("dbo.CreateUser");

      return toUser(result.recordset[0]);
    },

    async getUserByEmail(email) {
      const result = await (await request())
        .input("Email", sql.NVarChar(255), email)
        .execute("dbo.GetUserByEmail");

      return toUser(result.recordset[0]);
    },

    async getUserById(userId) {
      const result = await (await request())
        .input("Id", sql.NVarChar(64), userId)
        .execute("dbo.GetUserById");

      return toUser(result.recordset[0]);
    },

    async saveSession(session) {
      const result = await (await request())
        .input("AccessToken", sql.NVarChar(255), session.accessToken)
        .input("UserId", sql.NVarChar(64), session.userId)
        .input("Platform", sql.NVarChar(50), session.platform)
        .input("CreatedAt", sql.DateTime2, new Date(session.createdAt))
        .execute("dbo.CreateSession");

      return toSession(result.recordset[0]);
    },

    async getSessionByToken(accessToken) {
      const result = await (await request())
        .input("AccessToken", sql.NVarChar(255), accessToken)
        .execute("dbo.GetSessionByToken");

      return toSession(result.recordset[0]);
    },

    async saveMetricsSample(sample) {
      const metrics = sample.metrics || {};
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), sample.userId)
        .input("Platform", sql.NVarChar(50), sample.platform)
        .input("Timestamp", sql.DateTime2, new Date(sample.timestamp))
        .input("OpenTabsCount", sql.Int, toNullableNumber(metrics.openTabsCount))
        .input("TabSwitchCount", sql.Int, toNullableNumber(metrics.tabSwitchCount))
        .input("DeleteKeyCount", sql.Int, toNullableNumber(metrics.deleteKeyCount))
        .input("KeyPressCount", sql.Int, toNullableNumber(metrics.keyPressCount))
        .input("TypingSpeed", sql.Float, toNullableNumber(metrics.typingSpeed))
        .execute("dbo.CreateMetricsSample");

      return toMetricsSample(result.recordset[0]);
    },

    async getMetricsSamples(userId) {
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), userId)
        .execute("dbo.GetMetricsSamplesByUserId");

      return result.recordset.map(toMetricsSample);
    }
  };
}

function toUser(row) {
  if (!row) return null;

  return {
    id: row.Id,
    email: row.Email,
    username: row.Username,
    passwordHash: row.PasswordHash,
    createdAt: toIsoString(row.CreatedAt)
  };
}

function toSession(row) {
  if (!row) return null;

  return {
    accessToken: row.AccessToken,
    userId: row.UserId,
    platform: row.Platform,
    createdAt: toIsoString(row.CreatedAt)
  };
}

function toMetricsSample(row) {
  if (!row) return null;

  return {
    id: row.Id,
    userId: row.UserId,
    platform: row.Platform,
    timestamp: toIsoString(row.Timestamp),
    metrics: {
      openTabsCount: row.OpenTabsCount,
      tabSwitchCount: row.TabSwitchCount,
      deleteKeyCount: row.DeleteKeyCount,
      keyPressCount: row.KeyPressCount,
      typingSpeed: row.TypingSpeed
    },
    createdAt: toIsoString(row.CreatedAt)
  };
}

function toIsoString(value) {
  if (!value) return null;
  return value instanceof Date ? value.toISOString() : new Date(value).toISOString();
}

function toNullableNumber(value) {
  if (value === null || value === undefined || value === "") {
    return null;
  }

  const numberValue = Number(value);
  return Number.isFinite(numberValue) ? numberValue : null;
}

module.exports = { createSqlServerStore };
