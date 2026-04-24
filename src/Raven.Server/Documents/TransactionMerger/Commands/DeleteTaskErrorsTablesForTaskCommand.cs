using System;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class DeleteTaskErrorsTablesForTaskCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly TaskErrorSource _taskErrorSource;
    private readonly string _taskName;

    public DeleteTaskErrorsTablesForTaskCommand(TaskErrorSource taskErrorSource, string taskName)
    {
        _taskErrorSource = taskErrorSource;
        _taskName = taskName;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        context.DocumentDatabase.TaskErrorsStorage.DeleteTaskErrorsTablesForTask(context, _taskErrorSource, _taskName);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
