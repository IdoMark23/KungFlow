function createInMemoryStore() {
  const metricsByUserId = new Map();
  const statusByUserId = new Map();
  const usersByEmail = new Map();
  const usersById = new Map();
  const sessionsByToken = new Map();
  const cognitiveStatesByUserId = new Map();
  const firewallEventsByUserId = new Map();
  let nextFirewallEventId = 1;

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

    saveFirewallEvent(event) {
      const savedEvent = {
        ...event,
        id: nextFirewallEventId++,
        createdAt: new Date().toISOString()
      };
      const existingEvents = firewallEventsByUserId.get(event.userId) || [];
      existingEvents.push(savedEvent);
      firewallEventsByUserId.set(event.userId, existingEvents);

      return savedEvent;
    },

    getFirewallEvents(userId, limit = 50) {
      return (firewallEventsByUserId.get(userId) || [])
        .slice()
        .sort((left, right) => Date.parse(right.occurredAt) - Date.parse(left.occurredAt))
        .slice(0, limit);
    },

    statusByUserId
  };
}

module.exports = { createInMemoryStore };
