console.log("content.js loaded");

const MOUSE_MOVE_SAMPLE_INTERVAL_MS = 100;
let lastMouseSample = null;

window.addEventListener(
  "keydown",
  async (event) => {
    const data = await chrome.storage.local.get([
      "isActive",
      "keyPressCount",
      "keyPressHistory",
      "delCount",
      "delHistory"
    ]);

    if (!data.isActive) return;

    const now = Date.now();

    const keyPressHistory = data.keyPressHistory || [];
    keyPressHistory.push({
      key: event.key,
      timestamp: new Date(now).toLocaleString(),
      epoch: now
    });

    const updates = {
      keyPressCount: (data.keyPressCount || 0) + 1,
      keyPressHistory
    };

    if (event.key === "Delete" || event.key === "Backspace") {
      const delHistory = data.delHistory || [];

      delHistory.push({
        key: event.key,
        timestamp: new Date(now).toLocaleString(),
        epoch: now
      });

      updates.delCount = (data.delCount || 0) + 1;
      updates.delHistory = delHistory;
    }

    await chrome.storage.local.set(updates);
  },
  true
);

window.addEventListener(
  "mousemove",
  async (event) => {
    const data = await chrome.storage.local.get([
      "isActive",
      "mouseMoveHistory"
    ]);

    if (!data.isActive) return;

    const now = Date.now();

    if (
      lastMouseSample &&
      now - lastMouseSample.epoch < MOUSE_MOVE_SAMPLE_INTERVAL_MS
    ) {
      return;
    }

    const currentSample = {
      x: event.clientX,
      y: event.clientY,
      epoch: now
    };

    if (!lastMouseSample) {
      lastMouseSample = currentSample;
      return;
    }

    const distancePixels = Math.hypot(
      currentSample.x - lastMouseSample.x,
      currentSample.y - lastMouseSample.y
    );

    lastMouseSample = currentSample;

    if (distancePixels === 0) {
      return;
    }

    const mouseMoveHistory = data.mouseMoveHistory || [];
    mouseMoveHistory.push({
      timestamp: new Date(now).toLocaleString(),
      epoch: now,
      distancePixels
    });

    await chrome.storage.local.set({
      mouseMoveHistory
    });
  },
  true
);
