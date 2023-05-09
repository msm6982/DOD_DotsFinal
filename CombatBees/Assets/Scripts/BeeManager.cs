using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System;

public class BeeManager : MonoBehaviour
{
    public Mesh beeMesh;
    public Material beeMaterial;
    public Color[] teamColors;
    public float minBeeSize;
    public float maxBeeSize;
    public float speedStretch;
    public float rotationStiffness;
    [Space(10)]
    [Range(0f, 1f)]
    public float aggression;
    public float flightJitter;
    public float teamAttraction;
    public float teamRepulsion;
    [Range(0f, 1f)]
    public float damping;
    public float chaseForce;
    public float carryForce;
    public float grabDistance;
    public float attackDistance;
    public float attackForce;
    public float hitDistance;
    public float maxSpawnSpeed;
    [Space(10)]
    public int startBeeCount;

    List<Bee> bees;
    NativeList<int>[] teamsOfBees;
    NativeList<float3> velocities;
    //List<int> blueBees;
    //List<int> yellowBees;
    List<Bee> pooledBees;

    int activeBatch = 0;
    List<List<Matrix4x4>> beeMatrices;
    List<List<Vector4>> beeColors;

    static BeeManager instance;

    const int beesPerBatch = 1023;
    MaterialPropertyBlock matProps;

    public static void SpawnBee(bool teamBlue)
    {

        int team = (teamBlue) ? 1 : 0;
        Vector3 pos = Vector3.right * (-Field.size.x * .4f + Field.size.x * .8f * team);
        instance._SpawnBee(pos, teamBlue, team);
    }

    public static void SpawnBee(Vector3 pos, bool teamBlue)
    {
        int team = (teamBlue) ? 1 : 0;
        instance._SpawnBee(pos, teamBlue, team);
    }
    void _SpawnBee(Vector3 pos, bool teamBlue, int teamInt)
    {
        Bee bee;
        if (pooledBees.Count == 0)
        {
            bee = new Bee();
        }
        else
        {
            bee = pooledBees[pooledBees.Count - 1];
            pooledBees.RemoveAt(pooledBees.Count - 1);
        }

        bee.Init(pos, teamBlue, UnityEngine.Random.Range(minBeeSize, maxBeeSize));

        //bee.velocity = Random.insideUnitSphere * maxSpawnSpeed;
        bees.Add(bee);
        velocities.Add(UnityEngine.Random.insideUnitSphere * maxSpawnSpeed);
        // Track Index
        int index = bees.Count - 1;
        bee.index = index;

        teamsOfBees[teamInt].Add(index);
        //teamsOfBees[team].Add(bee)
        if (beeMatrices[activeBatch].Count == beesPerBatch)
        {
            activeBatch++;
            if (beeMatrices.Count == activeBatch)
            {
                beeMatrices.Add(new List<Matrix4x4>());
                beeColors.Add(new List<Vector4>());
            }
        }
        beeMatrices[activeBatch].Add(Matrix4x4.identity);

        beeColors[activeBatch].Add(teamColors[teamInt]);
    }
    List<JobHandle> DeleteBee(Bee bee)
    {

        int removedIndex = bee.index;



        pooledBees.Add(bee);
        velocities.RemoveAt(removedIndex);
        bees.RemoveAt(removedIndex);

        int teamInt = (bee.isBlue) ? 1 : 0;
        int teamIndex = teamsOfBees[teamInt].IndexOf(removedIndex);
        teamsOfBees[teamInt].RemoveAt(teamIndex);

        for (int i = 0; i < bees.Count; i++)
        {

            if (bees[i].index >= removedIndex)
            {
                bees[i].index--;
            }
            if (bees[i].enemyTargetIndex >= removedIndex)
            {
                bees[i].enemyTargetIndex--;
            }
        }

        var group1Job = new BeeDeathJob { beeList = teamsOfBees[0], threashHold = removedIndex };
        var group2Job = new BeeDeathJob { beeList = teamsOfBees[1], threashHold = removedIndex };

        JobHandle handleJob1 = group1Job.Schedule(teamsOfBees[0].Length, 500);
        JobHandle handleJob2 = group2Job.Schedule(teamsOfBees[1].Length, 500);
        var Joblist = new List<JobHandle>
        {
            handleJob1,
            handleJob2
        };


        //teamsOfBees[bee.isBlue].Remove(bee);
        if (beeMatrices[activeBatch].Count == 0 && activeBatch > 0)
        {
            activeBatch--;
        }
        beeMatrices[activeBatch].RemoveAt(beeMatrices[activeBatch].Count - 1);
        beeColors[activeBatch].RemoveAt(beeColors[activeBatch].Count - 1);
        return Joblist;

    }

    void Awake()
    {
        instance = this;
    }
    void Start()
    {
        bees = new List<Bee>(50000);
        teamsOfBees = new NativeList<int>[2];

        pooledBees = new List<Bee>(50000);
        velocities = new NativeList<float3>(500000, Allocator.Persistent);
        beeMatrices = new List<List<Matrix4x4>>();
        beeMatrices.Add(new List<Matrix4x4>());
        beeColors = new List<List<Vector4>>();
        beeColors.Add(new List<Vector4>());

        matProps = new MaterialPropertyBlock();

        for (int i = 0; i < 2; i++)
        {

            teamsOfBees[i] = new NativeList<int>(25000, Allocator.Persistent);
        }
        for (int i = 0; i < startBeeCount; i++)
        {
            // Previously 0-5
            bool team = (0 == i % 2);

            SpawnBee(team);
        }
        //Debug.Log(yellowBees[53]);

        matProps = new MaterialPropertyBlock();
        matProps.SetVectorArray("_Color", new Vector4[beesPerBatch]);
    }

    void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        for (int i = 0; i < bees.Count; i++)
        {
            List<JobHandle> deathJobs = new List<JobHandle>();
            JobHandle friendVelJob;
            Bee bee = bees[i];
            bee.isAttacking = false;
            bee.isHoldingResource = false;
            float3 delta;
            float dist;

            if (bee.dead == false)
            {
                int teamInt = (bee.isBlue) ? 1 : 0;

                velocities[bee.index] += (float3)UnityEngine.Random.insideUnitSphere * (flightJitter * deltaTime);
                velocities[bee.index] *= (1f - damping);
                if(bees[teamsOfBees[teamInt][UnityEngine.Random.Range(0, teamsOfBees[teamInt].Length)]] == null)
                {
                    Debug.Log(teamsOfBees[teamInt].Length);
                }
                
                float3 AttractiveFriend = bees[teamsOfBees[teamInt][UnityEngine.Random.Range(0, teamsOfBees[teamInt].Length)]].position;
                float3 RepellFriend = bees[teamsOfBees[teamInt][UnityEngine.Random.Range(0, teamsOfBees[teamInt].Length)]].position;
                var friendVel = new AllySettingJob
                {
                    beePos = bee.position,
                    friendPos = AttractiveFriend,
                    repelPos = RepellFriend,
                    teamAttraction = teamAttraction,
                    velocity = velocities[bee.index],
                    deltaTime = deltaTime,
                    teamRepulse = teamRepulsion
                };
                // Random Attactive Friend
                friendVelJob = friendVel.Schedule();



                if (bee.enemyTargetIndex == -1 && bee.resourceTarget == null)
                {
                    if (UnityEngine.Random.value < aggression)
                    {
                        int enemyInt = (bee.isBlue) ? 0 : 1;
                        if (teamsOfBees[enemyInt].Length > 0)
                        {
                            // Maybe fix this 
                            bee.enemyTargetIndex = teamsOfBees[enemyInt][UnityEngine.Random.Range(0, teamsOfBees[enemyInt].Length)];
                        }
                    }
                    else
                    {
                        bee.resourceTarget = ResourceManager.TryGetRandomResource();
                    }
                }
                else if (bee.enemyTargetIndex != -1)
                {
                    delta = bees[bee.enemyTargetIndex].position - bee.position;
                    float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                    friendVelJob.Complete();
                    if (sqrDist > attackDistance * attackDistance)
                    {
                        velocities[bee.index] += delta * (chaseForce * deltaTime / Mathf.Sqrt(sqrDist));
                    }
                    else
                    {
                        bee.isAttacking = true;
                        velocities[bee.index] += delta * (attackForce * deltaTime / Mathf.Sqrt(sqrDist));
                        if (sqrDist < hitDistance * hitDistance)
                        {

                            ParticleManager.SpawnParticle(bees[bee.enemyTargetIndex].position, ParticleType.Blood, velocities[bee.index] * .35f, 2f, 6);
                            bees[bee.enemyTargetIndex].dead = true;
                            velocities[bee.enemyTargetIndex] *= .5f;
                            bees[bee.enemyTargetIndex].enemyTargetIndex = -1;
                        }
                    }
                }
                else if (bee.resourceTarget != null)
                {
                    Resource resource = bee.resourceTarget;
                    if (resource.holder == null)
                    {
                        if (resource.dead)
                        {
                            bee.resourceTarget = null;
                        }
                        else if (resource.stacked && ResourceManager.IsTopOfStack(resource) == false)
                        {
                            bee.resourceTarget = null;
                        }
                        else
                        {
                            delta = resource.position - bee.position;
                            float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                            if (sqrDist > grabDistance * grabDistance)
                            {
                                friendVelJob.Complete();
                                velocities[bee.index] += delta * (chaseForce * deltaTime / Mathf.Sqrt(sqrDist));
                            }
                            else if (resource.stacked)
                            {
                                ResourceManager.GrabResource(bee, resource);
                            }
                        }
                    }
                    else if (resource.holder == bee)
                    {
                        int basePos = (bee.isBlue) ? 1 : 0;

                        float3 targetPos = new float3(-Field.size.x * .45f + Field.size.x * .9f * basePos, 0f, bee.position.z);
                        delta = targetPos - (float3)bee.position;
                        dist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
                        friendVelJob.Complete();
                        velocities[bee.index] += (targetPos - (float3)bee.position) * (carryForce * deltaTime / dist);
                        if (dist < 1f)
                        {
                            resource.holder = null;
                            bee.resourceTarget = null;
                        }
                        else
                        {
                            bee.isHoldingResource = true;
                        }
                    }
                    else if (resource.holder.isBlue != bee.isBlue)
                    {
                        if (bee.enemyTargetIndex != -1)
                        {
                            bees[bee.enemyTargetIndex] = resource.holder;
                        }
                        else { resource.holder = null; }

                    }
                    else if (resource.holder.isBlue == bee.isBlue)
                    {
                        bee.resourceTarget = null;
                    }
                }
                friendVelJob.Complete();
                BoundsCheck(bee, i);
                bee.velocity = velocities[bee.index];
                bee.position += deltaTime * (Vector3)velocities[bee.index];
            }
            else
            {
                if (UnityEngine.Random.value < (bee.deathTimer - .5f) * .5f)
                {
                    ParticleManager.SpawnParticle(bee.position, ParticleType.Blood, Vector3.zero);
                }
                velocities[bee.index] = new float3(velocities[bee.index].x, velocities[bee.index].y + (Field.gravity * deltaTime), velocities[bee.index].z);
                //velocities[i].y += Field.gravity * deltaTime;
                bee.deathTimer -= deltaTime / 10f;
                if (bee.deathTimer < 0f)
                {
                    deathJobs.AddRange(DeleteBee(bee));
                    Debug.Log("Delete");
                    //i--;
                }
                else { BoundsCheck(bee, i); bee.velocity = velocities[bee.index]; bee.position += deltaTime * (Vector3)velocities[bee.index]; }
            }





            // only used for smooth rotation:
            Vector3 oldSmoothPos = bee.smoothPosition;
            if (bee.isAttacking == false)
            {
                bee.smoothPosition = Vector3.Lerp(bee.smoothPosition, bee.position, deltaTime * rotationStiffness);
            }
            else
            {
                bee.smoothPosition = bee.position;
            }
            bee.smoothDirection = bee.smoothPosition - oldSmoothPos;

            foreach (JobHandle d in deathJobs)
            {
                d.Complete();
            }

        }
    }
    private void Update()
    {
        for (int i = 0; i < bees.Count; i++)
        {
            float size = bees[i].size;
            Vector3 scale = new Vector3(size, size, size);
            if (bees[i].dead == false)
            {
                float stretch = Mathf.Max(1f, bees[i].velocity.magnitude * speedStretch);
                scale.z *= stretch;
                scale.x /= (stretch - 1f) / 5f + 1f;
                scale.y /= (stretch - 1f) / 5f + 1f;
            }
            else
            {
                Color color = (bees[i].isBlue) ? teamColors[1] : teamColors[0];
                color *= .75f;
                scale *= Mathf.Sqrt(bees[i].deathTimer);
                beeColors[i / beesPerBatch][i % beesPerBatch] = color;
            }
            Quaternion rotation = Quaternion.identity;

            if (bees[i].smoothDirection != Vector3.zero)
            {
                rotation = Quaternion.LookRotation(bees[i].smoothDirection);
            }

            beeMatrices[i / beesPerBatch][i % beesPerBatch] = Matrix4x4.TRS(bees[i].position, rotation, scale);

        }
        for (int i = 0; i <= activeBatch; i++)
        {
            if (beeMatrices[i].Count > 0)
            {
                matProps.SetVectorArray("_Color", beeColors[i]);
                Graphics.DrawMeshInstanced(beeMesh, 0, beeMaterial, beeMatrices[i], matProps);
            }
        }
    }

    private void BoundsCheck(Bee bee, int index)
    {
        if (System.Math.Abs(bee.position.x) > Field.size.x * .5f)
        {
            bee.position.x = (Field.size.x * .5f) * Mathf.Sign(bee.position.x);
            //velocities[i].x *= -.5f;
            //bee.velocity.y *= .8f;
            //bee.velocity.z *= .8f;
            velocities[bee.index] = new float3(velocities[bee.index].x * -.5f, velocities[index].y * .8f, velocities[bee.index].z * .8f);
        }
        if (System.Math.Abs(bee.position.z) > Field.size.z * .5f)
        {
            bee.position.z = (Field.size.z * .5f) * Mathf.Sign(bee.position.z);
            //bee.velocity.z *= -.5f;
            //bee.velocity.x *= .8f;
            //bee.velocity.y *= .8f;
            velocities[bee.index] = new float3(velocities[bee.index].x * .8f, velocities[bee.index].y * -.5f, velocities[bee.index].z * -.5f);
        }
        float resourceModifier = 0f;
        if (bee.isHoldingResource)
        {
            resourceModifier = ResourceManager.instance.resourceSize;
        }
        if (System.Math.Abs(bee.position.y) > Field.size.y * .5f - resourceModifier)
        {
            bee.position.y = (Field.size.y * .5f - resourceModifier) * Mathf.Sign(bee.position.y);
            //bee.velocity.y *= -.5f;
            //bee.velocity.z *= .8f;
            //bee.velocity.x *= .8f;
            velocities[bee.index] = new float3(velocities[bee.index].x * .8f, velocities[index].y * -.5f, velocities[index].z * -.5f);
        }
    }

    public void OnDestroy()
    {
        velocities.Dispose();
        teamsOfBees[0].Dispose();
        teamsOfBees[1].Dispose();
    }
}

[BurstCompile]
public struct BeeDeathJob : IJobParallelFor
{
    public NativeArray<int> beeList;
    public int threashHold;
    //public bool removeTeam;

    public void Execute(int index)
    {
        for (int i = 0; i < beeList.Length; i++)
        {
            if (beeList[i] >= threashHold)
            {
                beeList[i]--;
            }

        }
    }
}


public struct AllySettingJob : IJob
{
    public float3 velocity;
    public Vector3 friendPos;
    public Vector3 repelPos;
    public Vector3 beePos;
    public float teamAttraction;
    public float teamRepulse;
    public float deltaTime;

    public void Execute()
    {
        float3 delta = friendPos - beePos;
        float dist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
        if (dist > 0f)
        {
            velocity += delta * (teamAttraction * deltaTime / dist);
        }

        // RepelantFriend
        //Vector3 repellentFriend = allies[Random.Range(0,allies.Count)];
        delta = repelPos - repelPos;
        dist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
        if (dist > 0f)
        {
            velocity -= delta * (teamRepulse * deltaTime / dist);
        }


    }
}