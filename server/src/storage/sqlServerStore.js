const sql = require("mssql/msnodesqlv8");

function createSqlServerStore({
  connectionString = process.env.SQLSERVER_CONNECTION_STRING
} = {}) {
  const server = process.env.SQLSERVER_SERVER || "(localdb)\\MSSQLLocalDB";
  const database = process.env.SQLSERVER_DATABASE || "KungFlowDB";
  const driver = process.env.SQLSERVER_DRIVER || "ODBC Driver 17 for SQL Server";
  const connectionConfig = {
    connectionString:
      connectionString ||
      `Driver={${driver}};Server=${server};Database=${database};Trusted_Connection=Yes;Encrypt=no;TrustServerCertificate=yes;`
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

    async updateUserPassword(userId, passwordHash) {
      const result = await (await request())
        .input("Id", sql.NVarChar(64), userId)
        .input("PasswordHash", sql.NVarChar(255), passwordHash)
        .execute("dbo.UpdateUserPassword");

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

    async deleteSession(accessToken) {
      const result = await (await request())
        .input("AccessToken", sql.NVarChar(255), accessToken)
        .execute("dbo.DeleteSession");

      return Number(result.recordset[0]?.DeletedCount || 0) > 0;
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
        .input("MouseSpeed", sql.Float, toNullableNumber(metrics.mouseSpeed))
        .execute("dbo.CreateMetricsSample");

      return toMetricsSample(result.recordset[0]);
    },

    async getMetricsSamples(userId) {
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), userId)
        .execute("dbo.GetMetricsSamplesByUserId");

      return result.recordset.map(toMetricsSample);
    },

    async deleteMetricsSamples(userId) {
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), userId)
        .execute("dbo.DeleteMetricsSamplesByUserId");

      return Number(result.recordset[0]?.DeletedCount || 0);
    },

    async getCognitiveState(userId) {
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), userId)
        .execute("dbo.GetUserCognitiveState");

      return toCognitiveState(result.recordset[0]);
    },

    async saveCognitiveState(cognitiveState) {
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), cognitiveState.userId)
        .input("SamplesCollected", sql.Int, cognitiveState.samplesCollected)
        .input("Phase", sql.NVarChar(50), cognitiveState.phase)
        .input("State", sql.NVarChar(50), cognitiveState.state)
        .input("CognitiveLoadScore", sql.Float, toNullableNumber(cognitiveState.cognitiveLoadScore))
        .input("BaselineScore", sql.Float, toNullableNumber(cognitiveState.baselineScore))
        .input("ComparisonBaselineScore", sql.Float, toNullableNumber(cognitiveState.comparisonBaselineScore))
        .input("ShouldSilenceNotifications", sql.Bit, Boolean(cognitiveState.shouldSilenceNotifications))
        .input("UpdatedAt", sql.DateTime2, cognitiveState.updatedAt ? new Date(cognitiveState.updatedAt) : null)
        .execute("dbo.UpsertUserCognitiveState");

      return toCognitiveState(result.recordset[0]);
    },

    async deleteCognitiveState(userId) {
      const result = await (await request())
        .input("UserId", sql.NVarChar(64), userId)
        .execute("dbo.DeleteUserCognitiveState");

      return Number(result.recordset[0]?.DeletedCount || 0);
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
      typingSpeed: row.TypingSpeed,
      mouseSpeed: row.MouseSpeed
    },
    createdAt: toIsoString(row.CreatedAt)
  };
}

function toCognitiveState(row) {
  if (!row) return null;

  return {
    userId: row.UserId,
    samplesCollected: row.SamplesCollected,
    phase: row.Phase,
    state: row.State,
    cognitiveLoadScore: row.CognitiveLoadScore,
    baselineScore: row.BaselineScore,
    comparisonBaselineScore: row.ComparisonBaselineScore,
    shouldSilenceNotifications: Boolean(row.ShouldSilenceNotifications),
    updatedAt: toIsoString(row.UpdatedAt)
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
