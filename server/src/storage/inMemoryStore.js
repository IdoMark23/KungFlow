function createInMemoryStore() {
  const metricsByUserId = new Map();
  const statusByUserId = new Map();
  const usersByEmail = new Map();
  const usersById = new Map();
  const sessionsByToken = new Map();
  const cognitiveStatesByUserId = new Map();

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

    updateUserPassword(userId, passwordHash) {
      const user = usersById.get(userId);

      if (!user) {
        return null;
      }

      const updatedUser = {
        ...user,
        passwordHash
      };

      usersById.set(userId, updatedUser);
      usersByEmail.set(updatedUser.email, updatedUser);

      return updatedUser;
    },

    saveSession(session) {
      sessionsByToken.set(session.accessToken, session);

      return session;
    },

    getSessionByToken(accessToken) {
      return sessionsByToken.get(accessToken) || null;
    },

    deleteSession(accessToken) {
      return sessionsByToken.delete(accessToken);
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

    deleteMetricsSamples(userId) {
      const deletedCount = (metricsByUserId.get(userId) || []).length;
      metricsByUserId.delete(userId);

      return deletedCount;
    },

    getCognitiveState(userId) {
      return cognitiveStatesByUserId.get(userId) || null;
    },

    saveCognitiveState(cognitiveState) {
      cognitiveStatesByUserId.set(cognitiveState.userId, cognitiveState);

      return cognitiveState;
    },

    deleteCognitiveState(userId) {
      return cognitiveStatesByUserId.delete(userId) ? 1 : 0;
    },

    statusByUserId
  };
}

module.exports = { createInMemoryStore };
