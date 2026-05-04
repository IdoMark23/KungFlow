console.log("content.js loaded");

window.addEventListener(
  "keydown",
  async (event) => {
    console.log("Key pressed:", event.key);

    if (event.key !== "Delete" && event.key !== "Backspace") return;

    const data = await chrome.storage.local.get([
      "isActive",
      "delCount",
      "delHistory"
    ]);

    if (!data.isActive) {
      console.log("Counter is not active");
      return;
    }

    const now = Date.now();
    const delHistory = data.delHistory || [];

    delHistory.push({
      key: event.key,
      timestamp: new Date(now).toLocaleString(),
      epoch: now
    });

    const newCount = (data.delCount || 0) + 1;

    await chrome.storage.local.set({
      delCount: newCount,
      delHistory: delHistory
    });

    console.log("Delete/Backspace counted:", newCount);
  },
  true
);