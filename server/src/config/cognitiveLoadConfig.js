const defaultCognitiveLoadConfig = {
  baselineSampleCount: 180,
  overloadThresholdMultiplier: 1.25,
  baselineEmaAlpha: 0.35,
  metricWeights: {
    openTabsCount: 1,
    openWindowsCount: 1,
    tabSwitchCount: 4,
    windowSwitchCount: 4,
    deleteKeyCount: 3,
    keyPressCount: 0.2,
    typingSpeed: 0.5,
    mouseSpeed: 0.1
  }
};

const demoCognitiveLoadConfig = {
  ...defaultCognitiveLoadConfig,
  baselineSampleCount: 3,
  overloadThresholdMultiplier: 1.1
};

const isDemoModeEnabled = process.env.KUNGFLOW_DEMO_MODE === "true";
const cognitiveLoadConfig = isDemoModeEnabled
  ? demoCognitiveLoadConfig
  : defaultCognitiveLoadConfig;

module.exports = {
  cognitiveLoadConfig,
  defaultCognitiveLoadConfig,
  demoCognitiveLoadConfig,
  isDemoModeEnabled
};
