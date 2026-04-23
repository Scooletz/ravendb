using System;

namespace Raven.Server.Documents.ETL;

public abstract class TaskErrorTableValueBase
{
    public string Id => GetId();
    public DateTime CreatedAt;
    public string TaskName;
    public long Step;
    public string Error;

    protected virtual string GetId() => throw new NotSupportedException();
}
