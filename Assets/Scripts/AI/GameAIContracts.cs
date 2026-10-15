using System;

namespace MarblesECS.AI
{
    [Serializable]
    public sealed class GameAIConfig
    {
        public int decisionTicks = 6;
        public int gridColumns = 7, gridRows = 9;
        public int maxActions = 3000, maxTicks = 108000;
        public float rewardScale = 1000;

        public void Validate()
        {
            if (decisionTicks < 1 || decisionTicks > 120 || gridColumns < 2 || gridColumns > 41 ||
                gridRows < 2 || gridRows > 41 || maxActions < 1 || maxTicks < 1 ||
                !MarbleRules.IsFinite(rewardScale) || rewardScale <= 0)
                throw new ArgumentException("Invalid AI environment configuration.");
        }
    }

    [Serializable]
    public sealed class GameAIRequest
    {
        public int requestId;
        public string command, actionKey;
        public uint seed;
        public GameAIConfig config;
    }

    [Serializable]
    public sealed class GameAIAction
    {
        public string key, kind;
        public int id, target, index;
        public uint definitionId;
        public float x, z, value;
        public float[] features;
    }

    [Serializable]
    public sealed class GameAIEntity
    {
        public int kind, instanceId;
        public uint definitionId;
        public float[] values;
        public string status;
    }

    [Serializable]
    public sealed class GameAIObservation
    {
        public int protocol = 1;
        public string phase;
        public float[] globals;
        public long[] counters;
        public GameAIEntity[] entities;
        public GameAIAction[] actions;
        public string digest;
    }

    [Serializable]
    public sealed class GameAIResponse
    {
        public int requestId;
        public string error, reason;
        public GameAIObservation observation;
        public float reward;
        public bool terminated, truncated;
        public long finalScore;
        public int ticks, steps;
        public string unityVersion, contentHash;
        public uint[] catalogIds;
    }
}
