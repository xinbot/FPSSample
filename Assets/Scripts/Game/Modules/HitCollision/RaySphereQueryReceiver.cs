using System.Collections.Generic;
using CollisionLib;
using Primitives;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

[DisableAutoCreation]
public class RaySphereQueryReceiver : BaseComponentSystem
{
    public struct Query
    {
        public float3 Origin;
        public float3 Direction;
        public float Radius;
        public float Distance;

        public int HitCollisionTestTick;

        public Entity ExcludeOwner;
        public uint Mask;
    }

    public struct QueryResult
    {
        public int Hit;
        public Entity HitCollisionOwner;
        public float3 HitPoint;
        public float3 HitNormal;
    }

    public class QueryBatch
    {
        public readonly List<int> QueryIds = new List<int>();

        public readonly List<DynamicBuffer<HitCollisionData.BoundsHistory>> BoundsHistory
            = new List<DynamicBuffer<HitCollisionData.BoundsHistory>>();

        public void Prepare(int count)
        {
            if (QueryIds.Capacity < count)
            {
                QueryIds.Capacity = count;
            }

            QueryIds.Clear();

            if (BoundsHistory.Capacity < count)
            {
                BoundsHistory.Capacity = count;
            }

            BoundsHistory.Clear();
        }
    }

    public class QueryData
    {
        public Query Query;
        public QueryResult Result;

        // Broad phase test
        public BroadPhaseSphereCastJob BroadTestJob;
        public NativeArray<sphere> BroadPhaseBounds;

        // Narrow phase test
        public readonly List<SphereCastSingleJob> NarrowTestJobs = new List<SphereCastSingleJob>(128);
    }

    [ConfigVar(Name = "collision.raysphere.debug", DefaultValue = "0", Description = "Show collision query debug",
        Flags = ConfigVar.Flags.None)]
    public static ConfigVar ShowDebug;

    [ConfigVar(Name = "collision.raysphere.debugduration", DefaultValue = "2",
        Description = "Show collision query debug", Flags = ConfigVar.Flags.None)]
    public static ConfigVar DebugDuration;

    private readonly List<QueryData> _queries = new List<QueryData>(128);
    private readonly Queue<int> _freeQueryIds = new Queue<int>(128);
    private readonly List<int> _inComingQueryIds = new List<int>(128);

    private readonly int _environmentMask;
    private readonly int _hitCollisionLayer;

    private ComponentGroup _colliderGroup;
    private readonly QueryBatch _batches = new QueryBatch();

    public RaySphereQueryReceiver(GameWorld world) : base(world)
    {
        var defaultLayer = LayerMask.NameToLayer("Default");
        var detailLayer = LayerMask.NameToLayer("collision_detail");
        var teamAreaALayer = LayerMask.NameToLayer("TeamAreaA");
        var teamAreaBLayer = LayerMask.NameToLayer("TeamAreaB");
        _hitCollisionLayer = LayerMask.NameToLayer("hitcollision_enabled");
        _environmentMask = 1 << defaultLayer | 1 << detailLayer | 1 << teamAreaALayer | 1 << teamAreaBLayer
                           | 1 << _hitCollisionLayer;
    }

    protected override void OnCreateManager()
    {
        _colliderGroup = GetComponentGroup(typeof(HitCollisionHistory), typeof(HitCollisionData));
        base.OnCreateManager();
    }

    public int RegisterQuery(Query query)
    {
        QueryData queryData;
        int queryId;
        if (_freeQueryIds.Count > 0)
        {
            queryId = _freeQueryIds.Dequeue();
            queryData = _queries[queryId];
        }
        else
        {
            queryData = new QueryData();
            queryId = _queries.Count;
            _queries.Add(queryData);
        }

        _inComingQueryIds.Add(queryId);

        queryData.Query = query;
        queryData.Result = new QueryResult();

        return queryId;
    }

    public void GetResult(int requestId, out Query query, out QueryResult result)
    {
        Profiler.BeginSample("RaySphereQueryReceiver.GetResult");

        // Update all incoming queries
        if (_inComingQueryIds.Count > 0)
        {
            _batches.Prepare(_inComingQueryIds.Count);
            _batches.QueryIds.AddRange(_inComingQueryIds);
            UpdateBatch(_batches);
            _inComingQueryIds.Clear();
        }

        var queryData = _queries[requestId];
        query = queryData.Query;
        result = queryData.Result;

        _freeQueryIds.Enqueue(requestId);
        Profiler.EndSample();
    }

    private void UpdateBatch(QueryBatch queryBatch)
    {
        Profiler.BeginSample("UpdateBatch");
        var queryCount = queryBatch.QueryIds.Count;

        Profiler.BeginSample("Get hit collision entities");

        var hitCollEntityArray = _colliderGroup.GetEntityArray();
        var hitCollDataArray = _colliderGroup.GetComponentDataArray<HitCollisionData>();
        var hitColliders = new NativeList<Entity>(hitCollEntityArray.Length, Allocator.TempJob);
        var hitColliderData = new NativeList<HitCollisionData>(hitCollEntityArray.Length, Allocator.TempJob);
        var hitColliderFlags = new NativeList<uint>(hitCollEntityArray.Length, Allocator.TempJob);
        for (int i = 0; i < hitCollEntityArray.Length; i++)
        {
            var hitCollisionOwner =
                EntityManager.GetComponentData<HitCollisionOwnerData>(hitCollDataArray[i].HitCollisionOwner);

            if (hitCollisionOwner.collisionEnabled == 0)
            {
                continue;
            }

            queryBatch.BoundsHistory.Add(
                EntityManager.GetBuffer<HitCollisionData.BoundsHistory>(hitCollEntityArray[i]));
            hitColliderData.Add(hitCollDataArray[i]);
            hitColliders.Add(hitCollEntityArray[i]);

            hitColliderFlags.Add(hitCollisionOwner.colliderFlags);
        }

        Profiler.EndSample();

        // Environment test
        Profiler.BeginSample("Environment test");

        var envTestCommands = new NativeArray<RaycastCommand>(queryCount, Allocator.TempJob);
        var envTestResults = new NativeArray<RaycastHit>(queryCount, Allocator.TempJob);
        for (int nQuery = 0; nQuery < queryCount; nQuery++)
        {
            var queryId = queryBatch.QueryIds[nQuery];
            var queryData = _queries[queryId];

            // Start environment test
            var query = queryData.Query;
            envTestCommands[nQuery] =
                new RaycastCommand(query.Origin, query.Direction, query.Distance, _environmentMask);
        }

        var envTestHandle = RaycastCommand.ScheduleBatch(envTestCommands, envTestResults, 10);
        envTestHandle.Complete();

        Profiler.EndSample();

        Profiler.BeginSample("Handle environment test");

        for (int nQuery = 0; nQuery < queryCount; nQuery++)
        {
            var queryId = queryBatch.QueryIds[nQuery];
            var queryData = _queries[queryId];

            var result = envTestResults[nQuery];
            var impact = result.collider != null;

            // query distance is adjusted so followup tests only are done before environment hit point 
            if (impact)
            {
                queryData.Query.Distance = result.distance;

                // Set environment as default hit. Will be overwritten if HitCollision is hit				
                queryData.Result.Hit = 1;
                queryData.Result.HitPoint = result.point;
                queryData.Result.HitNormal = result.normal;
                if (result.collider.gameObject.layer == _hitCollisionLayer)
                {
                    var hitCollision = result.collider.GetComponent<HitCollision>();
                    if (hitCollision != null)
                    {
                        queryData.Result.HitCollisionOwner = hitCollision.Owner;
                    }
                }
            }
        }

        Profiler.EndSample();

        // Start broadphase tests
        Profiler.BeginSample("Broadphase test");

        var broadphaseHandles = new NativeArray<JobHandle>(queryCount, Allocator.Temp);
        for (int nQuery = 0; nQuery < queryCount; nQuery++)
        {
            var queryId = queryBatch.QueryIds[nQuery];
            var queryData = _queries[queryId];
            var query = queryData.Query;

            queryData.BroadPhaseBounds = new NativeArray<sphere>(hitColliderData.Length, Allocator.TempJob);
            for (int i = 0; i < hitColliderData.Length; i++)
            {
                // Get bounds for tick
                var histIndex = hitColliderData[i].GetHistoryIndex(query.HitCollisionTestTick);
                var boundSphere = primlib.sphere(queryBatch.BoundsHistory[i][histIndex].Pos,
                    hitColliderData[i].BoundsRadius);
                queryData.BroadPhaseBounds[i] = boundSphere;
            }

            queryData.BroadTestJob = new BroadPhaseSphereCastJob(hitColliders, hitColliderData,
                hitColliderFlags, queryData.BroadPhaseBounds, query.ExcludeOwner, Entity.Null, query.Mask,
                new ray(query.Origin, query.Direction), query.Distance, query.Radius);

            broadphaseHandles[nQuery] = queryData.BroadTestJob.Schedule();
        }

        var broadphaseHandle = JobHandle.CombineDependencies(broadphaseHandles);
        broadphaseHandles.Dispose();
        broadphaseHandle.Complete();

        Profiler.EndSample();

        // Start narrow tests
        Profiler.BeginSample("Narrow test");

        // TODO (mogensh) find out how to combine jobs without "write to same native" issue
        for (int nQuery = 0; nQuery < queryCount; nQuery++)
        {
            var queryId = queryBatch.QueryIds[nQuery];
            var queryData = _queries[queryId];

            var query = queryData.Query;
            var broadPhaseResult = queryData.BroadTestJob.Result;

            // Start narrow tests
            queryData.NarrowTestJobs.Clear();

            for (var i = 0; i < broadPhaseResult.Length; i++)
            {
                var entity = broadPhaseResult[i];
                var ray = new ray(query.Origin, query.Direction);
                queryData.NarrowTestJobs.Add(new SphereCastSingleJob(EntityManager, entity, ray,
                    query.Distance, query.Radius, query.HitCollisionTestTick));

                var handle = queryData.NarrowTestJobs[i].Schedule();
                handle.Complete();
            }
        }

        Profiler.EndSample();

        // Find closest
        Profiler.BeginSample("Find closest");

        for (int nQuery = 0; nQuery < queryBatch.QueryIds.Count; nQuery++)
        {
            var queryId = queryBatch.QueryIds[nQuery];
            var queryData = _queries[queryId];
            var query = queryData.Query;

            var closestIndex = -1;
            var closestDist = float.MaxValue;

            for (int i = 0; i < queryData.NarrowTestJobs.Count; i++)
            {
                var result = queryData.NarrowTestJobs[i].Result[0];
                var hit = result.Hit == 1;
                if (hit)
                {
                    var dist = math.distance(query.Origin, result.PrimitiveCenter);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestIndex = i;
                    }
                }
            }

            if (closestIndex != -1)
            {
                var result = queryData.NarrowTestJobs[closestIndex].Result[0];
                queryData.Result.Hit = 1;
                queryData.Result.HitPoint = result.PrimitiveCenter;
                // TODO (mogensh) find correct normal
                queryData.Result.HitNormal = -queryData.Query.Direction; 

                var hitCollisionData = EntityManager.GetComponentData<HitCollisionData>(
                    queryData.NarrowTestJobs[closestIndex].HitCollObject);

                queryData.Result.HitCollisionOwner = hitCollisionData.HitCollisionOwner;
            }

            // TODO (mogensh) keep native arrays for next query
            queryData.BroadTestJob.Dispose();

            for (int i = 0; i < queryData.NarrowTestJobs.Count; i++)
            {
                queryData.NarrowTestJobs[i].Dispose();
            }
        }

        Profiler.EndSample();

        for (int nQuery = 0; nQuery < queryBatch.QueryIds.Count; nQuery++)
        {
            var queryId = queryBatch.QueryIds[nQuery];
            var queryData = _queries[queryId];
            queryData.BroadPhaseBounds.Dispose();
        }

        envTestCommands.Dispose();
        envTestResults.Dispose();

        hitColliders.Dispose();
        hitColliderData.Dispose();
        hitColliderFlags.Dispose();

        Profiler.EndSample();
    }

    protected override void OnUpdate()
    {
    }
}