using System;
using UnityEngine;

namespace QuantumMagicGarden.Quantum
{
    // ── Gate type enum ────────────────────────────────────────────────────

    public enum GateType
    {
        Hadamard,
        PauliX,
        PauliY,
        PauliZ,
        T,
        S,
        RX,
        RY,
        RZ,
        CNOT,
        Toffoli,
        SWAP,
    }

    // ── Gate data ─────────────────────────────────────────────────────────

    /// <summary>
    /// Immutable descriptor for a single quantum gate application.
    /// Used by QuantumSimulator and the puzzle scripts.
    /// </summary>
    [System.Serializable]
    public class QuantumGate
    {
        public GateType Type { get; }
        public int Target   { get; }
        public int Control  { get; }   // -1 if unused
        public int Control2 { get; }   // Toffoli second control
        public float Angle  { get; }   // radians, for RX/RY/RZ

        // ── Constructors ─────────────────────────────────────────────────

        private QuantumGate(GateType type, int target, int control = -1, int control2 = -1, float angle = 0f)
        {
            Type     = type;
            Target   = target;
            Control  = control;
            Control2 = control2;
            Angle    = angle;
        }

        // ── Factory methods ───────────────────────────────────────────────

        public static QuantumGate H(int qubit)                            => new(GateType.Hadamard, qubit);
        public static QuantumGate X(int qubit)                            => new(GateType.PauliX,   qubit);
        public static QuantumGate Y(int qubit)                            => new(GateType.PauliY,   qubit);
        public static QuantumGate Z(int qubit)                            => new(GateType.PauliZ,   qubit);
        public static QuantumGate T(int qubit)                            => new(GateType.T,         qubit);
        public static QuantumGate S(int qubit)                            => new(GateType.S,         qubit);
        public static QuantumGate RX(int qubit, float theta)              => new(GateType.RX,        qubit, angle: theta);
        public static QuantumGate RY(int qubit, float theta)              => new(GateType.RY,        qubit, angle: theta);
        public static QuantumGate RZ(int qubit, float theta)              => new(GateType.RZ,        qubit, angle: theta);
        public static QuantumGate CNOT(int control, int target)           => new(GateType.CNOT,      target, control);
        public static QuantumGate Toffoli(int c1, int c2, int target)     => new(GateType.Toffoli,   target, c1, c2);
        public static QuantumGate SWAP(int q1, int q2)                    => new(GateType.SWAP,      q2,     q1);

        // ── Parse from string (used by QAOA gate_sequence) ───────────────

        public static QuantumGate FromString(string label, int qubit = 0, int control = 1)
        {
            return label.ToUpperInvariant() switch
            {
                "H"       => H(qubit),
                "X"       => X(qubit),
                "Y"       => Y(qubit),
                "Z"       => Z(qubit),
                "T"       => T(qubit),
                "S"       => S(qubit),
                "CNOT"    => CNOT(control, qubit),
                "RZ"      => RZ(qubit, MathF.PI / 4f),
                "MEASURE" => throw new InvalidOperationException("MEASURE is not a gate"),
                _         => throw new ArgumentException($"Unknown gate label: {label}"),
            };
        }

        // ── Display ───────────────────────────────────────────────────────

        public string DisplayName => Type switch
        {
            GateType.Hadamard => "✨ Superposition",
            GateType.PauliX   => "🔄 Flip Spell",
            GateType.CNOT     => "🔗 Entanglement Link",
            GateType.RZ       => "🌀 Phase Shift",
            GateType.Toffoli  => "🔮 Toffoli",
            _                 => Type.ToString(),
        };

        public string LearningDescription => Type switch
        {
            GateType.Hadamard =>
                "The Hadamard gate creates superposition — both 0 and 1 at the same time! " +
                "Like a coin spinning in the air before it lands.",
            GateType.PauliX =>
                "The X gate flips your qubit — 0 becomes 1, 1 becomes 0. " +
                "It's the quantum NOT gate!",
            GateType.CNOT =>
                "The CNOT gate links two qubits. When the first is |1⟩, " +
                "it flips the second. This creates quantum entanglement!",
            GateType.RZ =>
                "The RZ gate rotates your qubit around the Z-axis of the Bloch sphere, " +
                "changing its phase without changing measurement probabilities.",
            _ => "A quantum gate that transforms your qubit state.",
        };

        public override string ToString() => $"{Type}[t={Target},c={Control}]";
    }
}
