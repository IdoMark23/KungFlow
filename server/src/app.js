const express = require("express");
const { getCurrentStatus } = require("./domain/cognitiveLoad");
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
      service: "kungflow-server"
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

    logInfo("metrics_sample_received", {
      userId: req.user.id,
      platform: savedSample.platform,
      sessionId: savedSample.sessionId,
      timestamp: savedSample.timestamp,
      metricNames: Object.keys(savedSample.metrics)
    });

    res.status(201).json({
      accepted: true,
      sample: savedSample
    });
  }));

  app.get("/api/status/current", requireAuth, asyncHandler(async (req, res) => {
    const samples = await app.locals.store.getMetricsSamples(req.user.id);
    const status = getCurrentStatus(samples);

    logInfo("status_current_requested", {
      userId: req.user.id,
      samplesCount: samples.length,
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
