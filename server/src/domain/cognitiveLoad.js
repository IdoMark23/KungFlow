const { cognitiveLoadConfig } = require("../config/cognitiveLoadConfig");

function calculateCognitiveLoadScore(
  metrics = {},
  metricWeights = cognitiveLoadConfig.metricWeights
) {
  return Object.entries(metricWeights).reduce((score, [metricName, weight]) => {
    const value = Number(metrics[metricName] || 0);

    if (!Number.isFinite(value)) {
      return score;
    }

    return score + value * weight;
  }, 0);
}

function calculateAverageScore(samples, metricWeights) {
  if (samples.length === 0) {
    return null;
  }

  const totalScore = samples.reduce((sum, sample) => {
    return sum + calculateCognitiveLoadScore(sample.metrics, metricWeights);
  }, 0);

  return totalScore / samples.length;
}

function calculateExponentialMovingAverage(
  currentAverage,
  nextValue,
  alpha
) {
  if (currentAverage === null) {
    return nextValue;
  }

  return alpha * nextValue + (1 - alpha) * currentAverage;
}

function splitSamplesByBaselineCount(samples, baselineSampleCount) {
  return {
    baselineSamples: samples.slice(0, baselineSampleCount),
    activeSamples: samples.slice(baselineSampleCount)
  };
}

function calculateAdaptiveBaseline(samples, options) {
  const {
    baselineSampleCount,
    baselineEmaAlpha,
    metricWeights,
    includeLatestSample
  } = options;
  const { baselineSamples, activeSamples } = splitSamplesByBaselineCount(
    samples,
    baselineSampleCount
  );
  const samplesForEma = includeLatestSample
    ? activeSamples
    : activeSamples.slice(0, -1);
  let baselineScore = calculateAverageScore(baselineSamples, metricWeights);

  samplesForEma.forEach((sample) => {
    const sampleScore = calculateCognitiveLoadScore(
      sample.metrics,
      metricWeights
    );
    baselineScore = calculateExponentialMovingAverage(
      baselineScore,
      sampleScore,
      baselineEmaAlpha
    );
  });

  return baselineScore;
}

function getCurrentStatus(samples, options = {}) {
  if (samples.length === 0) {
    return {
      phase: "unknown",
      state: "no_metrics",
      cognitiveLoadScore: null,
      baselineScore: null,
      shouldSilenceNotifications: false,
      updatedAt: null
    };
  }

  const baselineSampleCount =
    options.baselineSampleCount ?? cognitiveLoadConfig.baselineSampleCount;
  const overloadThresholdMultiplier =
    options.overloadThresholdMultiplier ??
    cognitiveLoadConfig.overloadThresholdMultiplier;
  const baselineEmaAlpha =
    options.baselineEmaAlpha ?? cognitiveLoadConfig.baselineEmaAlpha;
  const metricWeights = options.metricWeights ?? cognitiveLoadConfig.metricWeights;
  const latestSample = samples[samples.length - 1];
  const cognitiveLoadScore = calculateCognitiveLoadScore(
    latestSample.metrics,
    metricWeights
  );
  const stillCollectingBaseline = samples.length < baselineSampleCount;

  if (stillCollectingBaseline) {
    return {
      phase: "baseline",
      state: "collecting_baseline",
      cognitiveLoadScore,
      baselineScore: null,
      baselineSamplesCollected: samples.length,
      baselineSamplesRequired: baselineSampleCount,
      shouldSilenceNotifications: false,
      updatedAt: latestSample.timestamp
    };
  }

  const comparisonBaselineScore = calculateAdaptiveBaseline(samples, {
    baselineSampleCount,
    baselineEmaAlpha,
    metricWeights,
    includeLatestSample: false
  });
  const updatedBaselineScore = calculateAdaptiveBaseline(samples, {
    baselineSampleCount,
    baselineEmaAlpha,
    metricWeights,
    includeLatestSample: true
  });
  const isOverloaded =
    comparisonBaselineScore !== null &&
    cognitiveLoadScore >
      comparisonBaselineScore * overloadThresholdMultiplier;

  return {
    phase: "active",
    state: isOverloaded ? "overloaded" : "normal",
    cognitiveLoadScore,
    baselineScore: updatedBaselineScore,
    comparisonBaselineScore,
    baselineSamplesCollected: Math.min(samples.length, baselineSampleCount),
    baselineSamplesRequired: baselineSampleCount,
    shouldSilenceNotifications: isOverloaded,
    updatedAt: latestSample.timestamp
  };
}

module.exports = {
  calculateCognitiveLoadScore,
  calculateExponentialMovingAverage,
  getCurrentStatus
};
