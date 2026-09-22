#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Stackmaster.Core
{
    /// <summary>The observed result of one exact planned debit.</summary>
    public sealed class ResourceDebitAttempt<TReceipt> where TReceipt : class
    {
        private ResourceDebitAttempt(bool succeeded, int removedUnits, TReceipt? receipt, string? failure)
        {
            if (removedUnits < 0) throw new ArgumentOutOfRangeException(nameof(removedUnits));
            Succeeded = succeeded;
            RemovedUnits = removedUnits;
            Receipt = receipt;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public int RemovedUnits { get; }
        public TReceipt? Receipt { get; }
        public string? Failure { get; }

        public static ResourceDebitAttempt<TReceipt> Success(int removedUnits, TReceipt receipt)
            => new ResourceDebitAttempt<TReceipt>(true, removedUnits, receipt, null);

        public static ResourceDebitAttempt<TReceipt> FailureAfterMutation(int removedUnits, TReceipt receipt, string failure)
            => new ResourceDebitAttempt<TReceipt>(false, removedUnits, receipt, failure);

        public static ResourceDebitAttempt<TReceipt> FailureWithoutMutation(string failure)
            => new ResourceDebitAttempt<TReceipt>(false, 0, default, failure);
    }

    /// <summary>
    /// Applies one immutable withdrawal plan exactly once and journals every observed mutation.
    /// The caller chooses whether to commit or restore the receipts; a partial/incorrect debit can
    /// never be mistaken for a successful charge.
    /// </summary>
    public sealed class ExactResourceDebit<TReceipt> where TReceipt : class
    {
        private readonly ResourceWithdrawalPlan _plan;
        private readonly List<TReceipt> _receipts = new List<TReceipt>();
        private bool _applied;
        private bool _finished;

        public ExactResourceDebit(ResourceWithdrawalPlan plan)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (!plan.IsSatisfiable || plan.RequiredUnits != plan.PlannedUnits)
                throw new ArgumentException("The debit plan must represent one complete exact cost.", nameof(plan));
        }

        public IReadOnlyList<TReceipt> Receipts => new ReadOnlyCollection<TReceipt>(_receipts);
        public int ExpectedUnits => _plan.RequiredUnits;
        public int RemovedUnits { get; private set; }

        public bool TryApply(
            Func<ResourceWithdrawalStep, ResourceDebitAttempt<TReceipt>> remove,
            out string? failure)
        {
            if (remove == null) throw new ArgumentNullException(nameof(remove));
            if (_applied || _finished) throw new InvalidOperationException("The exact debit has already been used.");
            _applied = true;
            failure = null;

            foreach (var step in _plan.Steps)
            {
                var attempt = remove(step) ?? throw new InvalidOperationException("The debit callback returned no result.");
                if (attempt.RemovedUnits > 0)
                {
                    if (attempt.Receipt == null)
                        throw new InvalidOperationException("A mutated debit step must provide a rollback receipt.");
                    _receipts.Add(attempt.Receipt);
                    RemovedUnits = checked(RemovedUnits + attempt.RemovedUnits);
                }

                if (!attempt.Succeeded || attempt.RemovedUnits != step.Quantity)
                {
                    failure = string.IsNullOrWhiteSpace(attempt.Failure)
                        ? "a planned resource debit was not exact"
                        : attempt.Failure;
                    return false;
                }
            }

            if (RemovedUnits != ExpectedUnits)
            {
                failure = "the complete resource debit did not equal the exact planned cost";
                return false;
            }
            return true;
        }

        public void Commit()
        {
            if (!_applied || _finished || RemovedUnits != ExpectedUnits)
                throw new InvalidOperationException("Only a complete exact debit can be committed.");
            _finished = true;
        }

        public bool Rollback(Func<TReceipt, bool> restore)
        {
            if (restore == null) throw new ArgumentNullException(nameof(restore));
            if (!_applied || _finished) return false;

            _finished = true;
            var restored = true;
            for (var index = _receipts.Count - 1; index >= 0; index--)
            {
                try
                {
                    restored &= restore(_receipts[index]);
                }
                catch
                {
                    restored = false;
                }
            }
            return restored;
        }
    }
}
