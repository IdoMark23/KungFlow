importScripts("apiClient.js");

console.log("Background service worker loaded");

// Temporary for testing.
// Change to 15 for production.
const METRICS_COLLECTION_INTERVAL_MINUTES = 1;
const RAW_HISTORY_RETENTION_MINUTES = 5;
const MAX_METRICS_HISTORY_ENTRIES = 120;
const ALARM_METRICS_COLLECTION = "metricsCollection";

chrome.runtime.onInstalled.addListener(async () => {
  await chrome.storage.local.set({
    isLoggedIn: false,
    accessToken: null,
    user: null,
    isActive: false,

    switchCount: 0,
    switchHistory: [],

    delCount: 0,
    delHistory: [],

    keyPressCount: 0,
    keyPressHistory: [],

    mouseMoveHistory: [],

    metricsHistory: [],
    lastMetricsWindowStatus: null
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
  if (alarm.name === ALARM_METRICS_COLLECTION) {
    const now = Date.now();
    const collectionWindowMinutes = METRICS_COLLECTION_INTERVAL_MINUTES;

    const windowStartTime = now - collectionWindowMinutes * 60 * 1000;

    const data = await chrome.storage.local.get([
      "isActive",
      "isLoggedIn",
      "accessToken",
      "switchHistory",
      "delHistory",
      "metricsHistory",
      "keyPressHistory",
      "mouseMoveHistory"
    ]);

    if (!data.isActive) return;
    if (!data.isLoggedIn || !data.accessToken) return;

    const switchHistory = data.switchHistory || [];
    const delHistory = data.delHistory || [];
    const metricsHistory = data.metricsHistory || [];
    const keyPressHistory = data.keyPressHistory || [];
    const mouseMoveHistory = data.mouseMoveHistory || [];
    const retainedHistoryStartTime =
      now - RAW_HISTORY_RETENTION_MINUTES * 60 * 1000;
    const retainedSwitchHistory = keepRecentHistory(
      switchHistory,
      retainedHistoryStartTime
    );
    const retainedDelHistory = keepRecentHistory(
      delHistory,
      retainedHistoryStartTime
    );
    const retainedKeyPressHistory = keepRecentHistory(
      keyPressHistory,
      retainedHistoryStartTime
    );
    const retainedMouseMoveHistory = keepRecentHistory(
      mouseMoveHistory,
      retainedHistoryStartTime
    );

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

    const mouseDistancePixels = mouseMoveHistory.reduce((total, item) => {
      if (!item || !item.epoch || item.epoch < windowStartTime) {
        return total;
      }

      return total + Number(item.distancePixels || 0);
    }, 0);

    // key presed per min
    const typingSpeed = keyPressCount / collectionWindowMinutes;
    const mouseSpeed =
      mouseDistancePixels / (collectionWindowMinutes * 60);

    const sample = {
      timestamp: new Date(now).toISOString(),
      epoch: now,
      openTabsCount: openTabsCount,
      tabSwitchCount: tabSwitchCount,
      deleteKeyCount: deleteKeyCount,
      keyPressCount,
      typingSpeed,
      mouseSpeed
    };

    if (isInactiveMetricsWindow(sample)) {
      await chrome.storage.local.set({
        switchHistory: retainedSwitchHistory,
        delHistory: retainedDelHistory,
        keyPressHistory: retainedKeyPressHistory,
        mouseMoveHistory: retainedMouseMoveHistory,
        lastMetricsSyncError: null,
        lastMetricsWindowStatus: {
          status: "skipped_inactive",
          timestamp: sample.timestamp
        }
      });

      console.log("Skipped inactive metrics window:", sample);
      return;
    }

    try {
      await kungFlowSendMetrics({
        accessToken: data.accessToken,
        timestamp: sample.timestamp,
        metrics: {
          openTabsCount: sample.openTabsCount,
          tabSwitchCount: sample.tabSwitchCount,
          deleteKeyCount: sample.deleteKeyCount,
          keyPressCount: sample.keyPressCount,
          typingSpeed: sample.typingSpeed,
          mouseSpeed: sample.mouseSpeed
        }
      });

      const status = await kungFlowGetCurrentStatus({
        accessToken: data.accessToken
      });

      sample.cognitiveLoadScore = status.cognitiveLoadScore;
      sample.cognitiveLoadState = status.state;
      sample.cognitiveLoadPhase = status.phase;
      sample.baselineSamplesCollected = status.baselineSamplesCollected;
      sample.baselineSamplesRequired = status.baselineSamplesRequired;
      sample.shouldSilenceNotifications = status.shouldSilenceNotifications;
      sample.syncedToServer = true;
      sample.serverSyncError = null;
      sample.windowStatus = "synced";
    } catch (error) {
      sample.syncedToServer = false;
      sample.serverSyncError = error.message;
      sample.windowStatus = "sync_failed";
      console.warn("Failed to sync metrics sample:", error);
    }

    metricsHistory.push(sample);
    const retainedMetricsHistory = metricsHistory.slice(
      -MAX_METRICS_HISTORY_ENTRIES
    );

    await chrome.storage.local.set({
      switchHistory: retainedSwitchHistory,
      delHistory: retainedDelHistory,
      keyPressHistory: retainedKeyPressHistory,
      mouseMoveHistory: retainedMouseMoveHistory,
      metricsHistory: retainedMetricsHistory,
      lastMetricsSyncError: sample.serverSyncError,
      lastMetricsWindowStatus: {
        status: sample.windowStatus,
        timestamp: sample.timestamp
      }
    });

    console.log("New metrics sample:", sample);
  }
});

function isInactiveMetricsWindow(sample) {
  return (
    Number(sample.tabSwitchCount || 0) === 0 &&
    Number(sample.deleteKeyCount || 0) === 0 &&
    Number(sample.keyPressCount || 0) === 0 &&
    Number(sample.typingSpeed || 0) === 0 &&
    Number(sample.mouseSpeed || 0) === 0
  );
}

function keepRecentHistory(history, minEpoch) {
  return (history || []).filter((item) => {
    return item && item.epoch && item.epoch >= minEpoch;
  });
}
