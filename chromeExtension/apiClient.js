const KUNGFLOW_API_BASE_URL = "http://127.0.0.1:3000";

async function kungFlowApiRequest(path, options = {}) {
  const response = await fetch(`${KUNGFLOW_API_BASE_URL}${path}`, {
    ...options,
    headers: {
      "content-type": "application/json",
      ...(options.headers || {})
    }
  });
  const body = await response.json().catch(() => ({}));

  if (!response.ok) {
    throw new Error(body.error || "KungFlow server request failed.");
  }

  return body;
}

async function kungFlowRegister({ email, username, password }) {
  return kungFlowApiRequest("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({
      email,
      username,
      password
    })
  });
}

async function kungFlowLogin({ email, password, platform = "extension" }) {
  return kungFlowApiRequest("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({
      email,
      password,
      platform
    })
  });
}

async function kungFlowAuthorizedApiRequest(path, accessToken, options = {}) {
  return kungFlowApiRequest(path, {
    ...options,
    headers: {
      authorization: `Bearer ${accessToken}`,
      ...(options.headers || {})
    }
  });
}

async function kungFlowSendMetrics({
  accessToken,
  timestamp,
  platform = "extension",
  metrics
}) {
  return kungFlowAuthorizedApiRequest("/api/metrics", accessToken, {
    method: "POST",
    body: JSON.stringify({
      timestamp,
      platform,
      metrics
    })
  });
}

async function kungFlowGetCurrentStatus({ accessToken }) {
  return kungFlowAuthorizedApiRequest("/api/status/current", accessToken);
}

async function kungFlowLogout({ accessToken }) {
  return kungFlowAuthorizedApiRequest("/api/auth/logout", accessToken, {
    method: "POST"
  });
}

async function kungFlowChangePassword({
  accessToken,
  currentPassword,
  newPassword,
  confirmNewPassword
}) {
  return kungFlowAuthorizedApiRequest("/api/auth/change-password", accessToken, {
    method: "POST",
    body: JSON.stringify({
      currentPassword,
      newPassword,
      confirmNewPassword
    })
  });
}
