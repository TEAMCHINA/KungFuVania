using System;
using UnityEngine;

namespace KungFuVania.Player.Combat
{
    // Generic "play this clip, fire a callback partway through it, return to NONE when it ends"
    // combat state — replaces the fireball-specific ThrowFireballState. Any AbilityEffectSO that
    // needs to do something mid-animation (spawn a projectile, apply a buff partway through a
    // cast pose, whatever comes next) constructs one of these itself with its own clip/callback,
    // instead of PlayerCombatStateMachine needing dedicated fields and a dedicated state class per
    // ability. See PlayerCombatStateMachine.TryEnterAbilityState — that's the entry point this
    // pairs with, since a dynamically-constructed state was never pre-registered in the states
    // dictionary the way ATTACK_1/etc. are.
    //
    // Mid-clip timing is computed from the clip's OWN current frameRate/length every Enter, not a
    // baked Animation Event timestamp — see the constructor comment on why.
    public class AbilityAnimationState : ICombatState
    {
        private readonly PlayerCombatStateMachine machine;
        private readonly string animStateId;
        private readonly AnimationClip clip;
        private readonly int midClipFrameIndex;
        private readonly Action onMidClipFrame;

        private float midClipNormalizedTime;
        private bool midClipFired;

        // midClipFrameIndex is 0-indexed (2 = the 3rd frame). Reading frameRate/length live off
        // the clip means retiming it later (different fps or frame count) just works on the next
        // play, with no re-authoring step required — a baked Animation Event's time does not
        // recompute if the clip's sample rate changes, and silently drifts off the intended frame.
        public AbilityAnimationState(PlayerCombatStateMachine machine, string animStateId, AnimationClip clip,
            int midClipFrameIndex, Action onMidClipFrame)
        {
            this.machine = machine;
            this.animStateId = animStateId;
            this.clip = clip;
            this.midClipFrameIndex = midClipFrameIndex;
            this.onMidClipFrame = onMidClipFrame;
        }

        public void Enter()
        {
            midClipFired = false;
            midClipNormalizedTime = clip != null && clip.length > 0f
                ? (midClipFrameIndex / clip.frameRate) / clip.length
                : 0f;
        }

        public void Tick(float deltaTime)
        {
            var info = machine.Animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(animStateId)) return;

            if (!midClipFired && info.normalizedTime >= midClipNormalizedTime)
            {
                onMidClipFrame?.Invoke();
                midClipFired = true;
            }

            if (info.normalizedTime >= 1f)
                machine.ChangeState("NONE");
        }

        public void FixedTick(float fixedDeltaTime) { }

        public void Exit() { }
    }
}
