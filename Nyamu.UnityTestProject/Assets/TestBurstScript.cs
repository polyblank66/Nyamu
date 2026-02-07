using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;

// Burst error: try/catch is not supported
[BurstCompile]
struct TestBurstErrorJob : IJob
{
    public NativeArray<float> Result;

    public void Execute()
    {
        try
        {
            Result[0] = 1f;
        }
        catch
        {
            Result[0] = -1f;
        }
    }
}

// Burst warning BC1370: throw not guarded by ConditionalSafetyCheck
[BurstCompile]
struct TestBurstWarningJob : IJob
{
    public NativeArray<float> Result;

    public void Execute()
    {
        if (Result.Length == 0)
            throw new System.InvalidOperationException("empty");

        Result[0] = 1f;
    }
}
