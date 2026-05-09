const registerForm = document.getElementById("registerForm");
const usernameInput = document.getElementById("username");
const emailInput = document.getElementById("email");
const passwordInput = document.getElementById("password");
const confirmPasswordInput = document.getElementById("confirmPassword");
const backToLoginButton = document.getElementById("backToLoginButton");
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

registerForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const username = usernameInput.value.trim();
  const email = emailInput.value.trim();
  const password = passwordInput.value;
  const confirmPassword = confirmPasswordInput.value;

  if (password !== confirmPassword) {
    setMessage("Passwords do not match.", "error");
    return;
  }

  try {
    setMessage("Creating account...");
    await kungFlowRegister({ email, username, password });

    setMessage("Logging in...");
    const authResponse = await kungFlowLogin({ email, password });
    await saveLoggedInSession(authResponse);
  } catch (error) {
    setMessage(error.message, "error");
  }
});

backToLoginButton.addEventListener("click", () => {
  window.location.href = "login.html";
});

redirectIfLoggedIn();
