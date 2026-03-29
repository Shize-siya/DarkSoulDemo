using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SG
{
    public class AnimatorHandler : MonoBehaviour
    {
        public Animator anim;
        public bool canRotate;
        private InputHandler _input;
        private PlayerLocomotion _locomotion;
        private int _verticalHash;
        private int _horizontalHash;
        private int _isSprintingHash;

        [Header("动画过渡参数")]
        public float rollFadeTime = 0.1f;
        public float normalFadeTime = 0.15f;

        [Header("翻滚距离配置")]
        [SerializeField] float rollDistanceMultiplier = 2.0f;
        [SerializeField] string[] rollAnimNames = { "Rolling", "BackStep", "RollLeft", "RollRight" };

        private PlayerManager _playerManager;

        public void Initialize()
        {
            anim = GetComponent<Animator>();
            _input = GetComponentInParent<InputHandler>();
            _locomotion = GetComponentInParent<PlayerLocomotion>();
            _playerManager = GetComponentInParent<PlayerManager>();

            _verticalHash = Animator.StringToHash("Vertical");
            _horizontalHash = Animator.StringToHash("Horizontal");
            _isSprintingHash = Animator.StringToHash("isSprinting");
        }

        public void UpdateAnimatorValues(float vertical, float horizontal, bool isSprinting)
        {
            // 死亡后停止更新动画参数
            if (_playerManager != null && _playerManager.isDead)
            {
                anim.SetFloat(_verticalHash, 0, 0.1f, Time.deltaTime);
                anim.SetFloat(_horizontalHash, 0, 0.1f, Time.deltaTime);
                anim.SetBool(_isSprintingHash, false);
                return;
            }

            float v = Mathf.Clamp(vertical, -1f, 1f);
            if (v > 0 && v < 0.55f) v = 0.5f;
            else if (v > 0.55f) v = 1f;
            else if (v < 0 && v > -0.55f) v = -0.5f;
            else if (v < -0.55f) v = -1f;
            else v = 0f;

            if (isSprinting && vertical > 0) v = 2f;

            anim.SetFloat(_verticalHash, v, 0.1f, Time.deltaTime);
            anim.SetFloat(_horizontalHash, horizontal, 0.1f, Time.deltaTime);
            anim.SetBool(_isSprintingHash, isSprinting);
        }

        public void PlayTargetAnimation(string animName, bool isInteracting)
        {
            // 死亡后仅允许播放死亡动画，其他动画禁止播放
            if (_playerManager != null && _playerManager.isDead && animName != "Death_01")
            {
                return;
            }

            anim.applyRootMotion = isInteracting;
            anim.SetBool("isInteracting", isInteracting);

            float fadeTime = animName.Contains("Roll") || animName.Contains("BackStep") ? rollFadeTime : normalFadeTime;
            anim.CrossFade(animName, fadeTime, 0, 0f);
        }

        public void EnableCombo()
        {
            anim.SetBool("canDoCombo", true);
        }

        public void DisableCombo()
        {
            anim.SetBool("canDoCombo",false);
        }

        private void OnAnimatorMove()
        {
            if (_playerManager != null && _playerManager.isDead) return;

            if (!_input.isInteracting) return;

            Transform root = anim.transform.root;
            Vector3 deltaPos = anim.deltaPosition;

            AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            foreach (string rollAnim in rollAnimNames)
            {
                if (stateInfo.IsName(rollAnim))
                {
                    deltaPos *= rollDistanceMultiplier;
                    break;
                }
            }

            Vector3 targetPos = root.position + deltaPos;
            root.position = Vector3.Lerp(root.position, targetPos, 0.8f);
            root.rotation *= anim.deltaRotation;

            Vector3 vel = _locomotion.rigidbody.velocity;
            _locomotion.rigidbody.velocity = new Vector3(0, vel.y, 0);
        }
    }
}