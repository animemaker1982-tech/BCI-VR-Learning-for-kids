"""
Tests for QML BCI Signal Classifier
"""
import numpy as np
import pytest

from backend.qml.bci_classifier import (
    BCIClassifier,
    extract_band_powers,
    N_FEATURES,
)


@pytest.fixture
def classifier():
    return BCIClassifier(epochs=2, batch_size=4)


@pytest.fixture
def synthetic_eeg():
    """Synthetic 3-channel, 512-sample EEG at 256 Hz."""
    rng = np.random.default_rng(42)
    return rng.standard_normal((3, 512)).astype(np.float32)


# ── Feature extraction ────────────────────────────────────────────────────────

def test_band_powers_shape(synthetic_eeg):
    features = extract_band_powers(synthetic_eeg, sfreq=256.0)
    assert features.shape == (N_FEATURES,)


def test_band_powers_non_negative(synthetic_eeg):
    features = extract_band_powers(synthetic_eeg, sfreq=256.0)
    assert np.all(features >= 0)


def test_band_powers_focus_signal():
    """Synthesise a high-beta signal and verify beta power > alpha power."""
    sfreq = 256.0
    t = np.linspace(0, 2, int(2 * sfreq))
    beta_sig = np.sin(2 * np.pi * 20 * t).astype(np.float32)  # 20 Hz (beta)
    eeg = np.stack([beta_sig, beta_sig, beta_sig])
    features = extract_band_powers(eeg, sfreq)
    # alpha band is features[2], beta is features[3] (channel 0)
    assert features[3] > features[2], "Beta power should exceed alpha for beta-dominant signal"


# ── Rule-based prediction (no training) ──────────────────────────────────────

def test_rule_based_neutral(synthetic_eeg, classifier):
    pred = classifier.predict(synthetic_eeg)
    assert pred.state in ("focus", "relax", "neutral")
    assert 0.0 <= pred.confidence <= 1.0
    assert pred.gate_action in ("H", "MEASURE", "NONE")


def test_rule_based_focus_signal():
    """Strong beta → should predict focus."""
    sfreq = 256.0
    t = np.linspace(0, 2, int(2 * sfreq))
    beta = np.sin(2 * np.pi * 25 * t).astype(np.float32)
    eeg = np.stack([beta * 5, beta * 5, beta * 5])  # amplify
    classifier = BCIClassifier()
    pred = classifier.predict(eeg, sfreq)
    # Should lean toward focus (high beta)
    assert pred.probabilities["focus"] >= pred.probabilities["relax"]


def test_rule_based_relax_signal():
    """Strong alpha → should predict relax."""
    sfreq = 256.0
    t = np.linspace(0, 2, int(2 * sfreq))
    alpha = np.sin(2 * np.pi * 10 * t).astype(np.float32)
    eeg = np.stack([alpha * 5, alpha * 5, alpha * 5])
    classifier = BCIClassifier()
    pred = classifier.predict(eeg, sfreq)
    assert pred.probabilities["relax"] >= pred.probabilities["focus"]


# ── Training ─────────────────────────────────────────────────────────────────

def test_train_returns_metrics(classifier):
    rng = np.random.default_rng(0)
    X = rng.standard_normal((30, N_FEATURES)).astype(np.float32)
    y = rng.integers(0, 3, size=30)
    metrics = classifier.train(X, y)
    assert "train_loss" in metrics
    assert "train_accuracy" in metrics
    assert 0.0 <= metrics["train_accuracy"] <= 1.0


def test_trained_model_predicts(classifier, synthetic_eeg):
    rng = np.random.default_rng(1)
    X = rng.standard_normal((30, N_FEATURES)).astype(np.float32)
    y = rng.integers(0, 3, size=30)
    classifier.train(X, y)
    pred = classifier.predict(synthetic_eeg)
    assert pred.state in ("focus", "relax", "neutral")


# ── Probability sum ───────────────────────────────────────────────────────────

def test_probabilities_sum_to_one(synthetic_eeg, classifier):
    pred = classifier.predict(synthetic_eeg)
    total = sum(pred.probabilities.values())
    assert abs(total - 1.0) < 0.05
