using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace SlopeBooster
{
    [BepInPlugin("slopebooster", "Slope Booster", "1.0.0")]
    public class SlopeBoosterPlugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> CfgSlopeMult;
        private static ConfigEntry<float> CfgJumpMult;
        private static ConfigEntry<float> CfgInertiaDuration;
        private static ConfigEntry<float> CfgFadeStart;

        // MonoMod hooks (static field = GC root)
        private static Hook _hookSlope;

        // Shared state for inertia component
        internal static Vector3 SlopeDir;
        internal static float SlopeSpeed;
        internal static float SlopeTime;

        private void Awake()
        {
            CfgSlopeMult = Config.Bind("Boost", "SlopeMultiplier", 1.2f,
                "Slope speed multiplier (1.0 = vanilla)");
            CfgJumpMult = Config.Bind("Boost", "JumpSlopeMultiplier", 1.2f,
                "Jump-slope speed multiplier (1.0 = vanilla)");
            CfgInertiaDuration = Config.Bind("Inertia", "Duration", 0.8f,
                "How long air inertia lasts after sliding (seconds)");
            CfgFadeStart = Config.Bind("Inertia", "FadeStartTime", 0.2f,
                "When inertia starts fading (seconds after slope)");

            // Hook SlopeCurve (delta-position boost — worked before)
            var slopeM = typeof(PlayerMovement).GetMethod("SlopeCurve",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Vector3) }, null);
            var slopeD = typeof(SlopeBoosterPlugin).GetMethod(nameof(SlopeCurveDetour),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (slopeM != null && slopeD != null)
            {
                _hookSlope = new Hook(slopeM, slopeD);
                _hookSlope.Apply();
            }

            // Inject MonoBehaviour for air inertia (more reliable than AirCurve hook)
            var go = new GameObject("SlopeBooster_Inertia");
            go.AddComponent<InertiaComponent>();
            DontDestroyOnLoad(go);

            Logger.LogInfo($"SlopeBooster loaded! Slope x{CfgSlopeMult.Value}, " +
                $"JumpSlope x{CfgJumpMult.Value}");
        }

        private static void SlopeCurveDetour(
            Action<PlayerMovement, Vector3> orig,
            PlayerMovement self, Vector3 direction)
        {
            var body = GetBody(self);
            if (body == null || (!self.isSliding && !self.sliding))
            {
                orig(self, direction);
                return;
            }

            Vector3 before = body.position;
            orig(self, direction);
            Vector3 delta = body.position - before;
            if (delta.sqrMagnitude < 0.0001f) return;

            bool isJump = (bool?)GetField(self, "isSlopeJump") ?? false;
            float mult = isJump ? CfgJumpMult.Value : CfgSlopeMult.Value;
            body.position += delta * (mult - 1f);

            // Store for inertia (used by InertiaComponent)
            Vector3 total = delta + delta * (mult - 1f);
            SlopeDir = total.normalized;
            SlopeSpeed = total.magnitude / Time.fixedDeltaTime;
            SlopeTime = Time.time;
        }

        public static Transform GetBody(PlayerMovement pm)
        {
            var go = GetField(pm, "bodyRef") as GameObject;
            return go?.transform;
        }

        public static object GetField(object obj, string name)
        {
            var f = obj.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(obj);
        }

        public static float GetInertiaDuration() => CfgInertiaDuration.Value;
        public static float GetFadeStart() => CfgFadeStart.Value;
    }

    /// <summary>
    /// MonoBehaviour that applies slope inertia during air movement.
    /// Runs every FixedUpdate — no hook needed.
    /// </summary>
    public class InertiaComponent : MonoBehaviour
    {
        private PlayerMovement _player;
        private int _frameCount;

        private void FixedUpdate()
        {
            _frameCount++;

            // Find player (lazy)
            if (_player == null)
            {
                var players = FindObjectsOfType<PlayerMovement>();
                foreach (var p in players)
                {
                    if (p.isActiveAndEnabled && p.isLocalPlayer)
                    {
                        _player = p;
                        break;
                    }
                }
                if (_player == null) return;
            }

            if (!_player.isActiveAndEnabled) return;

            float elapsed = Time.time - SlopeBoosterPlugin.SlopeTime;
            if (elapsed > SlopeBoosterPlugin.GetInertiaDuration()) return;
            if (SlopeBoosterPlugin.SlopeSpeed < 0.01f) return;

            // Only apply in air (not sliding, not grounded)
            if (_player.grounded && !_player.jumping) return;
            if (_player.isSliding || _player.sliding) return;

            float fade = Mathf.Clamp01(
                1f - ((elapsed - SlopeBoosterPlugin.GetFadeStart()) /
                      Mathf.Max(SlopeBoosterPlugin.GetInertiaDuration() -
                                SlopeBoosterPlugin.GetFadeStart(), 0.01f)));
            if (fade <= 0f) return;

            // Apply inertia on bodyRef
            var body = SlopeBoosterPlugin.GetBody(_player);
            if (body == null) return;

            Vector3 add = Vector3.ProjectOnPlane(
                SlopeBoosterPlugin.SlopeDir *
                SlopeBoosterPlugin.SlopeSpeed * fade * Time.fixedDeltaTime,
                _player.surfaceNormal);

            if (add.sqrMagnitude > 0.0001f)
                body.position += add;
        }
    }
}
