using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SG
{
    public class PlayerManager : MonoBehaviour
    {
        InputHandler _input;
        Animator _anim;
        CameraHandler cameraHandler;
        PlayerLocomotion playerLocomotion;
        InteractableUI interactableUI;
        public GameObject interactableUIGameObject;
        public GameObject itemInteractableGameObject;

        private bool _lastIsRolling;

        [Header("Player Flags")]
        public bool isInAir;
        public bool isGrounded;
        public bool isDead;
        public bool canDoCombo;

        [SerializeField] float rollEndThreshold = 0.9f;


        private void Awake()
        {
            cameraHandler = FindObjectOfType<CameraHandler>();
        }
        private void Start()
        {
            _input = GetComponent<InputHandler>();
            _anim = GetComponentInChildren<Animator>();
            playerLocomotion = GetComponent<PlayerLocomotion>();
            interactableUI = FindObjectOfType<InteractableUI>();
            isDead = false;
        }

        private void Update()
        {
            if (isDead) return;

            float delta = Time.deltaTime;
            AnimatorStateInfo stateInfo = _anim.GetCurrentAnimatorStateInfo(0);
            bool isRolling = (stateInfo.IsName("Rolling") || stateInfo.IsName("BackStep") ||
                             stateInfo.IsName("RollLeft") || stateInfo.IsName("RollRight"))
                             && stateInfo.normalizedTime < rollEndThreshold;

            canDoCombo = _anim.GetBool("canDoCombo");
            _anim.SetBool("isInAir", isInAir);

            if (_lastIsRolling && !isRolling)
            {
                _input.isInteracting = false;
                _anim.SetBool("isInteracting", false);
                _input.ResetRollFlag();
            }

            if (!isInAir && _input.isInteracting != isRolling)
            {
                _input.isInteracting = isRolling;
                _anim.SetBool("isInteracting", isRolling);
            }

            _lastIsRolling = isRolling;

            playerLocomotion.HandleJumping();
            CheckForInteractableObject();
        }

        private void LateUpdate()
        {
            if (isDead) return;

            _input.rb_Input = false;
            _input.rt_Input = false;
            _input.d_Pad_Up = false;
            _input.d_Pad_Down = false;
            _input.d_Pad_Left = false;
            _input.d_Pad_Right = false;
            _input.jump_input = false;
            _input.inventory_input = false;

            if (isInAir)
            {
                playerLocomotion.inAirTimer += Time.deltaTime;
            }
        }

        public void CheckForInteractableObject()
        {
            RaycastHit hit;
            Vector3 detectDirection = cameraHandler.cameraTransform.forward;
            if (Physics.SphereCast(transform.position, 0.3f, detectDirection, out hit, 1f, cameraHandler.ignoreLayers))
            {
                if (hit.collider.CompareTag("interactable"))
                {
                    Interactable interactableObject = hit.collider.GetComponent<Interactable>();

                    if (interactableObject != null)
                    {
                        string interactableText = interactableObject.interactableText;
                        interactableUI.interactableText.text = interactableText;
                        interactableUIGameObject.SetActive(true);

                        if (_input.a_Input)
                        {
                            interactableObject.Interact(this);
                            _input.a_Input = false;
                        }
                    }
                }
            }
            else
            {
                if (interactableUIGameObject != null)
                {
                    interactableUIGameObject.SetActive(false);
                }

                if (itemInteractableGameObject != null && _input.a_Input)
                {
                    itemInteractableGameObject.SetActive(false);
                }
            }
        }
    }
}