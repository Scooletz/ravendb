using System;
using System.Collections.Generic;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public class StoreEtlItemErrorsCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly string _processName;
    private readonly List<EtlItemError> _itemErrors;

    public StoreEtlItemErrorsCommand(string processName, List<EtlItemError> itemErrors)
    {
        _processName = processName;
        _itemErrors = itemErrors;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        context.DocumentDatabase.EtlErrorsStorage.StoreItemErrors(context, _processName, _itemErrors);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
