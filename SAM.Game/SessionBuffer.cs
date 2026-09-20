using System;
using System.Collections.Generic;

namespace SAM.Game
{
    internal sealed class SessionBuffer
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, int> _intStats = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _floatStats = new(StringComparer.Ordinal);
        private readonly HashSet<string> _achievements = new(StringComparer.Ordinal);

        public event Action Changed;

        public int AchievementCount
        {
            get
            {
                lock (this._lock)
                {
                    return this._achievements.Count;
                }
            }
        }

        public int StatCount
        {
            get
            {
                lock (this._lock)
                {
                    return this._intStats.Count + this._floatStats.Count;
                }
            }
        }

        public bool HasWork
        {
            get { return this.AchievementCount > 0 || this.StatCount > 0; }
        }

        public void Clear()
        {
            lock (this._lock)
            {
                this._intStats.Clear();
                this._floatStats.Clear();
                this._achievements.Clear();
            }
        }

        public void UnlockAchievement(string id)
        {
            if (string.IsNullOrWhiteSpace(id) == true)
            {
                return;
            }

            bool added;
            lock (this._lock)
            {
                added = this._achievements.Add(id);
            }

            if (added == true)
            {
                this.Changed?.Invoke();
            }
        }

        public void SetIntStat(string id, int value)
        {
            if (string.IsNullOrWhiteSpace(id) == true)
            {
                return;
            }

            lock (this._lock)
            {
                if (this._intStats.TryGetValue(id, out var current) == true)
                {
                    if (value <= current)
                    {
                        return;
                    }
                }

                this._intStats[id] = value;
            }

            this.Changed?.Invoke();
        }

        public void SetFloatStat(string id, float value)
        {
            if (string.IsNullOrWhiteSpace(id) == true)
            {
                return;
            }

            lock (this._lock)
            {
                if (this._floatStats.TryGetValue(id, out var current) == true)
                {
                    if (value <= current)
                    {
                        return;
                    }
                }

                this._floatStats[id] = value;
            }

            this.Changed?.Invoke();
        }

        public void Snapshot(
            out Dictionary<string, int> intStats,
            out Dictionary<string, float> floatStats,
            out List<string> achievements)
        {
            lock (this._lock)
            {
                intStats = new Dictionary<string, int>(this._intStats, StringComparer.Ordinal);
                floatStats = new Dictionary<string, float>(this._floatStats, StringComparer.Ordinal);
                achievements = new List<string>(this._achievements);
            }
        }

        public void DropUnchanged(
            Dictionary<string, int> intStats,
            Dictionary<string, float> floatStats,
            List<string> achievements)
        {
            bool changed = false;
            lock (this._lock)
            {
                if (achievements != null)
                {
                    foreach (var id in achievements)
                    {
                        if (this._achievements.Remove(id) == true)
                        {
                            changed = true;
                        }
                    }
                }

                if (intStats != null)
                {
                    foreach (var pair in intStats)
                    {
                        if (this._intStats.TryGetValue(pair.Key, out var current) == true &&
                            current <= pair.Value)
                        {
                            this._intStats.Remove(pair.Key);
                            changed = true;
                        }
                    }
                }

                if (floatStats != null)
                {
                    foreach (var pair in floatStats)
                    {
                        if (this._floatStats.TryGetValue(pair.Key, out var current) == true &&
                            current <= pair.Value)
                        {
                            this._floatStats.Remove(pair.Key);
                            changed = true;
                        }
                    }
                }
            }

            if (changed == true)
            {
                this.Changed?.Invoke();
            }
        }
    }
}
