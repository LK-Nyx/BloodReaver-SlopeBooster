using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace SlideBooster
{
    [BepInPlugin("slidebooster", "Slide Booster", "1.0.0")]
    public class SlideBoosterPlugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> CfgSlideMult;
        private static ConfigEntry<float> CfgJumpMult;
        private static ConfigEntry<float> CfgInertiaDuration;
        private static ConfigEntry<float> CfgFadeStart;

        // MonoMod hooks (static field = GC root)
        private static Hook _hookSlide;

        // Shared state for inertia component
        internal static Vector3 SlideDir;
        internal static float SlideSpeed;
        internal static float SlideTime;

        private void Awake()
        {
            CfgSlideMult = Config.Bind("Boost", "SlideMultiplier", 1.2f,
                "Slide speed multiplier (1.0 = vanilla)");
            CfgJumpMult = Config.Bind("Boost", "JumpSlideMultiplier", 1.2f,
                "Jump-slide speed multiplier (1.0 = vanilla)");
            CfgInertiaDuration = Config.Bind("Inertia", "Duration", 0.8f,
                "How long air inertia lasts after sliding (seconds)");
            CfgFadeStart = Config.Bind("Inertia", "FadeStartTime", 0.2f,
                "When inertia starts fading (seconds after slide)");

            // Hook SlideCurve (delta-position boost — worked before)
            var slideM = typeof(PlayerMovement).GetMethod("SlideCurve",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Vector3) }, null);
            var slideD = typeof(SlideBoosterPlugin).GetMethod(nameof(SlideCurveDetour),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (slideM != null && slideD != null)
            {
                _hookSlide = new Hook(slideM, slideD);
                _hookSlide.Apply();
            }

            // Inject MonoBehaviour for air inertia (more reliable than AirCurve hook)
            var go = new GameObject("SlideBooster_Inertia");
            go.AddComponent<InertiaComponent>();
            DontDestroyOnLoad(go);

            Logger.LogInfo($"SlideBooster loaded! Slide x{CfgSlideMult.Value}, " +
                $"JumpSlide x{CfgJumpMult.Value}");
        }

        private static void SlideCurveDetour(
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

            bool isJump = (bool?)GetField(self, "isSlideJump") ?? false;
            float mult = isJump ? CfgJumpMult.Value : CfgSlideMult.Value;
            body.position += delta * (mult - 1f);

            // Store for inertia (used by InertiaComponent)
            Vector3 total = delta + delta * (mult - 1f);
            SlideDir = total.normalized;
            SlideSpeed = total.magnitude / Time.fixedDeltaTime;
            SlideTime = Time.time;
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
    /// MonoBehaviour that applies slide inertia during air movement.
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

            float elapsed = Time.time - SlideBoosterPlugin.SlideTime;
            if (elapsed > SlideBoosterPlugin.GetInertiaDuration()) return;
            if (SlideBoosterPlugin.SlideSpeed < 0.01f) return;

            // Only apply in air (not sliding, not grounded)
            if (_player.grounded && !_player.jumping) return;
            if (_player.isSliding || _player.sliding) return;

            float fade = Mathf.Clamp01(
                1f - ((elapsed - SlideBoosterPlugin.GetFadeStart()) /
                      Mathf.Max(SlideBoosterPlugin.GetInertiaDuration() -
                                SlideBoosterPlugin.GetFadeStart(), 0.01f)));
            if (fade <= 0f) return;

            // Apply inertia on bodyRef
            var body = SlideBoosterPlugin.GetBody(_player);
            if (body == null) return;

            Vector3 add = Vector3.ProjectOnPlane(
                SlideBoosterPlugin.SlideDir *
                SlideBoosterPlugin.SlideSpeed * fade * Time.fixedDeltaTime,
                _player.surfaceNormal);

            if (add.sqrMagnitude > 0.0001f)
                body.position += add;
        }
    }
}
