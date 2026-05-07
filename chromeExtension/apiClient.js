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
