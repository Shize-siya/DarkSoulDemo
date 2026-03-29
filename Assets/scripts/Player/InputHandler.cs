using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SG
{
    public class InputHandler : MonoBehaviour
    {
        public float horizontal;
        public float vertical;
        public float moveAmount;
        public float mouseX;
        public float mouseY;

        public bool rb_Input;
        public bool rt_Input;
        public bool jump_input;
        public bool a_Input;
        public bool inventory_input;

        public bool d_Pad_Up;
        public bool d_Pad_Down;
        public bool d_Pad_Left;
        public bool d_Pad_Right;

        public bool isHoldingShift;
        public bool rollFlag;
        public bool sprintFlag;
        public bool comboFlag;
        public bool inventoryFlag;
        public bool isInteracting;

        public bool isLightAttacking;
        public bool isTakingDamage;

        private PlayerManager _playerManager;
        private AnimatorHandler animatorHandler;

        [Header("判定阈值")]
        public float rollTime = 0.25f;
        public float sprintTime = 0.3f;

        private float _holdTimer;
        private PlayerControls _inputActions;
        private CameraHandler _cameraHandler;
        private PlayerAttacker playerAttacker;
        private PlayerInventory playerInventory;
        private PlayerStats playerStats;
        private WeaponSlotManager weaponSlotManager;
        private UIManager uiManager;

        private float _interactTimer;
        private bool _comboInputBuffered;

        private void Awake()
        {
            _cameraHandler = CameraHandler.singleton;
            _playerManager = GetComponent<PlayerManager>();
            playerAttacker = GetComponent<PlayerAttacker>();
            playerInventory = GetComponent<PlayerInventory>();
            animatorHandler = GetComponentInChildren<AnimatorHandler>();
            playerStats = GetComponent<PlayerStats>();
            weaponSlotManager = GetComponentInChildren<WeaponSlotManager>();
            uiManager = FindObjectOfType<UIManager>();
        }

        private void FixedUpdate()
        {
            if (_cameraHandler != null)
            {
                _cameraHandler.FollowTarget(Time.fixedDeltaTime);
                _cameraHandler.HandleCameraRotation(Time.fixedDeltaTime, mouseX, mouseY);
            }
        }

        private void Update()
        {
            // 跳跃修复：移除冗余的a_Input打印，避免控制台刷屏
            // Debug.Log("a_Input 当前值: " + a_Input);
            if (animatorHandler == null || animatorHandler.anim == null) return;

            comboFlag = animatorHandler.anim.GetBool("canDoCombo");
            animatorHandler.anim.SetBool("comboFlag", comboFlag);

            AnimatorStateInfo stateInfo = animatorHandler.anim.GetCurrentAnimatorStateInfo(0);
            bool isInLightAttack = stateInfo.IsName("OH_Light_Attack_1") || stateInfo.IsName("OH_Light_Attack_2");
            isLightAttacking = isInLightAttack;
        }

        public void OnEnable()
        {
            if (_inputActions == null)
            {
                _inputActions = new PlayerControls();
                // 移动和相机输入
                _inputActions.PlayerMovement.Movement.performed += ctx =>
                    SetMovementInput(ctx.ReadValue<Vector2>());
                _inputActions.PlayerMovement.Camera.performed += ctx =>
                    SetCameraInput(ctx.ReadValue<Vector2>());
                // 翻滚/冲刺输入
                _inputActions.PlayerActions.Roll.performed += _ => OnShiftDown();
                _inputActions.PlayerActions.Roll.canceled += _ => OnShiftUp();
                // 攻击输入
                _inputActions.PlayerActions.RB.performed += i => rb_Input = true;
                _inputActions.PlayerActions.RB.canceled += i => rb_Input = false;
                _inputActions.PlayerActions.RT.performed += i => rt_Input = true;
                _inputActions.PlayerActions.RT.canceled += i => rt_Input = false;
                // 方向键换武器输入
                _inputActions.PlayerWeapons.DPadRight.performed += i => d_Pad_Right = true;
                _inputActions.PlayerWeapons.DPadRight.canceled += i => d_Pad_Right = false;
                _inputActions.PlayerWeapons.DPadLeft.performed += i => d_Pad_Left = true;
                _inputActions.PlayerWeapons.DPadLeft.canceled += i => d_Pad_Left = false;
                // 修复：将A键输入监听移到OnEnable，仅绑定一次，同时添加抬起事件
                _inputActions.PlayerActions.A.performed += i => a_Input = true;
                _inputActions.PlayerActions.A.canceled += i => a_Input = false;
                // 跳跃修复：将跳跃输入监听移到OnEnable，仅绑定一次，避免Update中重复绑定导致输入异常
                _inputActions.PlayerActions.Jump.performed += indexer => jump_input = true;
                _inputActions.PlayerActions.Jump.canceled += indexer => jump_input = false;
            }
            _inputActions.Enable();
        }

        private void OnDisable() => _inputActions.Disable();

        private void SetMovementInput(Vector2 input)
        {
            if (_playerManager != null && (_playerManager.isDead || _playerManager.isInAir || isTakingDamage))
            {
                horizontal = 0;
                vertical = 0;
                moveAmount = 0;
                return;
            }
            horizontal = input.x;
            vertical = input.y;
            moveAmount = Mathf.Clamp01(Mathf.Abs(horizontal) + Mathf.Abs(vertical));
        }

        private void SetCameraInput(Vector2 input)
        {
            mouseX = input.x;
            mouseY = input.y;
        }

        private void OnShiftDown()
        {
            if (_playerManager != null && _playerManager.isDead || isInteracting || isLightAttacking || isTakingDamage || (_playerManager != null && _playerManager.isInAir)) return;
            isHoldingShift = true;
            _holdTimer = 0;
        }

        private void OnShiftUp()
        {
            if (_playerManager != null && _playerManager.isDead)
            {
                isHoldingShift = false;
                sprintFlag = false;
                rollFlag = false;
                _holdTimer = 0;
                return;
            }

            if (_playerManager != null && _playerManager.isInAir)
            {
                isHoldingShift = false;
                sprintFlag = false;
                rollFlag = false;
                _holdTimer = 0;
                return;
            }

            isHoldingShift = false;
            if (_holdTimer < rollTime && !sprintFlag)
            {
                rollFlag = true;
            }
            sprintFlag = false;
            _holdTimer = 0;
        }

        public void TickInput(float delta)
        {
            if (_playerManager != null && _playerManager.isDead)
            {
                ResetAllInputState();
                return;
            }

            if (_playerManager != null && (_playerManager.isInAir || isTakingDamage))
            {
                sprintFlag = false;
                rollFlag = false;
                _interactTimer = 0;
                _holdTimer = 0;
            }

            if (isInteracting)
            {
                _interactTimer += delta;
                if (_interactTimer >= 2.0f)
                {
                    isInteracting = false;
                    rollFlag = false;
                    sprintFlag = false;
                    _interactTimer = 0;
                }
                return;
            }

            _interactTimer = 0;

            if (isHoldingShift && !isLightAttacking)
            {
                _holdTimer += delta;
                if (_holdTimer >= sprintTime)
                {
                    sprintFlag = true;
                    rollFlag = false;
                    float moveMag = Mathf.Clamp01(Mathf.Abs(horizontal) + Mathf.Abs(vertical));
                    if (moveMag > 0)
                    {
                        horizontal /= moveMag;
                        vertical /= moveMag;
                        moveAmount = 1f;
                    }
                }
            }

            HandleAttackInput(delta);
            HandleQuickSlotsInput();
            HandleInventoryInput();
        }

        private void ResetAllInputState()
        {
            isInteracting = false;
            rollFlag = false;
            sprintFlag = false;
            isLightAttacking = false;
            isTakingDamage = false;
            _comboInputBuffered = false;
            comboFlag = false;
            rb_Input = false;
            rt_Input = false;
            a_Input = false; // 新增：重置A键输入
            jump_input = false; // 跳跃修复：重置跳跃输入
            _interactTimer = 0;
            _holdTimer = 0;
            horizontal = 0;
            vertical = 0;
            moveAmount = 0;

            if (animatorHandler != null)
            {
                animatorHandler.DisableCombo();
                animatorHandler.anim.SetBool("comboFlag", false);
                animatorHandler.anim.SetBool("isInteracting", false);
            }
        }

        public void ResetRollFlag() => rollFlag = false;

        public void EnableComboWindow()
        {
            _comboInputBuffered = false;
            if (animatorHandler != null)
            {
                animatorHandler.EnableCombo();
            }
        }

        public void DisableComboWindow()
        {
            _comboInputBuffered = false;
            rb_Input = false;
            rt_Input = false;

            if (animatorHandler != null)
            {
                animatorHandler.DisableCombo();
                animatorHandler.anim.SetBool("comboFlag", false);
                animatorHandler.anim.SetBool("isInteracting", false);
            }
        }

        private void HandleAttackInput(float delta)
        {
            if (_playerManager != null && _playerManager.isDead || isTakingDamage)
                return;
            if (playerInventory.rightWeapon == null)
                return;

            if (rb_Input)
            {
                int lightStaminaCost = Mathf.RoundToInt(playerInventory.rightWeapon.baseStamina * playerInventory.rightWeapon.lightAttackMultiplier);
                if (playerStats.currentStamina < lightStaminaCost)
                {
                    rb_Input = false;
                    return;
                }

                rb_Input = false;
                if (!isLightAttacking)
                {
                    isLightAttacking = true;
                    playerAttacker.HandleLightAttack(playerInventory.rightWeapon);
                }
                else if (comboFlag)
                {
                    playerAttacker.HandleWeaponCombo(playerInventory.rightWeapon);
                    DisableComboWindow();
                }
                else
                {
                    playerAttacker.HandleLightAttack(playerInventory.rightWeapon);
                    DisableComboWindow();
                }
            }
            if (rt_Input)
            {
                int heavyStaminaCost = Mathf.RoundToInt(playerInventory.rightWeapon.baseStamina * playerInventory.rightWeapon.heavyAttackMultiplier);
                if (playerStats.currentStamina < heavyStaminaCost)
                {
                    rt_Input = false;
                    return;
                }

                rt_Input = false;
                isLightAttacking = true;
                playerAttacker.HandleHeavyAttack(playerInventory.rightWeapon);
                DisableComboWindow();
            }
        }

        private void HandleQuickSlotsInput()
        {
            if (d_Pad_Right)
            {
                playerInventory.ChangeRightWeapon();
                d_Pad_Right = false;
            }
            if (d_Pad_Left)
            {
                playerInventory.ChangeLeftWeapon();
                d_Pad_Left = false;
            }
        }

        private void HandleInventoryInput()
        {
            _inputActions.PlayerActions.Inventory.performed += i => inventory_input = true;

            if (inventory_input)
            {
                inventoryFlag = !inventoryFlag;

                if (inventoryFlag)
                {
                    uiManager.OpenSelectWindow();
                    uiManager.UpdateUI();
                    uiManager.hudWindow.SetActive(false);
                }
                else
                {
                    uiManager.CloseSelectWindow();
                    uiManager.CloseAllInventoryWindows();
                    uiManager.hudWindow.SetActive(true);
                }
            }
        }
    }
}