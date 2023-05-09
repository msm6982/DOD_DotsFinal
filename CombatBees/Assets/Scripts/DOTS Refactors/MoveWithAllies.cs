/*
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MoveWithAllies : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> AllyPosition;
    [ReadOnly] public NativeArray<float3> RepelentPosition;

    void GetAllies(float3 seekerPos, int startIdx, int endIdx, int step,
        ref float3 nearestTargetPos, ref float nearestDistSq)
    {
        for (int i = startIdx; i != endIdx; i += step)
        {
            float3 targetPos = TargetPositions[i];
            float xdiff = seekerPos.x - targetPos.x;

            // If the square of the x distance is greater than the current nearest, we can stop searching. 
            if ((xdiff * xdiff) > nearestDistSq) break;

            float distSq = math.distancesq(targetPos, seekerPos);

            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestTargetPos = targetPos;
            }
        }
    }

    public void Execute(int index)
    {

    }
}
*/