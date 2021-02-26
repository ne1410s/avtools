// <copyright file="IntervalWorkerBase.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    /// <summary>
    /// A base class for implementing interval workers.
    /// </summary>
    internal abstract class IntervalWorkerBase : WorkerBase
    {
        private readonly StepTimer quantumTimer;

        /// <summary>
        /// Initialises a new instance of the <see cref="IntervalWorkerBase"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        protected IntervalWorkerBase(string name)
            : base(name)
        {
            this.quantumTimer = new StepTimer(this.OnQuantumTicked);
        }

        /// <inheritdoc />
        protected override void Dispose(bool alsoManaged)
        {
            base.Dispose(alsoManaged);
            this.quantumTimer.Dispose();
        }

        /// <summary>
        /// Called when every quantum of time occurs.
        /// </summary>
        private void OnQuantumTicked()
        {
            if (!this.TryBeginCycle())
            {
                return;
            }

            this.ExecuteCyle();
        }
    }
}
