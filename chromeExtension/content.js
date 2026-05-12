console.log("content.js loaded");

const MOUSE_MOVE_SAMPLE_INTERVAL_MS = 100;
let lastMouseSample = null;
let extensionContextValid = true;

function isExtensionContextError(error) {
  return error && String(error.message || error).includes("Extension context invalidated");
}

async function getLocalStorage(keys) {
  if (
    !extensionContextValid ||
    typeof chrome === "undefined" ||
    !chrome.storage?.local
  ) {
    return null;
  }

  try {
    return await chrome.storage.local.get(keys);
  } catch (error) {
    if (isExtensionContextError(error)) {
      extensionContextValid = false;
      return null;
    }

    throw error;
  }
}

async function setLocalStorage(updates) {
  if (
    !extensionContextValid ||
    typeof chrome === "undefined" ||
    !chrome.storage?.local
  ) {
    return;
  }

  try {
    await chrome.storage.local.set(updates);
  } catch (error) {
    if (isExtensionContextError(error)) {
      extensionContextValid = false;
      return;
    }

    throw error;
  }
}

window.addEventListener(
  "keydown",
  async (event) => {
    const data = await getLocalStorage([
      "isActive",
      "keyPressCount",
      "keyPressHistory",
      "delCount",
      "delHistory"
    ]);

    if (!data) return;
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

    await setLocalStorage(updates);
  },
  true
);

window.addEventListener(
  "mousemove",
  async (event) => {
    const data = await getLocalStorage([
      "isActive",
      "mouseMoveHistory"
    ]);

    if (!data) return;
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

    await setLocalStorage({
      mouseMoveHistory
    });
  },
  true
);
