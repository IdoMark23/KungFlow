const express = require("express");
const { cognitiveLoadConfig, isDemoModeEnabled } = require("./config/cognitiveLoadConfig");
const {
  cognitiveStateToStatus,
  rebuildCognitiveState,
  updateCognitiveState
} = require("./domain/cognitiveLoad");
const { hashPassword, verifyPassword } = require("./domain/passwords");
const { createAccessToken } = require("./domain/tokens");
const { logInfo, logWarn } = require("./logger");
const { createInMemoryStore } = require("./storage/inMemoryStore");

function createApp({ store = createInMemoryStore() } = {}) {
  const app = express();

  app.locals.store = store;

  app.use((req, res, next) => {
    res.setHeader("Access-Control-Allow-Origin", "*");
    res.setHeader(
      "Access-Control-Allow-Headers",
      "content-type, authorization"
    );
    res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

    if (req.method === "OPTIONS") {
      return res.sendStatus(204);
    }

    next();
  });

  app.use(express.json());

  app.use((req, res, next) => {
    const startedAt = Date.now();

    res.on("finish", () => {
      logInfo("http_request", {
        method: req.method,
        path: req.path,
        statusCode: res.statusCode,
        durationMs: Date.now() - startedAt
      });
    });

    next();
  });

  app.get("/health", (req, res) => {
    res.json({
      status: "ok",
      service: "kungflow-server",
      demoMode: isDemoModeEnabled,
      baselineSampleCount: cognitiveLoadConfig.baselineSampleCount
    });
  });

  app.post("/api/auth/register", asyncHandler(async (req, res) => {
    const { email, username, password } = req.body || {};

    if (!email || typeof email !== "string") {
      return res.status(400).json({
        error: "email is required."
      });
    }

    if (!password || typeof password !== "string" || password.length < 8) {
      return res.status(400).json({
        error: "password must be at least 8 characters."
      });
    }

    const normalizedEmail = email.trim().toLowerCase();
    const existingUser = await app.locals.store.getUserByEmail(normalizedEmail);

    if (existingUser) {
      logWarn("auth_register_conflict", {
        email: normalizedEmail
      });

      return res.status(409).json({
        error: "A user with this email already exists."
      });
    }

    const user = await app.locals.store.saveUser({
      id: cryptoRandomId(),
      email: normalizedEmail,
      username: username || normalizedEmail,
      passwordHash: await hashPassword(password),
      createdAt: new Date().toISOString()
    });

    logInfo("auth_register_success", {
      userId: user.id,
      email: user.email
    });

    res.status(201).json({
      user: toPublicUser(user)
    });
  }));

  app.post("/api/auth/login", asyncHandler(async (req, res) => {
    const { email, password, platform } = req.body || {};

    if (!email || typeof email !== "string") {
      return res.status(400).json({
        error: "email is required."
      });
    }

    if (!password || typeof password !== "string") {
      return res.status(400).json({
        error: "password is required."
      });
    }

    const normalizedEmail = email.trim().toLowerCase();
    const user = await app.locals.store.getUserByEmail(normalizedEmail);

    if (!user || !(await verifyPassword(password, user.passwordHash))) {
      logWarn("auth_login_failed", {
        email: normalizedEmail,
        platform: platform || "extension"
      });

      return res.status(401).json({
        error: "Invalid email or password."
      });
    }

    const accessToken = createAccessToken();
    await app.locals.store.saveSession({
      accessToken,
      userId: user.id,
      platform: platform || "extension",
      createdAt: new Date().toISOString()
    });

    logInfo("auth_login_success", {
      userId: user.id,
      email: user.email,
      platform: platform || "extension"
    });

    res.json({
      accessToken,
      user: toPublicUser(user)
    });
  }));

  app.post("/api/auth/logout", requireAuth, asyncHandler(async (req, res) => {
    await app.locals.store.deleteSession(req.session.accessToken);

    logInfo("auth_logout_success", {
      userId: req.user.id,
      platform: req.session.platform
    });

    res.json({
      loggedOut: true
    });
  }));

  app.post("/api/auth/change-password", requireAuth, asyncHandler(async (req, res) => {
    const { currentPassword, newPassword, confirmNewPassword } = req.body || {};

    if (!currentPassword || typeof currentPassword !== "string") {
      return res.status(400).json({
        error: "currentPassword is required."
      });
    }

    if (!newPassword || typeof newPassword !== "string" || newPassword.length < 8) {
      return res.status(400).json({
        error: "newPassword must be at least 8 characters."
      });
    }

    if (newPassword !== confirmNewPassword) {
      return res.status(400).json({
        error: "confirmNewPassword must match newPassword."
      });
    }

    if (!(await verifyPassword(currentPassword, req.user.passwordHash))) {
      logWarn("auth_change_password_failed", {
        userId: req.user.id
      });

      return res.status(401).json({
        error: "Current password is incorrect."
      });
    }

    const updatedUser = await app.locals.store.updateUserPassword(
      req.user.id,
      await hashPassword(newPassword)
    );

    logInfo("auth_change_password_success", {
      userId: req.user.id
    });

    res.json({
      passwordChanged: true,
      user: toPublicUser(updatedUser)
    });
  }));

  app.post("/api/metrics", requireAuth, asyncHandler(async (req, res) => {
    const sample = req.body;

    if (!sample || typeof sample !== "object") {
      return res.status(400).json({
        error: "Request body must be a JSON object."
      });
    }

    const metrics = sample.metrics || {};

    if (isInactiveMetricsWindow(metrics)) {
      logInfo("metrics_sample_ignored_inactive", {
        userId: req.user.id,
        platform: sample.platform || "extension",
        metricNames: Object.keys(metrics)
      });

      return res.status(202).json({
        accepted: false,
        ignored: true,
        reason: "inactive_metrics_window"
      });
    }

    const savedSample = await app.locals.store.saveMetricsSample({
      userId: req.user.id,
      platform: sample.platform || "extension",
      timestamp: sample.timestamp || new Date().toISOString(),
      metrics
    });
    const cognitiveState = await updateStoredCognitiveState(req, savedSample);
    const status = cognitiveStateToStatus(cognitiveState);

    logInfo("metrics_sample_received", {
      userId: req.user.id,
      platform: savedSample.platform,
      sessionId: savedSample.sessionId,
      timestamp: savedSample.timestamp,
      metricNames: Object.keys(savedSample.metrics)
    });

    res.status(201).json({
      accepted: true,
      sample: savedSample,
      status
    });
  }));

  app.get("/api/status/current", requireAuth, asyncHandler(async (req, res) => {
    const cognitiveState = await getStoredOrRebuiltCognitiveState(req);
    const status = cognitiveStateToStatus(cognitiveState);

    logInfo("status_current_requested", {
      userId: req.user.id,
      samplesCount: cognitiveState?.samplesCollected || 0,
      phase: status.phase,
      state: status.state,
      cognitiveLoadScore: status.cognitiveLoadScore,
      baselineScore: status.baselineScore,
      shouldSilenceNotifications: status.shouldSilenceNotifications
    });

    res.json({
      userId: req.user.id,
      ...status
    });
  }));

  app.post("/api/demo/baseline", requireAuth, asyncHandler(async (req, res) => {
    if (!isDemoModeEnabled) {
      return res.status(404).json({
        error: "Demo mode is not enabled."
      });
    }

    const savedSamples = await saveDemoSamples(req, [
      createDemoMetrics({ tabSwitchCount: 2, keyPressCount: 8, typingSpeed: 8, mouseSpeed: 80 }),
      createDemoMetrics({ tabSwitchCount: 3, keyPressCount: 10, typingSpeed: 10, mouseSpeed: 90 }),
      createDemoMetrics({ tabSwitchCount: 2, keyPressCount: 9, typingSpeed: 9, mouseSpeed: 85 })
    ]);
    const cognitiveState = await req.app.locals.store.getCognitiveState(req.user.id);
    const status = cognitiveStateToStatus(cognitiveState);

    logInfo("demo_baseline_created", {
      userId: req.user.id,
      samplesCreated: savedSamples.length
    });

    res.status(201).json({
      created: savedSamples.length,
      status
    });
  }));

  app.post("/api/demo/overload", requireAuth, asyncHandler(async (req, res) => {
    if (!isDemoModeEnabled) {
      return res.status(404).json({
        error: "Demo mode is not enabled."
      });
    }

    const savedSamples = await saveDemoSamples(req, [
      createDemoMetrics({
        openTabsCount: 12,
        tabSwitchCount: 22,
        deleteKeyCount: 8,
        keyPressCount: 80,
        typingSpeed: 70,
        mouseSpeed: 420
      })
    ]);
    const cognitiveState = await req.app.locals.store.getCognitiveState(req.user.id);
    const status = cognitiveStateToStatus(cognitiveState);

    logInfo("demo_overload_created", {
      userId: req.user.id,
      samplesCreated: savedSamples.length,
      state: status.state,
      shouldSilenceNotifications: status.shouldSilenceNotifications
    });

    res.status(201).json({
      created: savedSamples.length,
      status
    });
  }));

  app.post("/api/demo/reset-metrics", requireAuth, asyncHandler(async (req, res) => {
    if (!isDemoModeEnabled) {
      return res.status(404).json({
        error: "Demo mode is not enabled."
      });
    }

    const deletedCount = await app.locals.store.deleteMetricsSamples(req.user.id);
    await app.locals.store.deleteCognitiveState(req.user.id);
    const status = cognitiveStateToStatus(null);

    logInfo("demo_metrics_reset", {
      userId: req.user.id,
      deletedCount
    });

    res.json({
      deletedCount,
      status
    });
  }));

  app.use((error, req, res, next) => {
    logWarn("http_request_failed", {
      method: req.method,
      path: req.path,
      error: error.message
    });

    res.status(500).json({
      error: "Internal server error."
    });
  });

  return app;
}

function cryptoRandomId() {
  return `user_${createAccessToken().slice(0, 16)}`;
}

function toPublicUser(user) {
  return {
    id: user.id,
    email: user.email,
    username: user.username,
    createdAt: user.createdAt
  };
}

function isInactiveMetricsWindow(metrics) {
  return (
    Number(metrics.tabSwitchCount || 0) === 0 &&
    Number(metrics.deleteKeyCount || 0) === 0 &&
    Number(metrics.keyPressCount || 0) === 0 &&
    Number(metrics.typingSpeed || 0) === 0 &&
    Number(metrics.mouseSpeed || 0) === 0
  );
}

async function getStoredOrRebuiltCognitiveState(req) {
  const existingState = await req.app.locals.store.getCognitiveState(req.user.id);

  if (existingState) {
    return existingState;
  }

  const samples = await req.app.locals.store.getMetricsSamples(req.user.id);

  if (samples.length === 0) {
    return null;
  }

  const rebuiltState = rebuildCognitiveState(samples);
  return req.app.locals.store.saveCognitiveState(rebuiltState);
}

async function updateStoredCognitiveState(req, sample) {
  const previousState = await req.app.locals.store.getCognitiveState(req.user.id);
  const nextState = updateCognitiveState(previousState, sample);

  return req.app.locals.store.saveCognitiveState(nextState);
}

async function saveDemoSamples(req, metricsSamples) {
  const baseTimestamp = Date.now();
  const savedSamples = [];

  for (const [index, metrics] of metricsSamples.entries()) {
    const savedSample = await req.app.locals.store.saveMetricsSample({
      userId: req.user.id,
      platform: "extension",
      timestamp: new Date(baseTimestamp + index * 1000).toISOString(),
      metrics
    });
    await updateStoredCognitiveState(req, savedSample);
    savedSamples.push(savedSample);
  }

  return savedSamples;
}

function createDemoMetrics(overrides = {}) {
  return {
    openTabsCount: 5,
    tabSwitchCount: 2,
    deleteKeyCount: 1,
    keyPressCount: 10,
    typingSpeed: 10,
    mouseSpeed: 90,
    ...overrides
  };
}

function asyncHandler(handler) {
  return (req, res, next) => {
    Promise.resolve(handler(req, res, next)).catch(next);
  };
}

async function requireAuth(req, res, next) {
  const authorization = req.get("authorization") || "";
  const [scheme, accessToken] = authorization.split(" ");

  if (scheme !== "Bearer" || !accessToken) {
    logWarn("auth_missing_bearer_token", {
      method: req.method,
      path: req.path
    });

    return res.status(401).json({
      error: "Authorization Bearer token is required."
    });
  }

  try {
    const session = await req.app.locals.store.getSessionByToken(accessToken);

    if (!session) {
      logWarn("auth_invalid_token", {
        method: req.method,
        path: req.path
      });

      return res.status(401).json({
        error: "Invalid or expired access token."
      });
    }

    const user = await req.app.locals.store.getUserById(session.userId);

    if (!user) {
      logWarn("auth_session_user_missing", {
        userId: session.userId
      });

      return res.status(401).json({
        error: "Session user no longer exists."
      });
    }

    req.session = session;
    req.user = user;
    next();
  } catch (error) {
    next(error);
  }
}

module.exports = { createApp };
