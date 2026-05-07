const countEl = document.getElementById("count");
const delCountEl = document.getElementById("delCount");

const openTabsCountEl = document.getElementById("openTabsCount");
const tabSwitchCountEl = document.getElementById("tabSwitchCount");
const keyPressCountEl = document.getElementById("keyPressCount");
const deleteKeyCountEl = document.getElementById("deleteKeyCount");
const typingSpeedEl = document.getElementById("typingSpeed");
const cognitiveLoadScoreEl = document.getElementById("cognitiveLoadScore");

const userEmailEl = document.getElementById("userEmail");
const statusEl = document.getElementById("status");
const toggleBtn = document.getElementById("toggle");
const toggleReminderBtn = document.getElementById("toggleReminder");
const resetBtn = document.getElementById("reset");
const changeBgBtn = document.getElementById("changeBg");
const logoutBtn = document.getElementById("logout");

async function refresh() {
  const data = await chrome.storage.local.get([
    "isLoggedIn",
    "user",
    "isActive",
    "cognitiveLoadReminderEnabled",
    "switchCount",
    "delCount",
    "bgMode",
    "metricsHistory"
  ]);

  if (!data.isLoggedIn) {
    window.location.href = "login.html";
    return;
  }

  const isActive = data.isActive || false;
  const reminderEnabled = data.cognitiveLoadReminderEnabled !== false;
  userEmailEl.textContent = data.user?.email || "Logged in";

  countEl.textContent = data.switchCount || 0;
  delCountEl.textContent = data.delCount || 0;

  const metricsHistory = data.metricsHistory || [];
  const lastSample = metricsHistory[metricsHistory.length - 1];

  if (lastSample) {
    openTabsCountEl.textContent = lastSample.openTabsCount ?? 0;
    tabSwitchCountEl.textContent = lastSample.tabSwitchCount ?? 0;
    keyPressCountEl.textContent = lastSample.keyPressCount ?? 0;
    deleteKeyCountEl.textContent = lastSample.deleteKeyCount ?? 0;

    const typingSpeed = lastSample.typingSpeed ?? 0;
    typingSpeedEl.textContent = Number(typingSpeed).toFixed(1);

    cognitiveLoadScoreEl.textContent =
      lastSample.cognitiveLoadScore ?? "Not calculated yet";
  } else {
    openTabsCountEl.textContent = 0;
    tabSwitchCountEl.textContent = 0;
    keyPressCountEl.textContent = 0;
    deleteKeyCountEl.textContent = 0;
    typingSpeedEl.textContent = 0;
    cognitiveLoadScoreEl.textContent = "Not calculated yet";
  }

  toggleBtn.textContent = isActive ? "Stop Counter" : "Start Counter";
  toggleReminderBtn.textContent = reminderEnabled
    ? "Disable Cognitive Load Popup"
    : "Enable Cognitive Load Popup";

  statusEl.textContent = isActive ? "Active" : "Inactive";
  statusEl.className = isActive ? "status active" : "status inactive";

  if (data.bgMode === "dark") {
    document.body.style.background = "linear-gradient(135deg, #111827, #1f2937)";
    document.body.style.color = "white";
  } else {
    document.body.style.background = "linear-gradient(135deg, #e0f7ff, #f8fbff)";
    document.body.style.color = "#1f2937";
  }
}

toggleBtn.addEventListener("click", async () => {
  const data = await chrome.storage.local.get("isActive");

  await chrome.storage.local.set({
    isActive: !data.isActive
  });

  refresh();
});

toggleReminderBtn.addEventListener("click", async () => {
  const data = await chrome.storage.local.get("cognitiveLoadReminderEnabled");
  const reminderEnabled = data.cognitiveLoadReminderEnabled !== false;

  await chrome.storage.local.set({
    cognitiveLoadReminderEnabled: !reminderEnabled
  });

  refresh();
});

resetBtn.addEventListener("click", async () => {
  await chrome.storage.local.set({
    switchCount: 0,
    switchHistory: [],

    delCount: 0,
    delHistory: [],

    keyPressCount: 0,
    keyPressHistory: [],

    metricsHistory: []
  });

  refresh();
});

changeBgBtn.addEventListener("click", async () => {
  const data = await chrome.storage.local.get("bgMode");

  await chrome.storage.local.set({
    bgMode: data.bgMode === "dark" ? "light" : "dark"
  });

  refresh();
});

logoutBtn.addEventListener("click", async () => {
  await chrome.storage.local.set({
    isLoggedIn: false,
    accessToken: null,
    user: null,
    isActive: false
  });

  window.location.href = "login.html";
});

chrome.storage.onChanged.addListener(() => {
  refresh();
});

refresh();
