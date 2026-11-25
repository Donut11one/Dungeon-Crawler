using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Automatically connects PlayerController, PlayerAttack, and PlayerHealth
/// to the character's Animator component (on the player FBX model).
/// Drives Speed, IsGrounded, IsSprinting, Attack, and IsDead parameters.
/// </summary>
public class PlayerAnimationSync : NetworkBehaviour
{
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int GroundedParam = Animator.StringToHash("IsGrounded");
    private static readonly int SprintingParam = Animator.StringToHash("IsSprinting");
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int DeadParam = Animator.StringToHash("IsDead");

    [Header("Controller Fallback")]
    [Tooltip("Default animator controller to assign if the character model doesn't already have one.")]
    [SerializeField] private RuntimeAnimatorController defaultController;

    [Header("Runtime Debug (Read-Only)")]
    [SerializeField] private float debugSpeed;
    [SerializeField] private bool debugHasAnimator;
    [SerializeField] private bool debugHasController;

    private PlayerController playerController;
    private PlayerAttack playerAttack;
    private PlayerHealth playerHealth;
    private Rigidbody rb;
    private Animator animator;
    private Vector3 lastPosition;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerAttack = GetComponent<PlayerAttack>();
        playerHealth = GetComponent<PlayerHealth>();
        rb = GetComponent<Rigidbody>();
        EnsureAnimator();
    }

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void EnsureAnimator()
    {
        if (animator == null)
        {
            Transform charVisual = transform.Find("Body/CharacterVisual");
            if (charVisual != null)
            {
                animator = charVisual.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = charVisual.gameObject.AddComponent<Animator>();
                }
            }

            if (animator == null)
            {
                Transform monsterVisual = transform.Find("Body/MonsterVisual");
                if (monsterVisual != null)
                {
                    animator = monsterVisual.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = monsterVisual.gameObject.AddComponent<Animator>();
                    }
                }
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        Transform bodyTr = transform.Find("Body");
        if (bodyTr != null && animator != null && animator.transform != bodyTr)
        {
            Animator rogueAnim = bodyTr.GetComponent<Animator>();
            if (rogueAnim != null && rogueAnim != animator)
            {
                rogueAnim.enabled = false;
            }
        }

        if (animator == null)
        {
            if (bodyTr != null)
            {
                animator = bodyTr.gameObject.AddComponent<Animator>();
            }
        }

        if (animator != null && animator.runtimeAnimatorController == null && defaultController != null)
        {
            animator.runtimeAnimatorController = defaultController;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (playerAttack != null)
        {
            playerAttack.CurrentCombatState.OnValueChanged += OnCombatStateChanged;
        }

        if (playerHealth != null)
        {
            playerHealth.IsDead.OnValueChanged += OnDeadStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (playerAttack != null)
        {
            playerAttack.CurrentCombatState.OnValueChanged -= OnCombatStateChanged;
        }

        if (playerHealth != null)
        {
            playerHealth.IsDead.OnValueChanged -= OnDeadStateChanged;
        }
    }

    private void Update()
    {
        if (animator == null)
        {
            EnsureAnimator();
            if (animator == null)
            {
                return;
            }
        }

        if (!animator.enabled)
        {
            return;
        }

        // 1. Calculate horizontal movement speed (supports both physics-driven and kinematic/network-driven movement)
        float rbSpeed = 0f;
        if (rb != null && !rb.isKinematic)
        {
            Vector3 vel = rb.linearVelocity;
            vel.y = 0f;
            rbSpeed = vel.magnitude;
        }

        float displacementSpeed = 0f;
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        if (Time.deltaTime > 0.0001f)
        {
            displacementSpeed = delta.magnitude / Time.deltaTime;
        }
        lastPosition = transform.position;

        float horizontalSpeed = Mathf.Max(rbSpeed, displacementSpeed);

        debugSpeed = horizontalSpeed;
        debugHasAnimator = animator != null;
        debugHasController = animator != null && animator.runtimeAnimatorController != null;

        animator.SetFloat(SpeedParam, horizontalSpeed);

        // 2. Grounded status
        if (playerController != null)
        {
            animator.SetBool(GroundedParam, playerController.IsGrounded);
        }

        // 3. Sprinting status
        bool isSprinting = horizontalSpeed > 6.5f;
        animator.SetBool(SprintingParam, isSprinting);
    }

    private void OnCombatStateChanged(PlayerCombatState previous, PlayerCombatState current)
    {
        if (current == PlayerCombatState.Attacking)
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator != null && animator.enabled)
            {
                animator.SetTrigger(AttackTrigger);
            }
        }
    }

    private void OnDeadStateChanged(bool previous, bool current)
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            animator.SetBool(DeadParam, current);
        }
    }
}
