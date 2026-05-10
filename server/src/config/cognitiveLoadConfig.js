const cognitiveLoadConfig = {
  baselineSampleCount: 180,
  overloadThresholdMultiplier: 1.25,
  baselineEmaAlpha: 0.35,
  metricWeights: {
    openTabsCount: 1,
    tabSwitchCount: 4,
    deleteKeyCount: 3,
    keyPressCount: 0.2,
    typingSpeed: 0.5,
    mouseSpeed: 0.1
  }
};

module.exports = { cognitiveLoadConfig };
