const loginForm = document.getElementById("loginForm");
const emailInput = document.getElementById("email");
const passwordInput = document.getElementById("password");
const registerButton = document.getElementById("registerButton");
const messageEl = document.getElementById("message");

async function redirectIfLoggedIn() {
  const data = await chrome.storage.local.get("isLoggedIn");

  if (data.isLoggedIn) {
    window.location.href = "popup.html";
  }
}

async function saveLoggedInSession(authResponse) {
  await chrome.storage.local.set({
    isLoggedIn: true,
    accessToken: authResponse.accessToken,
    user: authResponse.user,
    fakeUser: null
  });

  window.location.href = "popup.html";
}

function setMessage(text, className = "") {
  messageEl.textContent = text;
  messageEl.className = `message ${className}`.trim();
}

loginForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const email = emailInput.value.trim();
  const password = passwordInput.value;

  try {
    setMessage("Logging in...");
    const authResponse = await kungFlowLogin({ email, password });
    await saveLoggedInSession(authResponse);
  } catch (error) {
    setMessage(error.message, "error");
  }
});

registerButton.addEventListener("click", async () => {
  const email = emailInput.value.trim();
  const password = passwordInput.value;

  try {
    setMessage("Creating account...");
    await kungFlowRegister({ email, username: email, password });
    const authResponse = await kungFlowLogin({ email, password });
    await saveLoggedInSession(authResponse);
  } catch (error) {
    setMessage(error.message, "error");
  }
});

redirectIfLoggedIn();
