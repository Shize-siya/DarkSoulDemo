using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SG
{
    public class PlayerStats : MonoBehaviour
    {
        public int healthLevel = 10;
        public int maxHealth;
        public int currentHealth;

        public int staminaLevel = 10;
        public int maxStamina;
        public int currentStamina;

        // 新增：耐力恢复配置（可在Inspector面板调整）
        [Header("耐力恢复配置")]
        public float staminaRecoverDelay = 1f; // 停止消耗后延迟恢复时间
        public int staminaRecoverAmount = 2;   // 每次恢复的耐力值
        public float staminaRecoverInterval = 0.2f; // 耐力恢复间隔

        HealthBar healthBar;
        StaminaBar staminaBar;
        AnimatorHandler animatorHandler;
        InputHandler inputHandler;
        PlayerManager playerManager;

        [Header("受伤锁定配置")]
        public float damageLockDuration = 0.1f;
        private Coroutine damageLockCoroutine;
        private Coroutine staminaRecoverCoroutine; // 新增：耐力恢复协程

        private void Awake()
        {
            animatorHandler = GetComponentInChildren<AnimatorHandler>();
            inputHandler = GetComponent<InputHandler>();
            playerManager = GetComponent<PlayerManager>();
            healthBar = FindObjectOfType<HealthBar>();
            staminaBar = FindObjectOfType<StaminaBar>();
        }

        void Start()
        {
            maxHealth = SetMaxHealthFromHealthLevel();
            currentHealth = maxHealth;
            healthBar.SetMaxHealth(maxHealth);

            maxStamina = SetMaxStaminaFromStaminaLevel();
            currentStamina = maxStamina;
            staminaBar.SetMaxStamina(maxStamina);
        }

        private int SetMaxHealthFromHealthLevel()
        {
            maxHealth = healthLevel * 10;
            return maxHealth;
        }

        private int SetMaxStaminaFromStaminaLevel()
        {
            maxStamina = staminaLevel * 10;
            return maxStamina;
        }

        public void TakeDamage(int damage)
        {
            if (playerManager.isDead) return;

            currentHealth = currentHealth - damage;
            healthBar.SetCurrentHealth(currentHealth);
            animatorHandler.PlayTargetAnimation("Damage_01", true);
            StartDamageLock();
            StopStaminaRecover(); // 新增：受伤时暂停耐力恢复

            if (currentHealth <= 0)
            {
                currentHealth = 0;
                animatorHandler.PlayTargetAnimation("Death_01", true);
                playerManager.isDead = true;
                inputHandler.isInteracting = false;
                inputHandler.rollFlag = false;
                inputHandler.sprintFlag = false;
                inputHandler.isLightAttacking = false;
            }
        }

        public void TakeStaminaDamage(int damage)
        {
            if (currentStamina <= 0 || playerManager.isDead) return;
            currentStamina = Mathf.Max(currentStamina - damage, 0); // 保证耐力不小于0
            staminaBar.SetCurrentStamina(currentStamina);

            // 新增：消耗耐力时重启恢复延迟
            StopStaminaRecover();
            staminaRecoverCoroutine = StartCoroutine(StartStaminaRecoverAfterDelay());
        }

        // 新增：延迟后开始持续恢复耐力
        private IEnumerator StartStaminaRecoverAfterDelay()
        {
            yield return new WaitForSeconds(staminaRecoverDelay);
            // 持续恢复：未满血、未死亡、未受伤
            while (currentStamina < maxStamina && !playerManager.isDead && !inputHandler.isTakingDamage)
            {
                currentStamina = Mathf.Min(currentStamina + staminaRecoverAmount, maxStamina); // 保证耐力不溢出
                staminaBar.SetCurrentStamina(currentStamina);
                yield return new WaitForSeconds(staminaRecoverInterval);
            }
            staminaRecoverCoroutine = null;
        }

        // 新增：停止耐力恢复协程
        public void StopStaminaRecover()
        {
            if (staminaRecoverCoroutine != null)
            {
                StopCoroutine(staminaRecoverCoroutine);
                staminaRecoverCoroutine = null;
            }
        }

        private void StartDamageLock()
        {
            if (damageLockCoroutine != null)
            {
                StopCoroutine(damageLockCoroutine);
            }
            inputHandler.isTakingDamage = true;
            damageLockCoroutine = StartCoroutine(EndDamageLockAfterDelay(damageLockDuration));
        }

        private IEnumerator EndDamageLockAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            inputHandler.isTakingDamage = false;
            // 新增：解除受伤后自动恢复耐力
            if (currentStamina < maxStamina && !playerManager.isDead)
            {
                staminaRecoverCoroutine = StartCoroutine(StartStaminaRecoverAfterDelay());
            }
            damageLockCoroutine = null;
        }

        // 新增：对象禁用/死亡时停止恢复
        private void OnDisable()
        {
            StopStaminaRecover();
        }
    }
}