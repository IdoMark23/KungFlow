console.log("Background service worker loaded");

chrome.runtime.onInstalled.addListener(async () => {
  await chrome.storage.local.set({
    isActive: false,

    switchCount: 0,
    switchHistory: [],

    delCount: 0,
    delHistory: [],

    keyPressCount: 0,
    keyPressHistory: [],

    metricsHistory: []
  });

  // experiment - only for MVP user self-report
  chrome.alarms.create("cognitiveLoadReminder", {
    periodInMinutes: 5
  });

  // production - collect behavioral metrics
  chrome.alarms.create("metricsCollection", {
    periodInMinutes: 1
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

chrome.alarms.onAlarm.addListener(async (alarm) => {
  if (alarm.name === "cognitiveLoadReminder") {
    chrome.windows.create({
      url: chrome.runtime.getURL("rating.html"),
      type: "popup",
      width: 360,
      height: 320
    });

    return;
  }

  if (alarm.name === "metricsCollection") {
    const now = Date.now();
    // Temporary for testing.
    // Later, change this back to 15.
    const collectionWindowMinutes = 1;

    const windowStartTime = now - collectionWindowMinutes * 60 * 1000;

    const data = await chrome.storage.local.get([
      "isActive",
      "switchHistory",
      "delHistory",
      "metricsHistory",
      "keyPressHistory"
    ]);

    if (!data.isActive) return;

    const switchHistory = data.switchHistory || [];
    const delHistory = data.delHistory || [];
    const metricsHistory = data.metricsHistory || [];
    const keyPressHistory = data.keyPressHistory || [];

    const tabs = await chrome.tabs.query({});
    const openTabsCount = tabs.length;

    const tabSwitchCount = switchHistory.filter((item) => {
      return item && item.epoch && item.epoch >= windowStartTime;
    }).length;

    const deleteKeyCount = delHistory.filter((item) => {
      return item && item.epoch && item.epoch >= windowStartTime;
    }).length;

    const keyPressCount = keyPressHistory.filter((item) => {
      return item && item.epoch && item.epoch >= windowStartTime;
    }).length;

    // keypresedpermin
    const typingSpeed = keyPressCount / collectionWindowMinutes;

    const sample = {
      timestamp: new Date(now).toLocaleString(),
      epoch: now,
      openTabsCount: openTabsCount,
      tabSwitchCount: tabSwitchCount,
      deleteKeyCount: deleteKeyCount,
      keyPressCount,
      typingSpeed
    };

    metricsHistory.push(sample);

    await chrome.storage.local.set({
      metricsHistory: metricsHistory
    });

    console.log("New metrics sample:", sample);
  }
});

