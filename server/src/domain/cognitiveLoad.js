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

function splitSamplesByBaselineWindow(samples, baselineDurationMs) {
  const firstSampleTime = new Date(samples[0].timestamp).getTime();
  const baselineEndTime = firstSampleTime + baselineDurationMs;
  const baselineSamples = [];
  const activeSamples = [];

  samples.forEach((sample) => {
    const sampleTime = new Date(sample.timestamp).getTime();

    if (!Number.isFinite(sampleTime)) {
      return;
    }

    if (sampleTime <= baselineEndTime) {
      baselineSamples.push(sample);
    } else {
      activeSamples.push(sample);
    }
  });

  return {
    baselineSamples,
    activeSamples
  };
}

function calculateAdaptiveBaseline(samples, options) {
  const {
    baselineDurationMs,
    baselineEmaAlpha,
    metricWeights,
    includeLatestSample
  } = options;
  const { baselineSamples, activeSamples } = splitSamplesByBaselineWindow(
    samples,
    baselineDurationMs
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

  const baselineDurationMs =
    options.baselineDurationMs ?? cognitiveLoadConfig.baselineDurationMs;
  const overloadThresholdMultiplier =
    options.overloadThresholdMultiplier ??
    cognitiveLoadConfig.overloadThresholdMultiplier;
  const baselineEmaAlpha =
    options.baselineEmaAlpha ?? cognitiveLoadConfig.baselineEmaAlpha;
  const metricWeights = options.metricWeights ?? cognitiveLoadConfig.metricWeights;
  const firstSampleTime = new Date(samples[0].timestamp).getTime();
  const latestSample = samples[samples.length - 1];
  const latestSampleTime = new Date(latestSample.timestamp).getTime();
  const cognitiveLoadScore = calculateCognitiveLoadScore(
    latestSample.metrics,
    metricWeights
  );
  const stillCollectingBaseline =
    Number.isFinite(firstSampleTime) &&
    Number.isFinite(latestSampleTime) &&
    latestSampleTime - firstSampleTime < baselineDurationMs;

  if (stillCollectingBaseline) {
    return {
      phase: "baseline",
      state: "collecting_baseline",
      cognitiveLoadScore,
      baselineScore: null,
      shouldSilenceNotifications: false,
      updatedAt: latestSample.timestamp
    };
  }

  const comparisonBaselineScore = calculateAdaptiveBaseline(samples, {
    baselineDurationMs,
    baselineEmaAlpha,
    metricWeights,
    includeLatestSample: false
  });
  const updatedBaselineScore = calculateAdaptiveBaseline(samples, {
    baselineDurationMs,
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
    shouldSilenceNotifications: isOverloaded,
    updatedAt: latestSample.timestamp
  };
}

module.exports = {
  calculateCognitiveLoadScore,
  calculateExponentialMovingAverage,
  getCurrentStatus
};
