console.log("Background service worker loaded");

const COGNITIVE_LOAD_REMINDER_INTERVAL_MINUTES = 5;
// Temporary for testing.
// Change to 15 for production.
const METRICS_COLLECTION_INTERVAL_MINUTES = 1;
const ALARM_COGNITIVE_LOAD_REMINDER = "cognitiveLoadReminder";
const ALARM_METRICS_COLLECTION = "metricsCollection";

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
	//Rotem note
  // experiment - only for MVP user self-report
  chrome.alarms.create(ALARM_COGNITIVE_LOAD_REMINDER, {
    periodInMinutes: COGNITIVE_LOAD_REMINDER_INTERVAL_MINUTES
  });

  // production - collect behavioral metrics
  chrome.alarms.create(ALARM_METRICS_COLLECTION, {
    periodInMinutes: METRICS_COLLECTION_INTERVAL_MINUTES
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
  if (alarm.name === ALARM_COGNITIVE_LOAD_REMINDER) {
    chrome.windows.create({
      url: chrome.runtime.getURL("rating.html"),
      type: "popup",
      width: 360,
      height: 320
    });

    return;
  }

  if (alarm.name === ALARM_METRICS_COLLECTION) {
    const now = Date.now();
    const collectionWindowMinutes = METRICS_COLLECTION_INTERVAL_MINUTES;

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

    // key presed per min
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

