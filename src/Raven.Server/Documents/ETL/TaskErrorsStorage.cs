using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Server.Documents.TransactionMerger;
using Raven.Server.Documents.TransactionMerger.Commands;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow.Binary;
using Sparrow.Json;
using Voron;
using Voron.Data.Tables;
using Transaction = Voron.Impl.Transaction;

namespace Raven.Server.Documents.ETL;

public unsafe class TaskErrorsStorage
{
    private const int ErrorsLimitPerTaskErrorType = 500;

    private const int TableSizeInPages = 16;
    private DocumentsContextPool _contextPool;
    private DocumentsTransactionOperationsMerger _txMerger;
    private HashSet<string> _tablesCreated = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(DocumentsContextPool contextPool, DocumentsTransactionOperationsMerger txMerger)
    {
        _contextPool = contextPool;
        _txMerger = txMerger;
    }

    private Table EnsureProcessErrorsTableCreated(Transaction tx, TaskErrorSource taskErrorSource, string taskName)
    {
        var tableName = GetProcessErrorsTableName(taskErrorSource, taskName);

        if (tx.IsWriteTransaction && _tablesCreated.Contains(tableName) == false)
        {
            Schemas.TaskProcessErrors.Current.Create(tx, tableName, TableSizeInPages);
            tx.LowLevelTransaction.OnDispose += _ =>
            {
                if (tx.LowLevelTransaction.Committed == false)
                    return;

                _tablesCreated = new HashSet<string>(_tablesCreated, StringComparer.OrdinalIgnoreCase) { tableName };
            };
        }

        return tx.OpenTable(Schemas.TaskProcessErrors.Current, tableName);
    }

    private Table EnsureItemErrorsTableCreated(Transaction tx, TaskErrorSource taskErrorSource, string taskName)
    {
        var tableName = GetItemErrorsTableName(taskErrorSource, taskName);

        if (tx.IsWriteTransaction && _tablesCreated.Contains(tableName) == false)
        {
            Schemas.TaskItemErrors.Current.Create(tx, tableName, TableSizeInPages);
            tx.LowLevelTransaction.OnDispose += _ =>
            {
                if (tx.LowLevelTransaction.Committed == false)
                    return;

                _tablesCreated = new HashSet<string>(_tablesCreated, StringComparer.OrdinalIgnoreCase) { tableName };
            };
        }

        return tx.OpenTable(Schemas.TaskItemErrors.Current, tableName);
    }

    internal void DeleteTaskErrorsTablesForTask(TaskErrorSource taskErrorSource, string taskName)
    {
        _txMerger.EnqueueSync(new DeleteTaskErrorsTablesForTaskCommand(taskErrorSource, taskName));
    }

    internal void DeleteTaskErrorsTablesForTask<T>(TransactionOperationContext<T> context, TaskErrorSource taskErrorSource, string taskName)
        where T : RavenTransaction
    {
        var processErrorsTableName = GetProcessErrorsTableName(taskErrorSource, taskName);
        var itemErrorsTableName = GetItemErrorsTableName(taskErrorSource, taskName);

        var tx = context.Transaction.InnerTransaction;

        tx.DeleteTable(processErrorsTableName);
        tx.DeleteTable(itemErrorsTableName);

        tx.LowLevelTransaction.OnDispose += _ =>
        {
            if (tx.LowLevelTransaction.Committed == false)
                return;

            _tablesCreated.Remove(processErrorsTableName);
            _tablesCreated.Remove(itemErrorsTableName);
        };
    }

    internal void StoreProcessError(TaskErrorSource taskErrorSource, TaskProcessError processError)
    {
        _txMerger.EnqueueSync(new StoreTaskProcessErrorCommand(taskErrorSource, processError));
    }

    internal void StoreProcessError<T>(TransactionOperationContext<T> context, TaskErrorSource taskErrorSource, TaskProcessError processError)
        where T : RavenTransaction
    {
        var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, processError.TaskName);

        var createdAtTicks = Bits.SwapBytes(processError.CreatedAt.Ticks);
        var affectedDocumentsCountSwapped = Bits.SwapBytes(processError.AffectedDocumentsCount);
        var stepSwapped = Bits.SwapBytes((long)processError.Step);

        var id = context.GetLazyString(processError.Id);
        var taskName = context.GetLazyString(processError.TaskName);
        var error = context.GetLazyString(processError.Error);

        using (Slice.From(context.Transaction.InnerTransaction.Allocator, taskName, out Slice taskNameSlice))
        {
            if (table.GetCountOfMatchesFor(Schemas.TaskProcessErrors.Current.Indexes[Schemas.TaskProcessErrors.ByTaskName], taskNameSlice) >= ErrorsLimitPerTaskErrorType)
            {
                DeleteOldestProcessErrorOfTask(table, context, processError.TaskName);
            }
        }

        using (table.Allocate(out TableValueBuilder tvb))
        {
            tvb.Add(id.Buffer, id.Size);
            tvb.Add(taskName.Buffer, taskName.Size);
            tvb.Add((byte*)&createdAtTicks, sizeof(long));
            tvb.Add((byte*)&affectedDocumentsCountSwapped, sizeof(long));
            tvb.Add((byte*)&stepSwapped, sizeof(long));
            tvb.Add(error.Buffer, error.Size);

            table.Set(tvb);
        }
    }

    internal void StoreItemErrors(TaskErrorSource taskErrorSource, string taskName, List<TaskItemError> itemErrors)
    {
        _txMerger.EnqueueSync(new StoreTaskItemErrorsCommand(taskErrorSource, taskName, itemErrors));
    }

    internal void StoreItemErrors<T>(TransactionOperationContext<T> context, TaskErrorSource taskErrorSource, string taskName, List<TaskItemError> itemErrors)
        where T : RavenTransaction
    {
        var table = EnsureItemErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, taskName);

        foreach (var itemError in itemErrors)
        {
            StoreItemError(itemError, table, context);
        }

        DeleteOldestItemErrorsOfTask(table);
    }

    private static void StoreItemError(TaskItemError itemError, Table table, JsonOperationContext context)
    {
        var createdAtTicks = Bits.SwapBytes(itemError.CreatedAt.Ticks);
        var stepSwapped = Bits.SwapBytes((long)itemError.Step);

        var id = context.GetLazyString(itemError.Id);
        var taskName = context.GetLazyString(itemError.TaskName);
        var documentId = context.GetLazyString(itemError.DocumentId);
        var error = context.GetLazyString(itemError.Error);

        using (table.Allocate(out TableValueBuilder tvb))
        {
            tvb.Add(id.Buffer, id.Size);
            tvb.Add(taskName.Buffer, taskName.Size);
            tvb.Add((byte*)&createdAtTicks, sizeof(long));
            tvb.Add(documentId.Buffer, documentId.Size);
            tvb.Add((byte*)&stepSwapped, sizeof(long));
            tvb.Add(error.Buffer, error.Size);

            table.Set(tvb);
        }
    }

    private static TaskProcessErrorTableValue ReadProcessError(ref TableValueReader reader)
    {
        var createdAt = new DateTime(Bits.SwapBytes(*(long*)reader.Read(Schemas.TaskProcessErrors.TaskProcessErrorsTable.CreatedAtIndex, out _)));
        var taskName = reader.ReadString(Schemas.TaskProcessErrors.TaskProcessErrorsTable.TaskNameIndex);
        var affectedDocumentsCount = Bits.SwapBytes(*(long*)reader.Read(Schemas.TaskProcessErrors.TaskProcessErrorsTable.AffectedDocumentsCountIndex, out _));
        var step = Bits.SwapBytes(*(long*)reader.Read(Schemas.TaskProcessErrors.TaskProcessErrorsTable.StepIndex, out _));
        var error = reader.ReadString(Schemas.TaskProcessErrors.TaskProcessErrorsTable.ErrorIndex);

        return new TaskProcessErrorTableValue
        {
            CreatedAt = createdAt,
            TaskName = taskName,
            AffectedDocumentsCount = affectedDocumentsCount,
            Step = step,
            Error = error
        };
    }

    private static TaskItemErrorTableValue ReadItemError(ref TableValueReader reader)
    {
        var createdAt = new DateTime(Bits.SwapBytes(*(long*)reader.Read(Schemas.TaskItemErrors.TaskItemErrorsTable.CreatedAtIndex, out _)));
        var taskName = reader.ReadString(Schemas.TaskItemErrors.TaskItemErrorsTable.TaskNameIndex);
        var documentId = reader.ReadString(Schemas.TaskItemErrors.TaskItemErrorsTable.DocumentIdIndex);
        var step = Bits.SwapBytes(*(long*)reader.Read(Schemas.TaskItemErrors.TaskItemErrorsTable.StepIndex, out _));
        var error = reader.ReadString(Schemas.TaskItemErrors.TaskItemErrorsTable.ErrorIndex);

        return new TaskItemErrorTableValue
        {
            CreatedAt = createdAt,
            TaskName = taskName,
            DocumentId = documentId,
            Step = step,
            Error = error
        };
    }

    public List<TaskProcessErrorTableValue> ReadAllProcessErrors(TaskErrorSource taskErrorSource)
    {
        var errors = new List<TaskProcessErrorTableValue>();

        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            foreach (var taskName in EnumerateStoredTaskNames(taskErrorSource, context.Transaction.InnerTransaction, Schemas.TaskProcessErrors.TaskProcessErrorsTree))
            {
                var processErrors = ReadProcessErrorsOfTask(context, taskName, taskErrorSource);
                errors.AddRange(processErrors);
            }
        }

        return errors;
    }

    public List<(string TaskName, List<TaskProcessErrorTableValue> ProcessErrors, List<TaskItemErrorTableValue> ItemErrors)> ReadAllErrorsGroupedByTask(TaskErrorSource taskErrorSource)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            var tx = context.Transaction.InnerTransaction;
            var taskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in EnumerateStoredTaskNames(taskErrorSource, tx, Schemas.TaskProcessErrors.TaskProcessErrorsTree))
                taskNames.Add(name);
            foreach (var name in EnumerateStoredTaskNames(taskErrorSource, tx, Schemas.TaskItemErrors.TaskItemErrorsTree))
                taskNames.Add(name);

            var result = new List<(string, List<TaskProcessErrorTableValue>, List<TaskItemErrorTableValue>)>(taskNames.Count);

            foreach (var taskName in taskNames)
            {
                var processErrors = ReadProcessErrorsOfTask(context, taskName, taskErrorSource).ToList();
                var itemErrors = ReadItemErrorsOfTask(context, taskName, taskErrorSource).ToList();

                // Table names are lowercased (see GetProcessErrorsTableName), but the TaskName field
                // stored in each row preserves the original case from when the error was written.
                // Use the row value so the Studio can do a case-sensitive match against EtlTaskStats.TaskName.
                var originalCaseName = processErrors.FirstOrDefault()?.TaskName
                    ?? itemErrors.FirstOrDefault()?.TaskName
                    ?? taskName;

                result.Add((originalCaseName, processErrors, itemErrors));
            }

            return result;
        }
    }

    public List<TaskItemErrorTableValue> ReadAllItemErrors(TaskErrorSource taskErrorSource)
    {
        var errors = new List<TaskItemErrorTableValue>();

        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            foreach (var taskName in EnumerateStoredTaskNames(taskErrorSource, context.Transaction.InnerTransaction, Schemas.TaskItemErrors.TaskItemErrorsTree))
            {
                var itemErrors = ReadItemErrorsOfTask(context, taskName, taskErrorSource);
                errors.AddRange(itemErrors);
            }
        }

        return errors;
    }

    public long ReadTotalErrorsCount(TaskErrorSource taskErrorSource)
    {
        var errorsCount = 0L;

        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            var tx = context.Transaction.InnerTransaction;
            var taskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in EnumerateStoredTaskNames(taskErrorSource, tx, Schemas.TaskProcessErrors.TaskProcessErrorsTree))
                taskNames.Add(name);
            foreach (var name in EnumerateStoredTaskNames(taskErrorSource, tx, Schemas.TaskItemErrors.TaskItemErrorsTree))
                taskNames.Add(name);

            foreach (var taskName in taskNames)
                errorsCount += ReadErrorsCountOfTask(context, taskName, taskErrorSource);
        }

        return errorsCount;
    }

    public long ReadErrorsCountOfTask(TaskErrorSource taskErrorSource, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            return ReadErrorsCountOfTask(context, taskName, taskErrorSource);
        }
    }

    private long ReadErrorsCountOfTask(DocumentsOperationContext context, string taskName, TaskErrorSource taskErrorSource)
    {
        var processErrorsCount = ReadProcessErrorsCountOfTask(context, taskName, taskErrorSource);
        var itemErrorsCount = ReadItemErrorsCountOfTask(context, taskName, taskErrorSource);

        return processErrorsCount + itemErrorsCount;
    }

    private long ReadProcessErrorsCountOfTask(DocumentsOperationContext context, string taskName, TaskErrorSource taskErrorSource)
    {
        var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, taskName);
        if (table == null)
            return 0;

        return table.NumberOfEntries;
    }

    private long ReadItemErrorsCountOfTask(DocumentsOperationContext context, string taskName, TaskErrorSource taskErrorSource)
    {
        var table = EnsureItemErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, taskName);
        if (table == null)
            return 0;

        return table.NumberOfEntries;
    }

    public List<TaskProcessErrorTableValue> ReadProcessErrorsOfTask(TaskErrorSource taskErrorSource, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            return ReadProcessErrorsOfTask(context, taskName, taskErrorSource).ToList();
        }
    }

    public List<TaskItemErrorTableValue> ReadItemErrorsOfTask(TaskErrorSource taskErrorSource, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            return ReadItemErrorsOfTask(context, taskName, taskErrorSource).ToList();
        }
    }

    private IEnumerable<TaskProcessErrorTableValue> ReadProcessErrorsOfTask(DocumentsOperationContext context, string taskName, TaskErrorSource taskErrorSource)
    {
        var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, taskName);
        if (table == null)
            yield break;

        foreach (var tvh in table.SeekByPrimaryKey(Slices.BeforeAllKeys, 0))
        {
            var error = ReadProcessError(ref tvh.Reader);

            yield return error;
        }
    }

    private IEnumerable<TaskItemErrorTableValue> ReadItemErrorsOfTask(DocumentsOperationContext context, string taskName, TaskErrorSource taskErrorSource)
    {
        var table = EnsureItemErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, taskName);
        if (table == null)
            yield break;

        foreach (var tvh in table.SeekByPrimaryKey(Slices.BeforeAllKeys, 0))
        {
            var error = ReadItemError(ref tvh.Reader);

            yield return error;
        }
    }

    internal TaskProcessErrorTableValue ReadLatestProcessErrorOfTask(TaskErrorSource taskErrorSource, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskErrorSource, taskName);

            if (table == null)
                return null;

            var tvh = table.SeekOneBackwardFrom(Schemas.TaskProcessErrors.Current.Indexes[Schemas.TaskProcessErrors.ByCreatedAt], Slices.Empty, Slices.AfterAllKeys);

            if (tvh == null)
                return null;

            return ReadProcessError(ref tvh.Reader);
        }
    }

    public void DeleteAllErrors()
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        {
            List<(TaskErrorSource TaskType, string TaskName)> toDelete;

            using (context.OpenReadTransaction())
            {
                var tx = context.Transaction.InnerTransaction;
                var seen = new HashSet<(TaskErrorSource, string)>();

                foreach (TaskErrorSource taskType in Enum.GetValues(typeof(TaskErrorSource)))
                {
                    foreach (var name in EnumerateStoredTaskNames(taskType, tx, Schemas.TaskProcessErrors.TaskProcessErrorsTree))
                        seen.Add((taskType, name));
                    foreach (var name in EnumerateStoredTaskNames(taskType, tx, Schemas.TaskItemErrors.TaskItemErrorsTree))
                        seen.Add((taskType, name));
                }

                toDelete = seen.ToList();
            }

            foreach (var (taskType, taskName) in toDelete)
                DeleteErrorsOfTask(taskType, taskName);
        }
    }

    public void DeleteErrorsOfTask(string taskName)
    {
        foreach (TaskErrorSource taskType in Enum.GetValues(typeof(TaskErrorSource)))
        {
            DeleteErrorsOfTask(taskType, taskName);
        }
    }

    public void DeleteErrorsOfTask(TaskErrorSource taskErrorSource, string taskName)
    {
        DeleteTaskErrorsTablesForTask(taskErrorSource, taskName);
    }

    private static IEnumerable<string> EnumerateStoredTaskNames(TaskErrorSource taskErrorSource, Transaction tx, string tree)
    {
        var prefix = $"{taskErrorSource}.{tree}.";

        using (Slice.From(tx.Allocator, prefix, out Slice prefixSlice))
        using (var it = tx.LowLevelTransaction.RootObjects.Iterate(prefetch: false))
        {
            it.SetRequiredPrefix(prefixSlice);

            if (it.Seek(prefixSlice) == false)
                yield break;

            do
            {
                var key = it.CurrentKey.ToString();
                yield return key.Substring(prefix.Length);
            }
            while (it.MoveNext());
        }
    }

    private static void DeleteOldestProcessErrorOfTask<T>(Table table, TransactionOperationContext<T> context, string taskName)
        where T : RavenTransaction
    {
        if (table == null)
            return;

        using (Slice.From(context.Transaction.InnerTransaction.Allocator, taskName, out Slice taskNameSlice))
        {
            foreach (var tvr in table.SeekForwardFrom(Schemas.TaskProcessErrors.Current.Indexes[Schemas.TaskProcessErrors.ByTaskName], taskNameSlice, 0))
            {
                var error = ReadProcessError(ref tvr.Result.Reader);

                if (error.TaskName != taskName)
                    break;

                using (Slice.From(context.Transaction.InnerTransaction.Allocator, error.Id, out Slice errorId))
                {
                    table.DeleteByKey(errorId);
                    return;
                }
            }
        }
    }

    private static void DeleteOldestItemErrorsOfTask(Table table)
    {
        if (table == null || table.NumberOfEntries <= ErrorsLimitPerTaskErrorType)
            return;

        var numberOfEntriesToDelete = table.NumberOfEntries - ErrorsLimitPerTaskErrorType;
        table.DeleteForwardFrom(Schemas.TaskItemErrors.Current.Indexes[Schemas.TaskItemErrors.ByCreatedAt], Slices.BeforeAllKeys, false, numberOfEntriesToDelete);
    }

    private static string GetProcessErrorsTableName(TaskErrorSource taskErrorSource, string taskName)
    {
        return $"{taskErrorSource}.{Schemas.TaskProcessErrors.TaskProcessErrorsTree}.{taskName.ToLowerInvariant()}";
    }

    private static string GetItemErrorsTableName(TaskErrorSource taskErrorSource, string taskName)
    {
        return $"{taskErrorSource}.{Schemas.TaskItemErrors.TaskItemErrorsTree}.{taskName.ToLowerInvariant()}";
    }
}
