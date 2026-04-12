namespace DotNetQuery.Core.Internals;

internal interface IQueryDataAccess
{
    void SetData(object? data);

    object? GetCurrentData();
}
