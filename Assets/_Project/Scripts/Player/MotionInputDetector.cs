using System.Collections.Generic;
using UnityEngine;
using KungFuVania.Combat;
using KungFuVania.Input;

namespace KungFuVania.Player
{
    // Real-time trie resolution over the player's active skill loadout — see GAME_PLAN.md 3l.
    // Only evaluates while PlayerController.ChiModeActive is true. Every runtime-needed value is
    // baked onto the trie nodes at build time; the walk itself never computes or looks anything
    // up beyond a dictionary read.
    [RequireComponent(typeof(PlayerController))]
    public class MotionInputDetector : MonoBehaviour
    {
        [SerializeField] private StickInputConfigSO stickConfig;
        [SerializeField] private SkillLoadout skillLoadout;

        private class TrieNode
        {
            // Keyed by zone (1-9) — any zone in a step's group gets its own entry pointing at
            // the same shared child, so satisfying a step via any zone in its group converges.
            public readonly Dictionary<int, TrieNode> children = new();
            public EffectSO effectOnComplete;
            public MotionInputTriggerSO sourceTrigger;
            public float effectiveWindow;
            // Non-null if arriving at this node satisfies/starts a charge requirement.
            public int[] chargeZones;
            public float chargeMinHoldDuration;
        }

        private PlayerController controller;
        private TrieNode root;
        private TrieNode currentNode;

        private bool wasChiModeActive;
        private int lastZone = -1;
        private float attemptStartTime;

        // Charge-hold tracking for whichever charge node we're currently sitting at (if any).
        private int[] chargeZones;
        private float chargeHoldDuration;
        private float chargeMinHoldDuration;
        private bool chargeSatisfied;

        private void Awake() => controller = GetComponent<PlayerController>();

        private void Start() => BuildTrie();

        // Rebuilt on the active loadout changing would normally hook in here too — no menu/
        // loadout-editing UI exists yet, so build-once on Start covers the current scope.
        private void BuildTrie()
        {
            root = new TrieNode();
            if (skillLoadout == null)
            {
                ResetToRoot();
                return;
            }

            foreach (var entry in skillLoadout.ActiveMotionInputs())
            {
                var motionTrigger = (MotionInputTriggerSO)entry.trigger;
                var node = root;

                foreach (var step in motionTrigger.sequence)
                {
                    TrieNode next = null;
                    foreach (var zone in step.zones)
                    {
                        if (node.children.TryGetValue(zone, out var existing))
                        {
                            next = existing;
                            break;
                        }
                    }
                    next ??= new TrieNode();

                    foreach (var zone in step.zones) node.children[zone] = next;

                    next.effectiveWindow = Mathf.Max(next.effectiveWindow, motionTrigger.sequenceWindow);
                    if (step.minHoldDuration > 0f)
                    {
                        next.chargeZones = step.zones;
                        next.chargeMinHoldDuration = step.minHoldDuration;
                    }

                    node = next;
                }

                node.effectOnComplete = entry.effect;
                node.sourceTrigger = motionTrigger;
            }

            ResetToRoot();
        }

        private void ResetToRoot()
        {
            currentNode = root;
            chargeZones = null;
            chargeHoldDuration = 0f;
            chargeSatisfied = false;
        }

        private void Update()
        {
            var active = controller.ChiModeActive;

            if (!active)
            {
                if (wasChiModeActive) ResetToRoot();
                wasChiModeActive = false;
                lastZone = -1;
                return;
            }

            if (!wasChiModeActive)
            {
                ResetToRoot();
                lastZone = -1; // force this session's first real zone to register as a change
            }
            wasChiModeActive = true;

            var zone = stickConfig != null ? stickConfig.ComputeZone(controller.MoveInput, controller.FacingRight) : 5;

            TickChargeHold(zone);

            if (zone != lastZone)
            {
                OnZoneChanged(zone);
                lastZone = zone;
            }
        }

        // Accumulates hold time whenever the current zone is still within the active charge
        // group, tolerating brief drift between that group's own zones (GAME_PLAN.md 3l) — an
        // off-group frame simply doesn't accumulate, it doesn't reset progress either. Marks the
        // charge satisfied the instant the requirement is met and starts the clock for whatever
        // transition step follows, rather than refreshing it on every later step.
        private void TickChargeHold(int zone)
        {
            if (chargeZones == null || chargeSatisfied) return;
            if (System.Array.IndexOf(chargeZones, zone) < 0) return;

            chargeHoldDuration += Time.deltaTime;
            if (chargeHoldDuration >= chargeMinHoldDuration)
            {
                chargeSatisfied = true;
                attemptStartTime = Time.time;
            }
        }

        private void OnZoneChanged(int zone)
        {
            // Charge steps are exempt from the timeout during the hold itself — see
            // TickChargeHold. Once satisfied, the clock (reset there) governs normally again.
            var chargingCurrentStep = chargeZones != null && !chargeSatisfied;
            if (!chargingCurrentStep && currentNode != root && Time.time - attemptStartTime > currentNode.effectiveWindow)
            {
                ResetToRoot();
            }

            if (!currentNode.children.TryGetValue(zone, out var next))
            {
                // Mismatch: stay put, not a reset — reproduces skip-mode tolerance (GAME_PLAN.md 3l).
                return;
            }

            var wasRoot = currentNode == root;
            currentNode = next;
            if (wasRoot) attemptStartTime = Time.time;

            if (currentNode.chargeZones != null)
            {
                chargeZones = currentNode.chargeZones;
                chargeMinHoldDuration = currentNode.chargeMinHoldDuration;
                chargeHoldDuration = 0f;
                chargeSatisfied = false;
            }
            else
            {
                chargeZones = null;
            }

            // Reaching a completion node here does NOT fire it — only a matching confirm-button
            // press does (TryFireCompletedMotion below). The doc as originally written read as
            // auto-firing here, which contradicted confirmButton's own existence; fixed in
            // GAME_PLAN.md 3l alongside this implementation.
        }

        // Called from PlayerController's attack-button handling, using the state as of the press.
        // True only if this exact button confirms whatever pattern the walk is currently sitting
        // on; false falls through to a normal buffered attack.
        public bool TryFireCompletedMotion(string attackAction)
        {
            if (!controller.ChiModeActive) return false;
            if (currentNode == null || currentNode.effectOnComplete == null) return false;
            if (currentNode.sourceTrigger == null || currentNode.sourceTrigger.confirmButton != attackAction) return false;

            currentNode.effectOnComplete.Execute(gameObject);
            ResetToRoot();
            return true;
        }
    }
}
