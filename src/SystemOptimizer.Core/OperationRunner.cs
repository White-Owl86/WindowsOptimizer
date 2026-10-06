namespace SystemOptimizer.Core;

public sealed class NotApplicableException(string message) : Exception(message);
public enum OperationStatus { Success, Skipped, Failed }
public record PlannedOperation(string Name, Func<Task<string>> Execute);
public record OperationResult(string Name, OperationStatus Status, string Detail);
public record OperationProgress(int Completed, int Total, OperationResult Result);

public static class OperationRunner
{
    public static async Task<IReadOnlyList<OperationResult>> RunAsync(
        IReadOnlyList<PlannedOperation> operations, IProgress<OperationProgress>? progress = null)
    {
        var results = new List<OperationResult>();
        foreach (var operation in operations)
        {
            OperationResult result;
            try
            {
                result = new(operation.Name, OperationStatus.Success, await operation.Execute().ConfigureAwait(false));
            }
            catch (NotApplicableException ex)
            {
                result = new(operation.Name, OperationStatus.Skipped, ex.Message);
            }
            catch (Exception ex)
            {
                result = new(operation.Name, OperationStatus.Failed, ex.Message);
            }
            results.Add(result);
            progress?.Report(new(results.Count, operations.Count, result));
        }
        return results;
    }
}
