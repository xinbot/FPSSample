using System;
using Networking;
using Unity.Entities;
using Unity.Mathematics;

public class Grenade
{
    [Serializable]
    public struct Settings : IComponentData
    {
        public float maxLifetime;
        public SplashDamageSettings splashDamage;
        public float proximityTriggerDist;
        public float gravity;
        public float bounciness;
        public float collisionRadius;
    }

    public struct InternalState : IComponentData
    {
        public int Active;
        public int RayQueryId;
        public float3 Position;
        public float3 Velocity;
        public Entity Owner;
        public int TeamId;
        public int StartTick;
        public int ExplodeTick;
    }

    public struct InterpolatedState : IInterpolatedComponent<InterpolatedState>, IComponentData
    {
        public float3 position;
        public int exploded;
        public int bouncetick;

        public static IInterpolatedComponentSerializerFactory CreateSerializerFactory()
        {
            return new InterpolatedComponentSerializerFactory<InterpolatedState>();
        }

        public void Serialize(ref SerializeContext context, ref NetworkWriter writer)
        {
            writer.WriteVector3("position", position);
            writer.WriteBoolean("exploded", exploded == 1);
            writer.WriteInt32("bouncetick", bouncetick);
        }

        public void Deserialize(ref SerializeContext context, ref NetworkReader reader)
        {
            position = reader.ReadVector3();
            exploded = reader.ReadBoolean() ? 1 : 0;
            bouncetick = reader.ReadInt32();
        }

        public void Interpolate(ref SerializeContext context, ref InterpolatedState first, ref InterpolatedState last,
            float t)
        {
            position = math.lerp(first.position, last.position, t);
            exploded = first.exploded;
            bouncetick = first.bouncetick;
        }
    }
}