const countEl = document.getElementById("count");
const delCountEl = document.getElementById("delCount");

const openTabsCountEl = document.getElementById("openTabsCount");
const tabSwitchCountEl = document.getElementById("tabSwitchCount");
const keyPressCountEl = document.getElementById("keyPressCount");
const deleteKeyCountEl = document.getElementById("deleteKeyCount");
const typingSpeedEl = document.getElementById("typingSpeed");
const mouseSpeedEl = document.getElementById("mouseSpeed");
const cognitiveLoadScoreEl = document.getElementById("cognitiveLoadScore");
const serverSyncStatusEl = document.getElementById("serverSyncStatus");
const lastMetricsWindowStatusEl = document.getElementById(
  "lastMetricsWindowStatus"
);
const cognitiveLoadStateEl = document.getElementById("cognitiveLoadState");
const notificationSilenceStatusEl = document.getElementById(
  "notificationSilenceStatus"
);
const serverSyncErrorRowEl = document.getElementById("serverSyncErrorRow");
const serverSyncErrorEl = document.getElementById("serverSyncError");

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
    "metricsHistory",
    "lastMetricsWindowStatus"
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

    const mouseSpeed = lastSample.mouseSpeed ?? 0;
    mouseSpeedEl.textContent = Number(mouseSpeed).toFixed(1);

    cognitiveLoadScoreEl.textContent =
      lastSample.cognitiveLoadScore ?? "Not calculated yet";
    updateServerStatus(lastSample, data.lastMetricsWindowStatus);
  } else {
    openTabsCountEl.textContent = 0;
    tabSwitchCountEl.textContent = 0;
    keyPressCountEl.textContent = 0;
    deleteKeyCountEl.textContent = 0;
    typingSpeedEl.textContent = 0;
    mouseSpeedEl.textContent = 0;
    cognitiveLoadScoreEl.textContent = "Not calculated yet";
    updateServerStatus(null, data.lastMetricsWindowStatus);
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

function updateServerStatus(lastSample, lastMetricsWindowStatus) {
  updateLastWindowStatus(lastMetricsWindowStatus);

  if (!lastSample) {
    setStatusValue(serverSyncStatusEl, "Not synced yet", "");
    setStatusValue(cognitiveLoadStateEl, "Unknown", "");
    setStatusValue(notificationSilenceStatusEl, "Not silenced", "");
    serverSyncErrorRowEl.style.display = "none";
    serverSyncErrorEl.textContent = "None";
    return;
  }

  if (lastSample.syncedToServer) {
    setStatusValue(serverSyncStatusEl, "Synced", "good");
  } else {
    setStatusValue(serverSyncStatusEl, "Not synced", "bad");
  }

  setStatusValue(
    cognitiveLoadStateEl,
    formatCognitiveLoadState(lastSample),
    getCognitiveLoadStateClass(lastSample.cognitiveLoadState)
  );

  if (lastSample.shouldSilenceNotifications) {
    setStatusValue(notificationSilenceStatusEl, "Should silence", "warn");
  } else {
    setStatusValue(notificationSilenceStatusEl, "Not silenced", "good");
  }

  if (lastSample.serverSyncError) {
    serverSyncErrorRowEl.style.display = "flex";
    serverSyncErrorEl.textContent = lastSample.serverSyncError;
  } else {
    serverSyncErrorRowEl.style.display = "none";
    serverSyncErrorEl.textContent = "None";
  }
}

function updateLastWindowStatus(lastMetricsWindowStatus) {
  if (!lastMetricsWindowStatus) {
    setStatusValue(lastMetricsWindowStatusEl, "No sample yet", "");
    return;
  }

  if (lastMetricsWindowStatus.status === "skipped_inactive") {
    setStatusValue(lastMetricsWindowStatusEl, "Skipped inactive", "warn");
    return;
  }

  if (lastMetricsWindowStatus.status === "synced") {
    setStatusValue(lastMetricsWindowStatusEl, "Active sample saved", "good");
    return;
  }

  if (lastMetricsWindowStatus.status === "sync_failed") {
    setStatusValue(lastMetricsWindowStatusEl, "Sync failed", "bad");
    return;
  }

  setStatusValue(lastMetricsWindowStatusEl, "Unknown", "");
}

function setStatusValue(element, text, className) {
  element.textContent = text;
  element.className = `status-value ${className}`.trim();
}

function formatCognitiveLoadState(sample) {
  if (!sample.cognitiveLoadState) {
    return "Unknown";
  }

  if (sample.cognitiveLoadState === "collecting_baseline") {
    return "Collecting baseline";
  }

  if (sample.cognitiveLoadState === "no_metrics") {
    return "No metrics yet";
  }

  return sample.cognitiveLoadState
    .split("_")
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(" ");
}

function getCognitiveLoadStateClass(state) {
  if (state === "normal" || state === "collecting_baseline") {
    return "good";
  }

  if (state === "overloaded") {
    return "warn";
  }

  return "";
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
    mouseMoveHistory: [],

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
