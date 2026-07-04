using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

namespace QuantumMagicGarden.Quantum
{
    /// <summary>
    /// Pure C# statevector quantum simulator.
    /// Supports 1–6 qubits with exact complex amplitudes.
    /// Used for real-time puzzle logic — no network required.
    /// </summary>
    public class QuantumSimulator
    {
        // ── Public properties ────────────────────────────────────────────
        public int NumQubits { get; private set; }
        public int Dimension  => 1 << NumQubits;

        /// <summary>Full statevector, length = 2^N.</summary>
        public Complex[] StateVector { get; private set; }

        // ── Constructor ──────────────────────────────────────────────────
        public QuantumSimulator(int numQubits = 2)
        {
            if (numQubits < 1 || numQubits > 6)
                throw new ArgumentOutOfRangeException(nameof(numQubits), "Must be 1–6");
            NumQubits = numQubits;
            Reset();
        }

        /// <summary>Reset to |0…0⟩.</summary>
        public void Reset()
        {
            StateVector = new Complex[Dimension];
            StateVector[0] = Complex.One;
        }

        /// <summary>Set an arbitrary normalised state.</summary>
        public void SetState(Complex[] state)
        {
            if (state.Length != Dimension)
                throw new ArgumentException($"State length must be {Dimension}");
            StateVector = (Complex[])state.Clone();
        }

        // ── Gate application ─────────────────────────────────────────────

        /// <summary>Apply a gate to the statevector.</summary>
        public void ApplyGate(QuantumGate gate)
        {
            switch (gate.Type)
            {
                case GateType.Hadamard:    ApplySingleQubitGate(gate.Target, Matrices.H);   break;
                case GateType.PauliX:      ApplySingleQubitGate(gate.Target, Matrices.X);   break;
                case GateType.PauliY:      ApplySingleQubitGate(gate.Target, Matrices.Y);   break;
                case GateType.PauliZ:      ApplySingleQubitGate(gate.Target, Matrices.Z);   break;
                case GateType.T:           ApplySingleQubitGate(gate.Target, Matrices.T);   break;
                case GateType.S:           ApplySingleQubitGate(gate.Target, Matrices.S);   break;
                case GateType.RX:          ApplySingleQubitGate(gate.Target, Matrices.RX(gate.Angle)); break;
                case GateType.RY:          ApplySingleQubitGate(gate.Target, Matrices.RY(gate.Angle)); break;
                case GateType.RZ:          ApplySingleQubitGate(gate.Target, Matrices.RZ(gate.Angle)); break;
                case GateType.CNOT:        ApplyCNOT(gate.Control, gate.Target);            break;
                case GateType.Toffoli:     ApplyToffoli(gate.Control, gate.Control2, gate.Target); break;
                case GateType.SWAP:        ApplySWAP(gate.Control, gate.Target);            break;
                default:
                    throw new NotSupportedException($"Gate {gate.Type} not supported");
            }
        }

        /// <summary>Apply a sequence of gates.</summary>
        public void ApplyCircuit(IEnumerable<QuantumGate> circuit)
        {
            foreach (var gate in circuit)
                ApplyGate(gate);
        }

        // ── Measurement ──────────────────────────────────────────────────

        /// <summary>
        /// Measure a single qubit.  Collapses the statevector.
        /// Returns 0 or 1.
        /// </summary>
        public int MeasureQubit(int qubit)
        {
            float prob1 = 0f;
            for (int i = 0; i < Dimension; i++)
            {
                if (((i >> qubit) & 1) == 1)
                    prob1 += (float)StateVector[i].Magnitude * (float)StateVector[i].Magnitude;
            }

            int result = UnityEngine.Random.value < prob1 ? 1 : 0;
            float norm = 0f;
            for (int i = 0; i < Dimension; i++)
            {
                int bit = (i >> qubit) & 1;
                if (bit != result)
                    StateVector[i] = Complex.Zero;
                else
                    norm += (float)StateVector[i].Magnitude * (float)StateVector[i].Magnitude;
            }
            float scale = 1f / MathF.Sqrt(norm);
            for (int i = 0; i < Dimension; i++)
                StateVector[i] *= scale;

            return result;
        }

        /// <summary>Measure all qubits.  Returns array of bits (LSB = qubit 0).</summary>
        public int[] MeasureAll()
        {
            float[] probs = GetProbabilities();
            float r = UnityEngine.Random.value;
            float cumul = 0f;
            int outcome = 0;
            for (int i = 0; i < Dimension; i++)
            {
                cumul += probs[i];
                if (r <= cumul) { outcome = i; break; }
            }
            // Collapse
            for (int i = 0; i < Dimension; i++)
                StateVector[i] = (i == outcome) ? Complex.One : Complex.Zero;

            int[] bits = new int[NumQubits];
            for (int q = 0; q < NumQubits; q++)
                bits[q] = (outcome >> q) & 1;
            return bits;
        }

        // ── Derived quantities ───────────────────────────────────────────

        public float[] GetProbabilities()
        {
            var probs = new float[Dimension];
            for (int i = 0; i < Dimension; i++)
                probs[i] = (float)(StateVector[i].Real * StateVector[i].Real
                                 + StateVector[i].Imaginary * StateVector[i].Imaginary);
            return probs;
        }

        /// <summary>Bloch sphere coordinates for a single qubit (reduced density matrix).</summary>
        public (float x, float y, float z) BlochVector(int qubit)
        {
            Complex rho00 = Complex.Zero, rho01 = Complex.Zero, rho10 = Complex.Zero, rho11 = Complex.Zero;

            for (int i = 0; i < Dimension; i++)
            {
                int b = (i >> qubit) & 1;
                for (int j = 0; j < Dimension; j++)
                {
                    if ((i ^ (1 << qubit)) != (j ^ (1 << qubit)) && (i >> qubit & ~1) != (j >> qubit & ~1))
                        continue;  // off-block
                    int bj = (j >> qubit) & 1;
                    Complex amp = StateVector[i] * Complex.Conjugate(StateVector[j]);
                    if (b == 0 && bj == 0) rho00 += amp;
                    else if (b == 0 && bj == 1) rho01 += amp;
                    else if (b == 1 && bj == 0) rho10 += amp;
                    else rho11 += amp;
                }
            }

            float x = 2f * (float)rho01.Real;
            float y = 2f * (float)rho01.Imaginary;
            float z = (float)(rho00 - rho11).Real;
            return (x, y, z);
        }

        public float Fidelity(Complex[] targetState)
        {
            Complex overlap = Complex.Zero;
            for (int i = 0; i < Dimension; i++)
                overlap += Complex.Conjugate(targetState[i]) * StateVector[i];
            return (float)(overlap.Real * overlap.Real + overlap.Imaginary * overlap.Imaginary);
        }

        // ── Private gate implementations ─────────────────────────────────

        private void ApplySingleQubitGate(int qubit, Complex[,] mat)
        {
            for (int i = 0; i < Dimension; i++)
            {
                if (((i >> qubit) & 1) == 0)
                {
                    int j = i | (1 << qubit);
                    Complex a = StateVector[i];
                    Complex b = StateVector[j];
                    StateVector[i] = mat[0, 0] * a + mat[0, 1] * b;
                    StateVector[j] = mat[1, 0] * a + mat[1, 1] * b;
                }
            }
        }

        private void ApplyCNOT(int control, int target)
        {
            for (int i = 0; i < Dimension; i++)
            {
                if (((i >> control) & 1) == 1 && ((i >> target) & 1) == 0)
                {
                    int j = i ^ (1 << target);
                    (StateVector[i], StateVector[j]) = (StateVector[j], StateVector[i]);
                }
            }
        }

        private void ApplyToffoli(int ctrl1, int ctrl2, int target)
        {
            for (int i = 0; i < Dimension; i++)
            {
                if (((i >> ctrl1) & 1) == 1 && ((i >> ctrl2) & 1) == 1 && ((i >> target) & 1) == 0)
                {
                    int j = i ^ (1 << target);
                    (StateVector[i], StateVector[j]) = (StateVector[j], StateVector[i]);
                }
            }
        }

        private void ApplySWAP(int q1, int q2)
        {
            for (int i = 0; i < Dimension; i++)
            {
                if (((i >> q1) & 1) == 0 && ((i >> q2) & 1) == 1)
                {
                    int j = (i ^ (1 << q1)) | (1 << q2) ^ (1 << q2);  // swap bits
                    j = i ^ (1 << q1) ^ (1 << q2);
                    (StateVector[i], StateVector[j]) = (StateVector[j], StateVector[i]);
                }
            }
        }
    }

    // ── Gate matrices ────────────────────────────────────────────────────

    public static class Matrices
    {
        private static readonly float INV_SQRT2 = 1f / MathF.Sqrt(2f);

        public static readonly Complex[,] H = {
            { INV_SQRT2,  INV_SQRT2 },
            { INV_SQRT2, -INV_SQRT2 },
        };

        public static readonly Complex[,] X = {
            { 0, 1 },
            { 1, 0 },
        };

        public static readonly Complex[,] Y = {
            { 0,                  new Complex(0, -1) },
            { new Complex(0, 1), 0 },
        };

        public static readonly Complex[,] Z = {
            { 1,  0 },
            { 0, -1 },
        };

        public static readonly Complex[,] T = {
            { 1, 0 },
            { 0, new Complex(INV_SQRT2, INV_SQRT2) },  // e^{iπ/4}
        };

        public static readonly Complex[,] S = {
            { 1, 0 },
            { 0, new Complex(0, 1) },
        };

        public static Complex[,] RX(float theta) => new Complex[,] {
            { MathF.Cos(theta / 2),     new Complex(0, -MathF.Sin(theta / 2)) },
            { new Complex(0, -MathF.Sin(theta / 2)), MathF.Cos(theta / 2) },
        };

        public static Complex[,] RY(float theta) => new Complex[,] {
            {  MathF.Cos(theta / 2), -MathF.Sin(theta / 2) },
            {  MathF.Sin(theta / 2),  MathF.Cos(theta / 2) },
        };

        public static Complex[,] RZ(float theta) => new Complex[,] {
            { new Complex(MathF.Cos(-theta / 2), MathF.Sin(-theta / 2)), 0 },
            { 0, new Complex(MathF.Cos(theta / 2), MathF.Sin(theta / 2)) },
        };
    }
}
