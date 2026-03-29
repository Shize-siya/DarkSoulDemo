using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SG
{
    public class PlayerLocomotion : MonoBehaviour
    {
        Transform cameraObject;
        InputHandler inputHandler;
        PlayerManager playerManager;
        public Vector3 moveDirection;

        [HideInInspector]
        public Transform mytransform;
        [HideInInspector]
        public AnimatorHandler animatorHandler;

        public new Rigidbody rigidbody;
        public GameObject normalCamera;

        [Header("Ground & Air Detection Stats")]
        [SerializeField]
        float groundDetectionRayStartPoint = 0.5f;
        [SerializeField]
        float minimumDistanceNeededToBeginFall = 1.1f;
        [SerializeField]
        float groundDirectionRayDistance = 0.3f;
        LayerMask ignoreForGroundCheck;
        public float inAirTimer;

        [Header("Stats")]
        [SerializeField]
        float movementSpeed = 8;
        [SerializeField]
        float sprintSpeed = 12;
        [SerializeField]
        float rotationSpeed = 10;
        [SerializeField]
        float fallingSpeed = 45;

        [Header("下落参数")]
        [SerializeField] float airMoveSpeedMultiplier = 0.5f;
        [SerializeField] float airRotationSpeedMultiplier = 0.7f;

        [Header("翻滚优化参数")]
        [SerializeField] float rollRotationSmoothTime = 0.1f;
        private Vector3 _rollDirSmooth;
        private Vector3 _currentRollDirection;

        [Header("方向翻滚配置")]
        [SerializeField] bool useDirectionalRoll = true;
        [SerializeField] float rollDirDeadZone = 0.1f;

        [Header("翻滚推力配置")]
        [SerializeField] float rollImpulseForce = 5f;

        public bool isSprinting;

        [Header("Roll Settings")]
        [SerializeField] float rollStandFrames = 0;
        private int currentStandFrame = 0;

        // 落地动画锁定配置
        [Header("落地动画锁定设置")]
        [SerializeField] private string landAnimName = "Land";
        [SerializeField] private float landAnimEndThreshold = 0.95f;
        private bool isLandAnimPlaying = false;

        [Header("轻攻击动画锁定设置")]
        [SerializeField] private string lightAttackAnimPrefix = "LightAttack";
        [SerializeField] private float lightAttackEndThreshold = 0.9f;

        // 跳跃参数
        [Header("跳跃参数")]
        [SerializeField] private float jumpForce = 7f; // 跳跃向上的力，可根据需求调整
        [SerializeField] private float jumpForwardForce = 2f; // 向前跳跃的额外推力，可选调整
        // 动画完整播放修复：新增跳跃动画锁定配置
        [SerializeField] private string jumpAnimName = "Jump";
        [SerializeField] private float jumpAnimEndThreshold = 0.8f; // 跳跃动画播放到80%再解锁，可微调
        private bool isJumpAnimPlaying = false; // 跳跃动画播放标记

        void Start()
        {
            rigidbody = GetComponent<Rigidbody>();
            inputHandler = GetComponent<InputHandler>();
            animatorHandler = GetComponentInChildren<AnimatorHandler>();
            cameraObject = Camera.main.transform;
            mytransform = transform;
            animatorHandler.Initialize();

            playerManager = GetComponent<PlayerManager>();
            playerManager.isGrounded = true;
            ignoreForGroundCheck = ~(1 << 8 | 1 << 11);

            rigidbody.isKinematic = false;
            rigidbody.freezeRotation = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.useGravity = true;

            rigidbody.drag = 2f;
            rigidbody.angularDrag = 2f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            normalCamera = Camera.main.gameObject;

            _currentRollDirection = mytransform.forward;
        }

        public void Update()
        {
            // 死亡后禁用所有Update逻辑
            if (playerManager.isDead) return;

            float delta = Time.deltaTime;
            isSprinting = inputHandler.sprintFlag;
            inputHandler.TickInput(delta); // 攻击输入始终在这里检测，不锁定

            // 检测落地/攻击/跳跃动画状态
            UpdateLandAnimState();
            UpdateLightAttackAnimState();
            UpdateJumpAnimState(delta); // 动画完整播放修复：新增跳跃动画状态检测

            // 【核心修改】新增跳跃动画锁定，加入位移锁定逻辑
            if (isLandAnimPlaying || isJumpAnimPlaying || inputHandler.isLightAttacking || inputHandler.isTakingDamage)
            {
                // 清空位移相关输入，保留攻击输入
                inputHandler.moveAmount = 0;
                inputHandler.horizontal = 0;
                inputHandler.vertical = 0;
                inputHandler.sprintFlag = false;
                inputHandler.rollFlag = false;
                return;
            }

            if (!playerManager.isInAir)
            {
                HandleRollingAndSprinting(delta);
            }

            if (useDirectionalRoll)
            {
                UpdateRollDirection(delta);
            }
        }

        private void FixedUpdate()
        {
            // 死亡后禁用所有FixedUpdate逻辑
            if (playerManager.isDead)
            {
                rigidbody.velocity = new Vector3(0, rigidbody.velocity.y, 0);
                rigidbody.angularVelocity = Vector3.zero;
                return;
            }

            float fixedDelta = Time.fixedDeltaTime;

            // 【核心修改】新增跳跃动画锁定，加入位移锁定逻辑
            if (isLandAnimPlaying || isJumpAnimPlaying || inputHandler.isLightAttacking || inputHandler.isTakingDamage)
            {
                // 跳跃动画播放时，保留垂直和向前的物理速度，仅锁定手动位移/旋转
                rigidbody.angularVelocity = Vector3.zero;
                animatorHandler.UpdateAnimatorValues(0, 0, false); // 重置动画参数
                return;
            }

            HandleMovement(fixedDelta);
            HandleFalling(fixedDelta, moveDirection);

            AnimatorStateInfo stateInfo = animatorHandler.anim.GetCurrentAnimatorStateInfo(0);
            bool isRolling = (stateInfo.IsName("Rolling") || stateInfo.IsName("BackStep") ||
                             stateInfo.IsName("RollLeft") || stateInfo.IsName("RollRight"))
                             && stateInfo.normalizedTime < 0.95f;
            if (isRolling)
            {
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.position = Vector3.Lerp(rigidbody.position, mytransform.position, 0.5f);
            }
        }

        #region 动画状态检测 - 新增跳跃动画检测
        private void UpdateLightAttackAnimState()
        {
            if (playerManager.isDead) return;

            AnimatorStateInfo stateInfo = animatorHandler.anim.GetCurrentAnimatorStateInfo(0);

            // 兼容自定义前缀 + WeaponItem标准攻击动画名
            bool isLightAttackAnim = stateInfo.IsName(lightAttackAnimPrefix) ||
                                     stateInfo.IsName(lightAttackAnimPrefix + "1") ||
                                     stateInfo.IsName(lightAttackAnimPrefix + "2") ||
                                     stateInfo.IsName("OH_Light_Attack_1") ||
                                     stateInfo.IsName("OH_Light_Attack_2");

            if (isLightAttackAnim)
            {
                inputHandler.isLightAttacking = stateInfo.normalizedTime < lightAttackEndThreshold;
            }
            else
            {
                if (inputHandler.isLightAttacking)
                {
                    inputHandler.isLightAttacking = false;
                }
            }
        }

        private void UpdateLandAnimState()
        {
            if (playerManager.isDead || !isLandAnimPlaying)
            {
                return;
            }

            AnimatorStateInfo stateInfo = animatorHandler.anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName(landAnimName) && stateInfo.normalizedTime >= landAnimEndThreshold)
            {
                isLandAnimPlaying = false;
            }
        }

        // 动画完整播放修复：新增跳跃动画状态检测，锁定直到播放到指定阈值
        private void UpdateJumpAnimState(float delta)
        {
            if (playerManager.isDead || !isJumpAnimPlaying)
            {
                return;
            }

            AnimatorStateInfo stateInfo = animatorHandler.anim.GetCurrentAnimatorStateInfo(0);
            // 跳跃动画播放到指定阈值，且角色未落地时也解锁（避免空中一直锁定）
            if (stateInfo.IsName(jumpAnimName) && stateInfo.normalizedTime >= jumpAnimEndThreshold)
            {
                isJumpAnimPlaying = false;
            }
        }
        #endregion

        #region 动态更新翻滚方向
        private void UpdateRollDirection(float delta)
        {
            Vector3 inputDir = cameraObject.forward * inputHandler.vertical + cameraObject.right * inputHandler.horizontal;
            inputDir.y = 0;

            if (inputDir.magnitude > rollDirDeadZone)
            {
                _currentRollDirection = Vector3.SmoothDamp(_currentRollDirection, inputDir.normalized, ref _rollDirSmooth, rollRotationSmoothTime);
            }
        }
        #endregion

        #region Movement
        Vector3 normalVector = Vector3.up;
        Vector3 targetPosition;

        private void HandleRotation(float delta)
        {
            Vector3 targetDir = Vector3.zero;
            float moveOverride = inputHandler.moveAmount;

            targetDir = cameraObject.forward * inputHandler.vertical;
            targetDir += cameraObject.right * inputHandler.horizontal;

            targetDir.Normalize();
            targetDir.y = 0;

            if (targetDir == Vector3.zero)
                targetDir = mytransform.forward;

            float rs = rotationSpeed;
            if (playerManager.isInAir)
            {
                rs *= airRotationSpeedMultiplier;
            }

            if (isSprinting)
                rs *= 1.5f;

            Quaternion tr = Quaternion.LookRotation(targetDir);
            Quaternion targetRotation = Quaternion.Slerp(mytransform.rotation, tr, rs * delta);
            mytransform.rotation = targetRotation;
        }

        public void HandleMovement(float delta)
        {
            if (inputHandler.moveAmount <= 0)
            {
                rigidbody.velocity = new Vector3(0, rigidbody.velocity.y, 0);
                animatorHandler.UpdateAnimatorValues(0, 0, false);
                return;
            }

            if (inputHandler.isInteracting)
                return;

            moveDirection = cameraObject.forward * inputHandler.vertical;
            moveDirection += cameraObject.right * inputHandler.horizontal;
            moveDirection.Normalize();
            moveDirection.y = 0;

            float speed = isSprinting ? sprintSpeed : movementSpeed;
            if (playerManager.isInAir)
            {
                speed *= airMoveSpeedMultiplier;
            }

            moveDirection *= speed;

            rigidbody.velocity = new Vector3(moveDirection.x, rigidbody.velocity.y, moveDirection.z);
            animatorHandler.UpdateAnimatorValues(inputHandler.moveAmount, 0, isSprinting);

            if (animatorHandler.canRotate)
            {
                HandleRotation(delta);
            }
        }

        public void HandleRollingAndSprinting(float delta)
        {
            AnimatorStateInfo stateInfo = animatorHandler.anim.GetCurrentAnimatorStateInfo(0);
            bool isRolling = (stateInfo.IsName("Rolling") || stateInfo.IsName("BackStep") ||
                             stateInfo.IsName("RollLeft") || stateInfo.IsName("RollRight"))
                             && stateInfo.normalizedTime < 0.9f;

            if (isRolling)
            {
                if (_currentRollDirection.magnitude > 0)
                {
                    Quaternion targetRot = Quaternion.LookRotation(_currentRollDirection);
                    mytransform.rotation = Quaternion.Slerp(mytransform.rotation, targetRot, delta * 20f);
                }
                return;
            }

            if (inputHandler.rollFlag)
            {
                currentStandFrame++;

                if (currentStandFrame >= rollStandFrames)
                {
                    UpdateRollDirection(delta);
                    string rollAnimName = GetDirectionalRollAnimation();
                    animatorHandler.PlayTargetAnimation(rollAnimName, true);

                    if (_currentRollDirection.magnitude > 0)
                    {
                        rigidbody.AddForce(_currentRollDirection * rollImpulseForce, ForceMode.Impulse);
                    }

                    if (_currentRollDirection.magnitude > 0)
                    {
                        Quaternion rollRotation = Quaternion.LookRotation(_currentRollDirection);
                        mytransform.rotation = Quaternion.Slerp(mytransform.rotation, rollRotation, 0.5f);
                    }

                    currentStandFrame = 0;
                    inputHandler.rollFlag = false;
                    inputHandler.sprintFlag = false;
                }
            }
            else
            {
                currentStandFrame = 0;
            }
        }

        #region 根据输入方向选择翻滚动画
        private string GetDirectionalRollAnimation()
        {
            if (!useDirectionalRoll)
            {
                return inputHandler.moveAmount > 0 ? "Rolling" : "BackStep";
            }

            float v = inputHandler.vertical;
            float h = inputHandler.horizontal;

            if (v > rollDirDeadZone && Mathf.Abs(h) <= rollDirDeadZone)
                return "Rolling";
            else if (v < -rollDirDeadZone && Mathf.Abs(h) <= rollDirDeadZone)
                return "BackStep";
            else if (h > rollDirDeadZone && Mathf.Abs(v) <= rollDirDeadZone)
                return "RollRight";
            else if (h < -rollDirDeadZone && Mathf.Abs(v) <= rollDirDeadZone)
                return "RollLeft";
            else if (v > 0)
                return "Rolling";
            else
                return "BackStep";
        }
        #endregion

        public void HandleFalling(float delta, Vector3 moveDirection)
        {
            if (playerManager.isDead) return;

            playerManager.isGrounded = false;
            RaycastHit hit;
            Vector3 origin = mytransform.position;
            origin.y += groundDetectionRayStartPoint;

            if (Physics.Raycast(origin, mytransform.forward, out hit, 0.6f))
            {
                moveDirection = Vector3.zero;
            }

            if (playerManager.isInAir)
            {
                rigidbody.AddForce(-Vector3.up * fallingSpeed);
                rigidbody.AddForce(moveDirection * fallingSpeed / 5f);
            }

            Vector3 dir = moveDirection;
            dir.Normalize();
            origin = origin + dir * groundDirectionRayDistance;

            targetPosition = mytransform.position;

            Debug.DrawRay(origin, -Vector3.up * minimumDistanceNeededToBeginFall, Color.red, 0.1f, false);
            if (Physics.Raycast(origin, -Vector3.up, out hit, minimumDistanceNeededToBeginFall, ignoreForGroundCheck))
            {
                normalVector = hit.normal;
                Vector3 tp = hit.point;
                playerManager.isGrounded = true;
                targetPosition.y = tp.y;

                if (playerManager.isInAir)
                {
                    if (inAirTimer > 0.5f)
                    {
                        animatorHandler.PlayTargetAnimation(landAnimName, true);
                        isLandAnimPlaying = true;
                    }
                    else
                    {
                        animatorHandler.PlayTargetAnimation("Empty", false);
                        isLandAnimPlaying = false;
                    }
                    playerManager.isInAir = false;
                    inAirTimer = 0;
                    isJumpAnimPlaying = false; // 落地后强制解锁跳跃动画锁定
                }
            }
            else
            {
                if (playerManager.isGrounded)
                {
                    playerManager.isGrounded = false;
                }

                if (playerManager.isInAir == false)
                {
                    if (inputHandler.isInteracting == false && !isJumpAnimPlaying) // 排除跳跃动画状态
                    {
                        animatorHandler.PlayTargetAnimation("Falling", true);
                    }

                    Vector3 vel = rigidbody.velocity;
                    vel.Normalize();
                    rigidbody.velocity = vel * (movementSpeed / 2);
                    playerManager.isInAir = true;
                }
            }

            if (inputHandler.isInteracting || inputHandler.moveAmount > 0)
            {
                mytransform.position = Vector3.Lerp(mytransform.position, targetPosition, Time.deltaTime / 0.1f);
            }
            else
            {
                mytransform.position = targetPosition;
            }

            if (playerManager.isGrounded)
            {
                if (inputHandler.isInteracting || inputHandler.moveAmount > 0)
                {
                    mytransform.position = Vector3.Lerp(mytransform.position, targetPosition, delta);
                }
                else
                {
                    mytransform.position = targetPosition;
                }
            }
        }

        public void HandleJumping()
        {
            if (inputHandler.isInteracting)
                return;
            // 仅在地面且跳跃输入为true时触发跳跃
            if (inputHandler.jump_input && playerManager.isGrounded && !playerManager.isInAir && !isJumpAnimPlaying)
            {
                // 重置垂直速度，避免重力叠加导致跳跃高度异常
                rigidbody.velocity = new Vector3(rigidbody.velocity.x, 0, rigidbody.velocity.z);

                // 施加向上冲量（核心跳跃力）
                rigidbody.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
                // 向前跳跃时添加额外向前推力，让方向跳跃更自然
                if (inputHandler.moveAmount > 0)
                {
                    moveDirection = cameraObject.forward * inputHandler.vertical + cameraObject.right * inputHandler.horizontal;
                    moveDirection.y = 0;
                    moveDirection.Normalize();
                    rigidbody.AddForce(moveDirection * jumpForwardForce, ForceMode.Impulse);

                    Quaternion jumprotation = Quaternion.LookRotation(moveDirection);
                    mytransform.rotation = jumprotation;
                }

                // 动画完整播放修复：播放跳跃动画并标记锁定状态
                animatorHandler.PlayTargetAnimation(jumpAnimName, true);
                isJumpAnimPlaying = true; // 锁定跳跃动画，直到播放到阈值/落地

                // 标记为空中状态，触发下落逻辑
                playerManager.isInAir = true;
                // 清空跳跃输入，防止连续跳跃
                inputHandler.jump_input = false;
            }
        }
        #endregion
    }
}