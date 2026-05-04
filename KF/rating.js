document.querySelectorAll("button").forEach((button) => {
  button.addEventListener("click", async () => {
    const rating = Number(button.dataset.rating);

    const now = Date.now();
    const fiveMinutesAgo = now - 5 * 60 * 1000;

    const data = await chrome.storage.local.get([
      "switchHistory",
      "cognitiveLoadHistory",
      "delHistory"
    ]);

    const switchHistory = data.switchHistory || [];
    const cognitiveLoadHistory = data.cognitiveLoadHistory || [];
    const delHistory = data.delHistory || [];

    const tabSwitchesLastFiveMinutes = switchHistory.filter((item) => {
      return item && item.epoch && item.epoch >= fiveMinutesAgo;
    }).length;

    const delPressesLastFiveMinutes = delHistory.filter((item) => {
      return item && item.epoch && item.epoch >= fiveMinutesAgo;
    }).length;

    const ratingJson = {
      rating: rating,
      timestamp: new Date(now).toLocaleString(),
      epoch: now,
      tabSwitchesLastFiveMinutes: tabSwitchesLastFiveMinutes,
      delPressesLastFiveMinutes: delPressesLastFiveMinutes
    };

    cognitiveLoadHistory.push(ratingJson);

    await chrome.storage.local.set({
      cognitiveLoadHistory: cognitiveLoadHistory,

      switchCount: 0,
      switchHistory: [],

      delCount: 0,
      delHistory: []
    });

    window.close();
  });
});