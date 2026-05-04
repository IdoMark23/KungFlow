const loginForm = document.getElementById("loginForm");
const emailInput = document.getElementById("email");

async function redirectIfLoggedIn() {
  const data = await chrome.storage.local.get("isLoggedIn");

  if (data.isLoggedIn) {
    window.location.href = "popup.html";
  }
}

loginForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const email = emailInput.value.trim() || "fake-user@kungflow.local";

  await chrome.storage.local.set({
    isLoggedIn: true,
    fakeUser: {
      email,
      loggedInAt: new Date().toLocaleString()
    }
  });

  window.location.href = "popup.html";
});

redirectIfLoggedIn();
