const cognitiveLoadConfig = {
  baselineDurationMs: 3 * 60 * 60 * 1000,
  overloadThresholdMultiplier: 1.25,
  baselineEmaAlpha: 0.35,
  metricWeights: {
    openTabsCount: 1,
    tabSwitchCount: 4,
    deleteKeyCount: 3,
    keyPressCount: 0.2,
    typingSpeed: 0.5
  }
};

module.exports = { cognitiveLoadConfig };
