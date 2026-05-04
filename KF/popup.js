const countEl = document.getElementById("count");
const delCountEl = document.getElementById("delCount");
const statusEl = document.getElementById("status");
const toggleBtn = document.getElementById("toggle");
const resetBtn = document.getElementById("reset");
const changeBgBtn = document.getElementById("changeBg");
const historyListEl = document.getElementById("historyList");

async function refresh() {
  const data = await chrome.storage.local.get([
    "isActive",
    "switchCount",
    "delCount",
    "bgMode",
    "switchHistory"
  ]);

  const isActive = data.isActive || false;
  const history = data.switchHistory || [];

  countEl.textContent = data.switchCount || 0;
  delCountEl.textContent = data.delCount || 0;

  toggleBtn.textContent = isActive ? "Stop Counter" : "Start Counter";

  statusEl.textContent = isActive ? "Active" : "Inactive";
  statusEl.className = isActive ? "status active" : "status inactive";

  if (data.bgMode === "dark") {
    document.body.style.background = "linear-gradient(135deg, #111827, #1f2937)";
    document.body.style.color = "white";
  } else {
    document.body.style.background = "linear-gradient(135deg, #e0f7ff, #f8fbff)";
    document.body.style.color = "#1f2937";
  }

  renderHistory(history);
}

function renderHistory(history) {
  historyListEl.innerHTML = "";

  if (history.length === 0) {
    historyListEl.innerHTML = `<li class="empty-history">No tab switches yet</li>`;
    return;
  }

  const lastItems = history.slice(-20).reverse();

  lastItems.forEach((item, index) => {
    const li = document.createElement("li");

    const displayTime =
      typeof item === "string"
        ? item
        : item.timestamp || "Unknown time";

    li.textContent = `${index + 1}. ${displayTime}`;
    historyListEl.appendChild(li);
  });
}

toggleBtn.addEventListener("click", async () => {
  const data = await chrome.storage.local.get("isActive");

  await chrome.storage.local.set({
    isActive: !data.isActive
  });

  refresh();
});

resetBtn.addEventListener("click", async () => {
  await chrome.storage.local.set({
    switchCount: 0,
    switchHistory: [],
    delCount: 0,
    delHistory: []
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

chrome.storage.onChanged.addListener(() => {
  refresh();
});

refresh();