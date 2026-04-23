using System;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class DeleteTaskErrorsTablesForTaskCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly TaskType _taskType;
    private readonly string _taskName;

    public DeleteTaskErrorsTablesForTaskCommand(TaskType taskType, string taskName)
    {
        _taskType = taskType;
        _taskName = taskName;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        context.DocumentDatabase.TaskErrorsStorage.DeleteTaskErrorsTablesForTask(context, _taskType, _taskName);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
