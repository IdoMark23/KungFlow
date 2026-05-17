const countEl = document.getElementById("count");
const delCountEl = document.getElementById("delCount");

const openTabsCountEl = document.getElementById("openTabsCount");
const tabSwitchCountEl = document.getElementById("tabSwitchCount");
const keyPressCountEl = document.getElementById("keyPressCount");
const deleteKeyCountEl = document.getElementById("deleteKeyCount");
const typingSpeedEl = document.getElementById("typingSpeed");
const mouseSpeedEl = document.getElementById("mouseSpeed");
const cognitiveLoadScoreEl = document.getElementById("cognitiveLoadScore");
const cognitiveLoadMeterFillEl = document.getElementById("cognitiveLoadMeterFill");
const lastMetricsWindowStatusEl = document.getElementById(
  "lastMetricsWindowStatus"
);
const cognitiveLoadStateEl = document.getElementById("cognitiveLoadState");
const baselineProgressEl = document.getElementById("baselineProgress");
const notificationSilenceStatusEl = document.getElementById(
  "notificationSilenceStatus"
);
const serverSyncErrorRowEl = document.getElementById("serverSyncErrorRow");
const serverSyncErrorEl = document.getElementById("serverSyncError");

const userEmailEl = document.getElementById("userEmail");
const userEmailTextEl = document.getElementById("userEmailText");
const userMenuEl = document.getElementById("userMenu");
const mainNavigationEl = document.getElementById("mainNavigation");
const backNavigationEl = document.getElementById("backNavigation");
const screenBackButtonEl = document.getElementById("screenBackButton");
const mainScreenEl = document.getElementById("mainScreen");
const settingScreenEl = document.getElementById("settingScreen");
const changePasswordScreenEl = document.getElementById("changePasswordScreen");
const privacyPolicyScreenEl = document.getElementById("privacyPolicyScreen");
const statusEl = document.getElementById("status");
const statisticTabBtn = document.getElementById("statisticTab");
const settingTabBtn = document.getElementById("settingTab");
const statisticPanelEl = document.getElementById("statisticPanel");
const settingPanelEl = document.getElementById("settingPanel");
const toggleBtn = document.getElementById("toggle");
const recordButtonLabelEl = document.getElementById("recordButtonLabel");
const toggleReminderBtn = document.getElementById("toggleReminder");
const resetBtn = document.getElementById("reset");
const changeBgBtn = document.getElementById("changeBg");
const bgModeLabelEl = document.getElementById("bgModeLabel");
const filteringSelectEl = document.getElementById("filteringSelect");
const sensitivitySelectEl = document.getElementById("sensitivitySelect");
const changePasswordMenuItemBtn = document.getElementById("changePasswordMenuItem");
const settingMenuItemBtn = document.getElementById("settingMenuItem");
const privacyPolicyMenuItemBtn = document.getElementById("privacyPolicyMenuItem");
const changePasswordFormEl = document.getElementById("changePasswordForm");
const currentPasswordEl = document.getElementById("currentPassword");
const newPasswordEl = document.getElementById("newPassword");
const confirmNewPasswordEl = document.getElementById("confirmNewPassword");
const changePasswordMessageEl = document.getElementById("changePasswordMessage");
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
    "filteringLevel",
    "sensitivityLevel",
    "metricsHistory",
    "lastMetricsWindowStatus"
  ]);

  if (!data.isLoggedIn) {
    window.location.href = "login.html";
    return;
  }

  const isActive = data.isActive || false;
  const reminderEnabled = data.cognitiveLoadReminderEnabled !== false;
  userEmailTextEl.textContent = data.user?.email || "Logged in";

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

    cognitiveLoadScoreEl.textContent = formatNumber(
      lastSample.cognitiveLoadScore,
      "Not calculated yet"
    );
    updateCognitiveLoadMeter(lastSample);
    updateServerStatus(lastSample, data.lastMetricsWindowStatus);
  } else {
    openTabsCountEl.textContent = 0;
    tabSwitchCountEl.textContent = 0;
    keyPressCountEl.textContent = 0;
    deleteKeyCountEl.textContent = 0;
    typingSpeedEl.textContent = 0;
    mouseSpeedEl.textContent = 0;
    cognitiveLoadScoreEl.textContent = "Not calculated yet";
    updateCognitiveLoadMeter(null);
    updateServerStatus(null, data.lastMetricsWindowStatus);
  }

  toggleBtn.setAttribute("aria-pressed", String(isActive));
  recordButtonLabelEl.textContent = isActive ? "Stop Record" : "Start Record";
  toggleReminderBtn.setAttribute("aria-pressed", String(reminderEnabled));

  const isDarkMode = data.bgMode === "dark";
  changeBgBtn.setAttribute("aria-pressed", String(isDarkMode));
  bgModeLabelEl.textContent = isDarkMode ? "Dark Mode" : "Light Mode";
  filteringSelectEl.value = data.filteringLevel || "disable";
  sensitivitySelectEl.value = data.sensitivityLevel || "medium";

  if (isDarkMode) {
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
    setStatusValue(cognitiveLoadStateEl, "Unknown", "");
    setStatusValue(baselineProgressEl, "Unknown", "");
    setStatusValue(notificationSilenceStatusEl, "Not silenced", "");
    serverSyncErrorRowEl.style.display = "none";
    serverSyncErrorEl.textContent = "None";
    return;
  }

  setStatusValue(
    cognitiveLoadStateEl,
    formatCognitiveLoadState(lastSample),
    getCognitiveLoadStateClass(lastSample.cognitiveLoadState)
  );
  setStatusValue(
    baselineProgressEl,
    formatBaselineProgress(lastSample),
    getBaselineProgressClass(lastSample)
  );

  if (lastSample.shouldSilenceNotifications) {
    setStatusValue(notificationSilenceStatusEl, "Should silence", "warn");
  } else {
    setStatusValue(notificationSilenceStatusEl, "Not silenced", "good");
  }

  if (!lastSample.syncedToServer || lastSample.serverSyncError) {
    serverSyncErrorRowEl.style.display = "grid";
    serverSyncErrorEl.textContent =
      lastSample.serverSyncError || "Latest metrics were not saved.";
  } else {
    serverSyncErrorRowEl.style.display = "none";
    serverSyncErrorEl.textContent = "None";
  }
}

function formatNumber(value, fallback) {
  const numberValue = Number(value);
  return Number.isFinite(numberValue) ? numberValue.toFixed(2) : fallback;
}

function updateCognitiveLoadMeter(sample) {
  const meterEl = cognitiveLoadMeterFillEl.parentElement;

  if (!sample || !Number.isFinite(Number(sample.cognitiveLoadScore))) {
    cognitiveLoadMeterFillEl.style.width = "0%";
    cognitiveLoadMeterFillEl.style.background = "#22c55e";
    meterEl.setAttribute("aria-valuenow", "0");
    return;
  }

  const score = Number(sample.cognitiveLoadScore);
  const baseline = Number(sample.baselineScore || sample.comparisonBaselineScore);
  const ratio = Number.isFinite(baseline) && baseline > 0 ? score / baseline : 1;
  const normalizedRatio = Math.max(0, Math.min(ratio, 1.5));
  const percent = Math.round((normalizedRatio / 1.5) * 100);

  cognitiveLoadMeterFillEl.style.width = `${percent}%`;
  cognitiveLoadMeterFillEl.style.background = getCognitiveLoadMeterColor(ratio);
  meterEl.setAttribute("aria-valuenow", String(percent));
}

function getCognitiveLoadMeterColor(ratio) {
  if (ratio < 0.85) {
    return "#22c55e";
  }

  if (ratio < 1) {
    return "#84cc16";
  }

  if (ratio < 1.25) {
    return "#f59e0b";
  }

  return "#ef4444";
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

function formatBaselineProgress(sample) {
  const collected = sample.baselineSamplesCollected;
  const required = sample.baselineSamplesRequired;

  if (sample.cognitiveLoadPhase === "active") {
    return "Complete";
  }

  if (Number.isFinite(collected) && Number.isFinite(required)) {
    return `${collected} / ${required}`;
  }

  return "Unknown";
}

function getBaselineProgressClass(sample) {
  if (sample.cognitiveLoadPhase === "active") {
    return "good";
  }

  if (sample.cognitiveLoadPhase === "baseline") {
    return "warn";
  }

  return "";
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

function activateTab(tabName) {
  const isStatisticTab = tabName === "statistic";

  statisticTabBtn.classList.toggle("active-tab", isStatisticTab);
  settingTabBtn.classList.toggle("active-tab", !isStatisticTab);
  statisticPanelEl.classList.toggle("active-panel", isStatisticTab);
  settingPanelEl.classList.toggle("active-panel", !isStatisticTab);

  statisticTabBtn.setAttribute("aria-selected", String(isStatisticTab));
  settingTabBtn.setAttribute("aria-selected", String(!isStatisticTab));
}

statisticTabBtn.addEventListener("click", () => {
  activateTab("statistic");
});

settingTabBtn.addEventListener("click", () => {
  activateTab("setting");
});

function closeUserMenu() {
  userMenuEl.classList.remove("open");
  userEmailEl.setAttribute("aria-expanded", "false");
}

userEmailEl.addEventListener("click", (event) => {
  event.stopPropagation();
  const isOpen = userMenuEl.classList.toggle("open");
  userEmailEl.setAttribute("aria-expanded", String(isOpen));
});

document.addEventListener("click", (event) => {
  if (!userMenuEl.contains(event.target) && event.target !== userEmailEl) {
    closeUserMenu();
  }
});

function showScreen(screenName) {
  const isMainScreen = screenName === "main";

  mainNavigationEl.classList.toggle("hidden", !isMainScreen);
  backNavigationEl.classList.toggle("hidden", isMainScreen);
  mainScreenEl.classList.toggle("active-screen", screenName === "main");
  settingScreenEl.classList.toggle("active-screen", screenName === "setting");
  changePasswordScreenEl.classList.toggle(
    "active-screen",
    screenName === "changePassword"
  );
  privacyPolicyScreenEl.classList.toggle(
    "active-screen",
    screenName === "privacyPolicy"
  );

  if (screenName !== "changePassword") {
    clearChangePasswordMessage();
  }
}

function setChangePasswordMessage(message, className) {
  changePasswordMessageEl.textContent = message;
  changePasswordMessageEl.className = `form-message visible ${className}`;
}

function clearChangePasswordMessage() {
  changePasswordMessageEl.textContent = "";
  changePasswordMessageEl.className = "form-message";
}

changePasswordMenuItemBtn.addEventListener("click", () => {
  closeUserMenu();
  showScreen("changePassword");
});

settingMenuItemBtn.addEventListener("click", () => {
  closeUserMenu();
  showScreen("setting");
});

privacyPolicyMenuItemBtn.addEventListener("click", () => {
  closeUserMenu();
  showScreen("privacyPolicy");
});

screenBackButtonEl.addEventListener("click", () => {
  showScreen("main");
});

changePasswordFormEl.addEventListener("submit", async (event) => {
  event.preventDefault();
  clearChangePasswordMessage();

  const currentPassword = currentPasswordEl.value;
  const newPassword = newPasswordEl.value;
  const confirmNewPassword = confirmNewPasswordEl.value;

  if (newPassword !== confirmNewPassword) {
    setChangePasswordMessage("New passwords do not match.", "error");
    return;
  }

  const data = await chrome.storage.local.get("accessToken");

  if (!data.accessToken) {
    setChangePasswordMessage("You must be logged in to change password.", "error");
    return;
  }

  try {
    await kungFlowChangePassword({
      accessToken: data.accessToken,
      currentPassword,
      newPassword,
      confirmNewPassword
    });

    changePasswordFormEl.reset();
    setChangePasswordMessage("Password changed successfully.", "success");
  } catch (error) {
    setChangePasswordMessage(error.message, "error");
  }
});

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

filteringSelectEl.addEventListener("change", async () => {
  await chrome.storage.local.set({
    filteringLevel: filteringSelectEl.value
  });
});

sensitivitySelectEl.addEventListener("change", async () => {
  await chrome.storage.local.set({
    sensitivityLevel: sensitivitySelectEl.value
  });
});

logoutBtn.addEventListener("click", async () => {
  closeUserMenu();
  const data = await chrome.storage.local.get("accessToken");

  if (data.accessToken) {
    try {
      await kungFlowLogout({ accessToken: data.accessToken });
    } catch (error) {
      console.warn("Failed to logout from server:", error);
    }
  }

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
