using System;
using Raven.Server.Documents.ETL;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.TransactionMerger.Commands;

public sealed class StoreEtlProcessErrorCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
{
    private readonly EtlProcessError _processError;

    public StoreEtlProcessErrorCommand(EtlProcessError processError)
    {
        _processError = processError;
    }

    protected override long ExecuteCmd(DocumentsOperationContext context)
    {
        context.DocumentDatabase.EtlErrorsStorage.StoreProcessError(context, _processError);
        return 1;
    }

    public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
    {
        throw new NotImplementedException();
    }
}
