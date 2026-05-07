console.log("content.js loaded");

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