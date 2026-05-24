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

function createNoMetricsStatus(options = {}) {
  return {
    phase: "unknown",
    state: "no_metrics",
    cognitiveLoadScore: null,
    baselineScore: null,
    comparisonBaselineScore: null,
    baselineSamplesCollected: 0,
    baselineSamplesRequired:
      options.baselineSampleCount ?? cognitiveLoadConfig.baselineSampleCount,
    shouldSilenceNotifications: false,
    updatedAt: null
  };
}

function updateCognitiveState(previousState, sample, options = {}) {
  const baselineSampleCount =
    options.baselineSampleCount ?? cognitiveLoadConfig.baselineSampleCount;
  const overloadThresholdMultiplier =
    options.overloadThresholdMultiplier ??
    cognitiveLoadConfig.overloadThresholdMultiplier;
  const baselineEmaAlpha =
    options.baselineEmaAlpha ?? cognitiveLoadConfig.baselineEmaAlpha;
  const metricWeights = options.metricWeights ?? cognitiveLoadConfig.metricWeights;
  const previousSamplesCollected = Number(previousState?.samplesCollected || 0);
  const samplesCollected = previousSamplesCollected + 1;
  const cognitiveLoadScore = calculateCognitiveLoadScore(
    sample.metrics,
    metricWeights
  );
  const previousBaselineScore = Number.isFinite(Number(previousState?.baselineScore))
    ? Number(previousState.baselineScore)
    : null;

  if (samplesCollected < baselineSampleCount) {
    return {
      userId: sample.userId,
      samplesCollected,
      phase: "baseline",
      state: "collecting_baseline",
      cognitiveLoadScore,
      baselineScore: calculateRunningAverage(
        previousBaselineScore,
        previousSamplesCollected,
        cognitiveLoadScore
      ),
      comparisonBaselineScore: null,
      shouldSilenceNotifications: false,
      updatedAt: sample.timestamp
    };
  }

  if (samplesCollected === baselineSampleCount) {
    const baselineScore = calculateRunningAverage(
      previousBaselineScore,
      previousSamplesCollected,
      cognitiveLoadScore
    );
    const isOverloaded = isScoreOverloaded(
      cognitiveLoadScore,
      baselineScore,
      overloadThresholdMultiplier
    );

    return {
      userId: sample.userId,
      samplesCollected,
      phase: "active",
      state: isOverloaded ? "overloaded" : "normal",
      cognitiveLoadScore,
      baselineScore,
      comparisonBaselineScore: baselineScore,
      shouldSilenceNotifications: isOverloaded,
      updatedAt: sample.timestamp
    };
  }

  const comparisonBaselineScore = previousBaselineScore;
  const baselineScore = calculateExponentialMovingAverage(
    previousBaselineScore,
    cognitiveLoadScore,
    baselineEmaAlpha
  );
  const isOverloaded = isScoreOverloaded(
    cognitiveLoadScore,
    comparisonBaselineScore,
    overloadThresholdMultiplier
  );

  return {
    userId: sample.userId,
    samplesCollected,
    phase: "active",
    state: isOverloaded ? "overloaded" : "normal",
    cognitiveLoadScore,
    baselineScore,
    comparisonBaselineScore,
    shouldSilenceNotifications: isOverloaded,
    updatedAt: sample.timestamp
  };
}

function rebuildCognitiveState(samples, options = {}) {
  return samples.reduce((state, sample) => {
    return updateCognitiveState(state, sample, options);
  }, null);
}

function cognitiveStateToStatus(cognitiveState, options = {}) {
  if (!cognitiveState) {
    return createNoMetricsStatus(options);
  }

  const baselineSampleCount =
    options.baselineSampleCount ?? cognitiveLoadConfig.baselineSampleCount;
  const isCollectingBaseline = cognitiveState.samplesCollected < baselineSampleCount;

  return {
    phase: cognitiveState.phase,
    state: cognitiveState.state,
    cognitiveLoadScore: cognitiveState.cognitiveLoadScore,
    baselineScore: isCollectingBaseline ? null : cognitiveState.baselineScore,
    comparisonBaselineScore: cognitiveState.comparisonBaselineScore,
    baselineSamplesCollected: Math.min(
      cognitiveState.samplesCollected,
      baselineSampleCount
    ),
    baselineSamplesRequired: baselineSampleCount,
    shouldSilenceNotifications: cognitiveState.shouldSilenceNotifications,
    updatedAt: cognitiveState.updatedAt
  };
}

function calculateRunningAverage(currentAverage, currentCount, nextValue) {
  if (currentAverage === null || currentCount === 0) {
    return nextValue;
  }

  return (currentAverage * currentCount + nextValue) / (currentCount + 1);
}

function isScoreOverloaded(score, baselineScore, overloadThresholdMultiplier) {
  return (
    baselineScore !== null &&
    Number.isFinite(Number(baselineScore)) &&
    score > baselineScore * overloadThresholdMultiplier
  );
}

module.exports = {
  calculateCognitiveLoadScore,
  calculateExponentialMovingAverage,
  cognitiveStateToStatus,
  createNoMetricsStatus,
  rebuildCognitiveState,
  updateCognitiveState,
  getCurrentStatus
};
