const { createApp } = require("./app");
const { createSqlServerStore } = require("./storage/sqlServerStore");

const port = process.env.PORT || 3000;
const app = createApp({
  store: createSqlServerStore()
});

app.listen(port, () => {
  console.log(`KungFlow server listening on port ${port}`);
});
