"""
QML BCI Signal Classifier
=========================
Quantum Machine Learning pipeline (PennyLane) that classifies EEG/BCI signals
into mental states: Focus, Relax, Neutral.  Used to map BCI states to quantum
gate actions in the Magic Garden VR experience.

Pipeline:
  raw EEG (channels × samples)
      → band-power features (delta/theta/alpha/beta/gamma)
      → classical pre-processing (StandardScaler)
      → quantum kernel / variational classifier (PennyLane)
      → mental state label + confidence
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

import numpy as np
import pennylane as qml
import torch
import torch.nn as nn
import torch.optim as optim
from sklearn.preprocessing import StandardScaler

logger = logging.getLogger(__name__)

MentalState = Literal["focus", "relax", "neutral"]
N_FEATURES = 15   # 5 frequency bands × 3 EEG channels (Fp1, Fp2, Pz)
N_CLASSES = 3
N_QUBITS = 4


# ---------------------------------------------------------------------------
# Feature extraction
# ---------------------------------------------------------------------------

def extract_band_powers(
    eeg: np.ndarray,
    sfreq: float = 256.0,
    channels: int = 3,
    window_sec: float = 1.0,
) -> np.ndarray:
    """
    Compute mean log band-power for delta/theta/alpha/beta/gamma per channel.

    Args:
        eeg: array of shape (channels, n_samples)
        sfreq: sampling frequency in Hz
        channels: number of EEG channels to use
        window_sec: epoch length in seconds (applied to the tail of the signal)

    Returns:
        feature vector of shape (N_FEATURES,)
    """
    bands = {
        "delta": (1, 4),
        "theta": (4, 8),
        "alpha": (8, 13),
        "beta":  (13, 30),
        "gamma": (30, 50),
    }

    n_window = int(window_sec * sfreq)
    segment = eeg[:channels, -n_window:]          # latest 1-second window

    features: list[float] = []
    for ch in range(channels):
        sig = segment[ch] - np.mean(segment[ch])  # remove DC
        freqs = np.fft.rfftfreq(len(sig), d=1.0 / sfreq)
        psd = np.abs(np.fft.rfft(sig)) ** 2 / len(sig)

        for lo, hi in bands.values():
            mask = (freqs >= lo) & (freqs < hi)
            power = np.mean(psd[mask]) if mask.any() else 1e-10
            features.append(float(np.log1p(power)))

    return np.array(features, dtype=np.float32)


# ---------------------------------------------------------------------------
# Quantum variational classifier
# ---------------------------------------------------------------------------

dev = qml.device("default.qubit", wires=N_QUBITS)


def _angle_embedding(features: torch.Tensor) -> None:
    """Encode N_QUBITS angles via IQP-style embedding (wraps indices to tensor size)."""
    n = features.shape[0]
    for i in range(N_QUBITS):
        qml.RX(features[i % n], wires=i)
        qml.RZ(features[(i + n // 2) % n], wires=i)


def _variational_layer(weights: torch.Tensor, layer_idx: int) -> None:
    """Strongly entangling layer."""
    for i in range(N_QUBITS):
        qml.Rot(
            weights[layer_idx, i, 0],
            weights[layer_idx, i, 1],
            weights[layer_idx, i, 2],
            wires=i,
        )
    for i in range(N_QUBITS - 1):
        qml.CNOT(wires=[i, i + 1])
    qml.CNOT(wires=[N_QUBITS - 1, 0])


@qml.qnode(dev, interface="torch", diff_method="backprop")
def quantum_circuit(features: torch.Tensor, weights: torch.Tensor) -> list[torch.Tensor]:
    """Variational quantum circuit returning expectation values for 3 classes."""
    _angle_embedding(features)
    n_layers = weights.shape[0]
    for layer in range(n_layers):
        _variational_layer(weights, layer)
    return [qml.expval(qml.PauliZ(0)), qml.expval(qml.PauliZ(1)), qml.expval(qml.PauliZ(2))]


# ---------------------------------------------------------------------------
# Hybrid quantum-classical model
# ---------------------------------------------------------------------------

class QMLBCIModel(nn.Module):
    """
    Hybrid model:
        classical linear → quantum circuit (4 qubits, 3 layers) → softmax
    """

    N_LAYERS = 3

    def __init__(self) -> None:
        super().__init__()
        # Classical embedding: project N_FEATURES → N_QUBITS
        self.pre = nn.Sequential(
            nn.Linear(N_FEATURES, 16),
            nn.Tanh(),
            nn.Linear(16, N_QUBITS),
            nn.Tanh(),
        )
        # Quantum weights: (layers, qubits, 3 Euler angles)
        self.q_weights = nn.Parameter(
            torch.Tensor(self.N_LAYERS, N_QUBITS, 3).uniform_(-np.pi, np.pi)
        )
        # Classical head: map 3 PauliZ expvals → logits
        self.head = nn.Linear(3, N_CLASSES)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        emb = self.pre(x)
        # Run quantum circuit for each sample in the batch
        q_out = torch.stack(
            [torch.stack(quantum_circuit(emb[i], self.q_weights)) for i in range(x.shape[0])]
        ).float()   # PennyLane returns float64; cast to float32 for the linear head
        return self.head(q_out)


# ---------------------------------------------------------------------------
# Classifier wrapper (used by the FastAPI backend)
# ---------------------------------------------------------------------------

@dataclass
class ClassifierPrediction:
    state: MentalState
    confidence: float
    probabilities: dict[str, float]
    gate_action: str           # suggested quantum gate for Unity


_STATE_TO_GATE: dict[MentalState, str] = {
    "focus":   "H",    # Hadamard — create superposition
    "relax":   "MEASURE",
    "neutral": "NONE",
}


class BCIClassifier:
    """
    Trains and serves the QML BCI classifier.

    Usage:
        clf = BCIClassifier()
        clf.train(X_train, y_train)
        pred = clf.predict(eeg_segment)
    """

    CLASSES: list[MentalState] = ["focus", "relax", "neutral"]

    def __init__(
        self,
        lr: float = 1e-3,
        epochs: int = 50,
        batch_size: int = 16,
        model_path: str = "backend/qml/bci_model.pt",
    ) -> None:
        self.lr = lr
        self.epochs = epochs
        self.batch_size = batch_size
        self.model_path = Path(model_path)
        self.scaler = StandardScaler()
        self.model = QMLBCIModel()
        self._trained = False

    # ------------------------------------------------------------------

    def train(self, X: np.ndarray, y: np.ndarray) -> dict[str, float]:
        """
        Train model on feature matrix X (n_samples, N_FEATURES) and
        integer labels y (0=focus, 1=relax, 2=neutral).
        Returns dict with final train_loss and train_accuracy.
        """
        X_scaled = self.scaler.fit_transform(X)
        X_t = torch.tensor(X_scaled, dtype=torch.float32)
        y_t = torch.tensor(y, dtype=torch.long)

        optim_ = optim.Adam(self.model.parameters(), lr=self.lr)
        criterion = nn.CrossEntropyLoss()

        self.model.train()
        last_loss = float("inf")
        last_acc = 0.0

        for epoch in range(self.epochs):
            perm = torch.randperm(len(X_t))
            epoch_loss = 0.0
            correct = 0

            for i in range(0, len(X_t), self.batch_size):
                idx = perm[i : i + self.batch_size]
                xb, yb = X_t[idx], y_t[idx]
                optim_.zero_grad()
                logits = self.model(xb)
                loss = criterion(logits, yb)
                loss.backward()
                optim_.step()
                epoch_loss += loss.item() * len(yb)
                correct += (logits.argmax(1) == yb).sum().item()

            last_loss = epoch_loss / len(X_t)
            last_acc = correct / len(X_t)
            if epoch % 10 == 0:
                logger.info("epoch=%d loss=%.4f acc=%.3f", epoch, last_loss, last_acc)

        self._trained = True
        torch.save(
            {"model": self.model.state_dict(), "scaler": self.scaler},
            self.model_path,
        )
        return {"train_loss": last_loss, "train_accuracy": last_acc}

    def load(self) -> None:
        """Load a previously saved model."""
        if not self.model_path.exists():
            raise FileNotFoundError(f"No saved model at {self.model_path}")
        ckpt = torch.load(self.model_path, weights_only=False)
        self.model.load_state_dict(ckpt["model"])
        self.scaler = ckpt["scaler"]
        self._trained = True
        logger.info("QML BCI model loaded from %s", self.model_path)

    def predict(self, eeg: np.ndarray, sfreq: float = 256.0) -> ClassifierPrediction:
        """
        Classify a raw EEG segment (channels × samples).
        Returns ClassifierPrediction with state, confidence, and gate action.
        """
        if not self._trained:
            # Fallback to rule-based alpha/beta ratio when model not trained
            return self._rule_based_predict(eeg, sfreq)

        features = extract_band_powers(eeg, sfreq)
        x_scaled = self.scaler.transform(features.reshape(1, -1))
        x_t = torch.tensor(x_scaled, dtype=torch.float32)

        self.model.eval()
        with torch.no_grad():
            logits = self.model(x_t)
            probs = torch.softmax(logits, dim=-1).squeeze().numpy()

        idx = int(np.argmax(probs))
        state: MentalState = self.CLASSES[idx]
        return ClassifierPrediction(
            state=state,
            confidence=float(probs[idx]),
            probabilities={c: float(p) for c, p in zip(self.CLASSES, probs)},
            gate_action=_STATE_TO_GATE[state],
        )

    # ------------------------------------------------------------------

    def _rule_based_predict(self, eeg: np.ndarray, sfreq: float) -> ClassifierPrediction:
        """Simple alpha/beta ratio heuristic — works without training data."""
        feats = extract_band_powers(eeg, sfreq)
        # feats layout: [delta, theta, alpha, beta, gamma] per channel
        alpha_mean = float(np.mean(feats[2::5]))
        beta_mean = float(np.mean(feats[3::5]))

        if beta_mean > alpha_mean + 0.5:
            state: MentalState = "focus"
            conf = min(1.0, (beta_mean - alpha_mean) / 2.0)
        elif alpha_mean > beta_mean + 0.5:
            state = "relax"
            conf = min(1.0, (alpha_mean - beta_mean) / 2.0)
        else:
            state = "neutral"
            conf = 0.5

        probs = {"focus": 0.0, "relax": 0.0, "neutral": 0.0}
        probs[state] = conf
        # distribute rest
        for k in probs:
            if k != state:
                probs[k] = (1.0 - conf) / 2.0

        return ClassifierPrediction(
            state=state,
            confidence=conf,
            probabilities=probs,
            gate_action=_STATE_TO_GATE[state],
        )
