using System;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class StoreTaskProcessErrorCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly TaskType _taskType;
    private readonly TaskProcessError _processError;

    public StoreTaskProcessErrorCommand(TaskType taskType, TaskProcessError processError)
    {
        _taskType = taskType;
        _processError = processError;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        context.DocumentDatabase.TaskErrorsStorage.StoreProcessError(context, _taskType, _processError);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
