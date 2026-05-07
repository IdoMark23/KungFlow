function createInMemoryStore() {
  const metricsByUserId = new Map();
  const statusByUserId = new Map();
  const usersByEmail = new Map();
  const usersById = new Map();
  const sessionsByToken = new Map();

  return {
    saveUser(user) {
      usersByEmail.set(user.email, user);
      usersById.set(user.id, user);

      return user;
    },

    getUserByEmail(email) {
      return usersByEmail.get(email) || null;
    },

    getUserById(userId) {
      return usersById.get(userId) || null;
    },

    saveSession(session) {
      sessionsByToken.set(session.accessToken, session);

      return session;
    },

    getSessionByToken(accessToken) {
      return sessionsByToken.get(accessToken) || null;
    },

    saveMetricsSample(sample) {
      const existingSamples = metricsByUserId.get(sample.userId) || [];
      existingSamples.push(sample);
      metricsByUserId.set(sample.userId, existingSamples);

      return sample;
    },

    getMetricsSamples(userId) {
      return metricsByUserId.get(userId) || [];
    },

    statusByUserId
  };
}

module.exports = { createInMemoryStore };
