console.log("Background service worker loaded");

chrome.runtime.onInstalled.addListener(async () => {
  await chrome.storage.local.set({
    isActive: false,
    switchCount: 0,
    switchHistory: []
  });
});

chrome.tabs.onActivated.addListener(async () => {
  const data = await chrome.storage.local.get([
    "isActive",
    "switchCount",
    "switchHistory"
  ]);

  if (!data.isActive) return;

  const now = new Date();

  const history = data.switchHistory || [];

  history.push({
    timestamp: now.toLocaleString(),
    epoch: now.getTime()
  });

  await chrome.storage.local.set({
    switchCount: (data.switchCount || 0) + 1,
    switchHistory: history
  });
});

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create("cognitiveLoadReminder", {
    periodInMinutes: 5
  });
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name !== "cognitiveLoadReminder") return;

  chrome.windows.create({
    url: chrome.runtime.getURL("rating.html"),
    type: "popup",
    width: 360,
    height: 320
  });
});

