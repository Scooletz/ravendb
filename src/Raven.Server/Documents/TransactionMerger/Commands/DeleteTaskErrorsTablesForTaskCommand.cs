using System;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class DeleteTaskErrorsTablesForTaskCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly string _taskName;
    private readonly TaskErrorSource _taskErrorSource;

    public DeleteTaskErrorsTablesForTaskCommand(string taskName, TaskErrorSource taskErrorSource)
    {
        _taskName = taskName;
        _taskErrorSource = taskErrorSource;
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
