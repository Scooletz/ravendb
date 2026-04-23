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

    private Table EnsureProcessErrorsTableCreated(Transaction tx, TaskType taskType, string taskName)
    {
        var tableName = GetProcessErrorsTableName(taskType, taskName);

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

    private Table EnsureItemErrorsTableCreated(Transaction tx, TaskType taskType, string taskName)
    {
        var tableName = GetItemErrorsTableName(taskType, taskName);

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

    internal void DeleteTaskErrorsTablesForTask(TaskType taskType, string taskName)
    {
        _txMerger.EnqueueSync(new DeleteTaskErrorsTablesForTaskCommand(taskType, taskName));
    }

    internal void DeleteTaskErrorsTablesForTask<T>(TransactionOperationContext<T> context, TaskType taskType, string taskName)
        where T : RavenTransaction
    {
        var processErrorsTableName = GetProcessErrorsTableName(taskType, taskName);
        var itemErrorsTableName = GetItemErrorsTableName(taskType, taskName);

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

    internal void StoreProcessError(TaskType taskType, TaskProcessError processError)
    {
        _txMerger.EnqueueSync(new StoreTaskProcessErrorCommand(taskType, processError));
    }

    internal void StoreProcessError<T>(TransactionOperationContext<T> context, TaskType taskType, TaskProcessError processError)
        where T : RavenTransaction
    {
        var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskType, processError.TaskName);

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

    internal void StoreItemErrors(TaskType taskType, string taskName, List<TaskItemError> itemErrors)
    {
        _txMerger.EnqueueSync(new StoreTaskItemErrorsCommand(taskType, taskName, itemErrors));
    }

    internal void StoreItemErrors<T>(TransactionOperationContext<T> context, TaskType taskType, string taskName, List<TaskItemError> itemErrors)
        where T : RavenTransaction
    {
        var table = EnsureItemErrorsTableCreated(context.Transaction.InnerTransaction, taskType, taskName);

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

    public List<TaskProcessErrorTableValue> ReadAllProcessErrors(TaskType taskType)
    {
        var errors = new List<TaskProcessErrorTableValue>();

        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            foreach (var taskName in EnumerateStoredTaskNames(taskType, context.Transaction.InnerTransaction, Schemas.TaskProcessErrors.TaskProcessErrorsTree))
            {
                var processErrors = ReadProcessErrorsOfTask(taskType, taskName, context);
                errors.AddRange(processErrors);
            }
        }

        return errors;
    }

    public List<TaskItemErrorTableValue> ReadAllItemErrors(TaskType taskType)
    {
        var errors = new List<TaskItemErrorTableValue>();

        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            foreach (var taskName in EnumerateStoredTaskNames(taskType, context.Transaction.InnerTransaction, Schemas.TaskItemErrors.TaskItemErrorsTree))
            {
                var itemErrors = ReadItemErrorsOfTask(taskType, taskName, context);
                errors.AddRange(itemErrors);
            }
        }

        return errors;
    }

    public long ReadTotalErrorsCount(TaskType taskType)
    {
        var errorsCount = 0L;

        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            var tx = context.Transaction.InnerTransaction;
            var taskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in EnumerateStoredTaskNames(taskType, tx, Schemas.TaskProcessErrors.TaskProcessErrorsTree))
                taskNames.Add(name);
            foreach (var name in EnumerateStoredTaskNames(taskType, tx, Schemas.TaskItemErrors.TaskItemErrorsTree))
                taskNames.Add(name);

            foreach (var taskName in taskNames)
                errorsCount += ReadErrorsCountOfTask(taskType, taskName, context);
        }

        return errorsCount;
    }

    public long ReadErrorsCountOfTask(TaskType taskType, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            return ReadErrorsCountOfTask(taskType, taskName, context);
        }
    }

    private long ReadErrorsCountOfTask(TaskType taskType, string taskName, DocumentsOperationContext context)
    {
        var processErrorsCount = ReadProcessErrorsCountOfTask(taskType, taskName, context);
        var itemErrorsCount = ReadItemErrorsCountOfTask(taskType, taskName, context);

        return processErrorsCount + itemErrorsCount;
    }

    private long ReadProcessErrorsCountOfTask(TaskType taskType, string taskName, DocumentsOperationContext context)
    {
        var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskType, taskName);
        if (table == null)
            return 0;

        return table.NumberOfEntries;
    }

    private long ReadItemErrorsCountOfTask(TaskType taskType, string taskName, DocumentsOperationContext context)
    {
        var table = EnsureItemErrorsTableCreated(context.Transaction.InnerTransaction, taskType, taskName);
        if (table == null)
            return 0;

        return table.NumberOfEntries;
    }

    public List<TaskProcessErrorTableValue> ReadProcessErrorsOfTask(TaskType taskType, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            return ReadProcessErrorsOfTask(taskType, taskName, context).ToList();
        }
    }

    public List<TaskItemErrorTableValue> ReadItemErrorsOfTask(TaskType taskType, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            return ReadItemErrorsOfTask(taskType, taskName, context).ToList();
        }
    }

    private IEnumerable<TaskProcessErrorTableValue> ReadProcessErrorsOfTask(TaskType taskType, string taskName, DocumentsOperationContext context)
    {
        var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskType, taskName);
        if (table == null)
            yield break;

        foreach (var tvh in table.SeekByPrimaryKey(Slices.BeforeAllKeys, 0))
        {
            var error = ReadProcessError(ref tvh.Reader);

            yield return error;
        }
    }

    private IEnumerable<TaskItemErrorTableValue> ReadItemErrorsOfTask(TaskType taskType, string taskName, DocumentsOperationContext context)
    {
        var table = EnsureItemErrorsTableCreated(context.Transaction.InnerTransaction, taskType, taskName);
        if (table == null)
            yield break;

        foreach (var tvh in table.SeekByPrimaryKey(Slices.BeforeAllKeys, 0))
        {
            var error = ReadItemError(ref tvh.Reader);

            yield return error;
        }
    }

    internal TaskProcessErrorTableValue ReadLatestProcessErrorOfTask(TaskType taskType, string taskName)
    {
        using (_contextPool.AllocateOperationContext(out DocumentsOperationContext context))
        using (context.OpenReadTransaction())
        {
            var table = EnsureProcessErrorsTableCreated(context.Transaction.InnerTransaction, taskType, taskName);

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
            List<(TaskType TaskType, string TaskName)> toDelete;

            using (context.OpenReadTransaction())
            {
                var tx = context.Transaction.InnerTransaction;
                var seen = new HashSet<(TaskType, string)>();

                foreach (TaskType taskType in Enum.GetValues(typeof(TaskType)))
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
        foreach (TaskType taskType in Enum.GetValues(typeof(TaskType)))
        {
            DeleteErrorsOfTask(taskType, taskName);
        }
    }

    public void DeleteErrorsOfTask(TaskType taskType, string taskName)
    {
        DeleteTaskErrorsTablesForTask(taskType, taskName);
    }

    private static IEnumerable<string> EnumerateStoredTaskNames(TaskType taskType, Transaction tx, string tree)
    {
        var prefix = $"{taskType}.{tree}.";

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

    private static string GetProcessErrorsTableName(TaskType taskType, string taskName)
    {
        return $"{taskType}.{Schemas.TaskProcessErrors.TaskProcessErrorsTree}.{taskName.ToLowerInvariant()}";
    }

    private static string GetItemErrorsTableName(TaskType taskType, string taskName)
    {
        return $"{taskType}.{Schemas.TaskItemErrors.TaskItemErrorsTree}.{taskName.ToLowerInvariant()}";
    }
}
