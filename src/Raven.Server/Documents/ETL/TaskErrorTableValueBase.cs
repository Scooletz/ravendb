using System;

namespace Raven.Server.Documents.ETL;

public abstract class TaskErrorTableValueBase
{
    public string Id
    {
        get => field ??= GetId();
        set;
    }

    public DateTime CreatedAt;
    public string TaskName;
    public long Step;
    public string Error;

    protected virtual string GetId() => throw new NotSupportedException();
}
