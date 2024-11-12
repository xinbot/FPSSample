using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

[DisableAutoCreation]
public abstract class BaseComponentSystem : ComponentSystem
{
    protected BaseComponentSystem(GameWorld world)
    {
        m_world = world;
    }

    protected readonly GameWorld m_world;
}

[DisableAutoCreation]
public abstract class BaseComponentSystem<T> : BaseComponentSystem
    where T : MonoBehaviour
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray = _group.GetComponentArray<T>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T data);
}

[DisableAutoCreation]
public abstract class BaseComponentSystem<T1, T2> : BaseComponentSystem
    where T1 : MonoBehaviour
    where T2 : MonoBehaviour
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T1), typeof(T2)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray1 = _group.GetComponentArray<T1>();
        var dataArray2 = _group.GetComponentArray<T2>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray1[i], dataArray2[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T1 data1, T2 data2);
}

[DisableAutoCreation]
public abstract class BaseComponentSystem<T1, T2, T3> : BaseComponentSystem
    where T1 : MonoBehaviour
    where T2 : MonoBehaviour
    where T3 : MonoBehaviour
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T1), typeof(T2), typeof(T3)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray1 = _group.GetComponentArray<T1>();
        var dataArray2 = _group.GetComponentArray<T2>();
        var dataArray3 = _group.GetComponentArray<T3>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray1[i], dataArray2[i], dataArray3[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T1 data1, T2 data2, T3 data3);
}

[DisableAutoCreation]
public abstract class BaseComponentDataSystem<T> : BaseComponentSystem
    where T : struct, IComponentData
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray = _group.GetComponentDataArray<T>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T data);
}

[DisableAutoCreation]
public abstract class BaseComponentDataSystem<T1, T2> : BaseComponentSystem
    where T1 : struct, IComponentData
    where T2 : struct, IComponentData
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T1), typeof(T2)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray1 = _group.GetComponentDataArray<T1>();
        var dataArray2 = _group.GetComponentDataArray<T2>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray1[i], dataArray2[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T1 data1, T2 data2);
}

[DisableAutoCreation]
public abstract class BaseComponentDataSystem<T1, T2, T3> : BaseComponentSystem
    where T1 : struct, IComponentData
    where T2 : struct, IComponentData
    where T3 : struct, IComponentData
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T1), typeof(T2), typeof(T3)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray1 = _group.GetComponentDataArray<T1>();
        var dataArray2 = _group.GetComponentDataArray<T2>();
        var dataArray3 = _group.GetComponentDataArray<T3>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray1[i], dataArray2[i], dataArray3[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T1 data1, T2 data2, T3 data3);
}

[DisableAutoCreation]
public abstract class BaseComponentDataSystem<T1, T2, T3, T4> : BaseComponentSystem
    where T1 : struct, IComponentData
    where T2 : struct, IComponentData
    where T3 : struct, IComponentData
    where T4 : struct, IComponentData
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T1), typeof(T2), typeof(T3), typeof(T4)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray1 = _group.GetComponentDataArray<T1>();
        var dataArray2 = _group.GetComponentDataArray<T2>();
        var dataArray3 = _group.GetComponentDataArray<T3>();
        var dataArray4 = _group.GetComponentDataArray<T4>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray1[i], dataArray2[i], dataArray3[i], dataArray4[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T1 data1, T2 data2, T3 data3, T4 data4);
}

[DisableAutoCreation]
public abstract class BaseComponentDataSystem<T1, T2, T3, T4, T5> : BaseComponentSystem
    where T1 : struct, IComponentData
    where T2 : struct, IComponentData
    where T3 : struct, IComponentData
    where T4 : struct, IComponentData
    where T5 : struct, IComponentData
{
    protected ComponentType[] ExtraComponentRequirements;

    private ComponentGroup _group;
    private string _name;

    public BaseComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;

        var list = new List<ComponentType>(6);
        if (ExtraComponentRequirements != null)
        {
            list.AddRange(ExtraComponentRequirements);
        }

        list.AddRange(new ComponentType[] {typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5)});
        list.Add(ComponentType.Subtractive<DespawningEntity>());
        _group = GetComponentGroup(list.ToArray());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var entityArray = _group.GetEntityArray();
        var dataArray1 = _group.GetComponentDataArray<T1>();
        var dataArray2 = _group.GetComponentDataArray<T2>();
        var dataArray3 = _group.GetComponentDataArray<T3>();
        var dataArray4 = _group.GetComponentDataArray<T4>();
        var dataArray5 = _group.GetComponentDataArray<T5>();

        for (var i = 0; i < entityArray.Length; i++)
        {
            Update(entityArray[i], dataArray1[i], dataArray2[i], dataArray3[i], dataArray4[i], dataArray5[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Update(Entity entity, T1 data1, T2 data2, T3 data3, T4 data4, T5 data5);
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public abstract class InitializeComponentSystem<T> : BaseComponentSystem
    where T : MonoBehaviour
{
    struct SystemState : IComponentData
    {
    }

    private ComponentGroup _incomingGroup;
    private string _name;

    public InitializeComponentSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;
        _incomingGroup = GetComponentGroup(typeof(T), ComponentType.Subtractive<SystemState>());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var incomingEntityArray = _incomingGroup.GetEntityArray();
        if (incomingEntityArray.Length > 0)
        {
            var incomingComponentArray = _incomingGroup.GetComponentArray<T>();
            for (var i = 0; i < incomingComponentArray.Length; i++)
            {
                var entity = incomingEntityArray[i];
                PostUpdateCommands.AddComponent(entity, new SystemState());

                Initialize(entity, incomingComponentArray[i]);
            }
        }

        Profiler.EndSample();
    }

    protected abstract void Initialize(Entity entity, T component);
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public abstract class InitializeComponentDataSystem<T, K> : BaseComponentSystem
    where T : struct, IComponentData
    where K : struct, IComponentData
{
    private ComponentGroup _incomingGroup;
    private string _name;

    public InitializeComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;
        _incomingGroup = GetComponentGroup(typeof(T), ComponentType.Subtractive<K>());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var incomingEntityArray = _incomingGroup.GetEntityArray();
        if (incomingEntityArray.Length > 0)
        {
            var incomingComponentDataArray = _incomingGroup.GetComponentDataArray<T>();
            for (var i = 0; i < incomingComponentDataArray.Length; i++)
            {
                var entity = incomingEntityArray[i];
                PostUpdateCommands.AddComponent(entity, new K());

                Initialize(entity, incomingComponentDataArray[i]);
            }
        }

        Profiler.EndSample();
    }

    protected abstract void Initialize(Entity entity, T component);
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public abstract class DeinitializeComponentSystem<T> : BaseComponentSystem
    where T : MonoBehaviour
{
    private ComponentGroup _outgoingGroup;
    private string _name;

    public DeinitializeComponentSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;
        _outgoingGroup = GetComponentGroup(typeof(T), typeof(DespawningEntity));
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var outgoingComponentArray = _outgoingGroup.GetComponentArray<T>();
        var outgoingEntityArray = _outgoingGroup.GetEntityArray();
        for (var i = 0; i < outgoingComponentArray.Length; i++)
        {
            Deinitialize(outgoingEntityArray[i], outgoingComponentArray[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Deinitialize(Entity entity, T component);
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public abstract class DeinitializeComponentDataSystem<T> : BaseComponentSystem
    where T : struct, IComponentData
{
    private ComponentGroup _outgoingGroup;
    private string _name;

    public DeinitializeComponentDataSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;
        _outgoingGroup = GetComponentGroup(typeof(T), typeof(DespawningEntity));
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var outgoingComponentArray = _outgoingGroup.GetComponentDataArray<T>();
        var outgoingEntityArray = _outgoingGroup.GetEntityArray();
        for (var i = 0; i < outgoingComponentArray.Length; i++)
        {
            Deinitialize(outgoingEntityArray[i], outgoingComponentArray[i]);
        }

        Profiler.EndSample();
    }

    protected abstract void Deinitialize(Entity entity, T component);
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public abstract class InitializeComponentGroupSystem<T, S> : BaseComponentSystem
    where T : MonoBehaviour
    where S : struct, IComponentData
{
    private ComponentGroup _incomingGroup;
    private string _name;

    public InitializeComponentGroupSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;
        _incomingGroup = GetComponentGroup(typeof(T), ComponentType.Subtractive<S>());
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        var incomingEntityArray = _incomingGroup.GetEntityArray();
        if (incomingEntityArray.Length > 0)
        {
            for (var i = 0; i < incomingEntityArray.Length; i++)
            {
                var entity = incomingEntityArray[i];
                PostUpdateCommands.AddComponent(entity, new S());
            }

            Initialize(ref _incomingGroup);
        }

        Profiler.EndSample();
    }

    protected abstract void Initialize(ref ComponentGroup group);
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public abstract class DeinitializeComponentGroupSystem<T> : BaseComponentSystem
    where T : MonoBehaviour
{
    private ComponentGroup _outgoingGroup;
    private string _name;

    public DeinitializeComponentGroupSystem(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _name = GetType().Name;
        _outgoingGroup = GetComponentGroup(typeof(T), typeof(DespawningEntity));
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample(_name);

        if (_outgoingGroup.CalculateLength() > 0)
        {
            Deinitialize(ref _outgoingGroup);
        }

        Profiler.EndSample();
    }

    protected abstract void Deinitialize(ref ComponentGroup group);
}