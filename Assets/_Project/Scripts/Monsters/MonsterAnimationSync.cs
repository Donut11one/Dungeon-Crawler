using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Automatically connects BasicEnemyAI and EnemyHealth to the monster's
/// Animator component (on the monster FBX model).
/// Drives Speed, IsChasing, Attack, IsStunned, and IsDead parameters.
/// </summary>
public class MonsterAnimationSync : NetworkBehaviour
{
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int ChasingParam = Animator.StringToHash("IsChasing");
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int StunnedParam = Animator.StringToHash("IsStunned");
    private static readonly int DeadParam = Animator.StringToHash("IsDead");

    [Header("Controller Fallback")]
    [Tooltip("Default animator controller to assign if the monster model doesn't already have one.")]
    [SerializeField] private RuntimeAnimatorController defaultController;

    [Header("Runtime Debug (Read-Only)")]
    [SerializeField] private float debugSpeed;
    [SerializeField] private bool debugHasAnimator;
    [SerializeField] private bool debugHasController;

    private BasicEnemyAI enemyAI;
    private EnemyHealth enemyHealth;
    private NavMeshAgent agent;
    private Rigidbody rb;
    private Animator animator;
    private Vector3 lastPosition;

    private void Awake()
    {
        enemyAI = GetComponent<BasicEnemyAI>();
        enemyHealth = GetComponent<EnemyHealth>();
        agent = GetComponent<NavMeshAgent>();
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
            Transform monsterVisual = transform.Find("Body/MonsterVisual");
            if (monsterVisual != null)
            {
                animator = monsterVisual.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = monsterVisual.gameObject.AddComponent<Animator>();
                }
            }

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
        if (enemyAI != null)
        {
            enemyAI.CurrentCombatState.OnValueChanged += OnCombatStateChanged;
            enemyAI.IsStunned.OnValueChanged += OnStunnedChanged;
        }

        if (enemyHealth != null)
        {
            enemyHealth.IsDead.OnValueChanged += OnDeadChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (enemyAI != null)
        {
            enemyAI.CurrentCombatState.OnValueChanged -= OnCombatStateChanged;
            enemyAI.IsStunned.OnValueChanged -= OnStunnedChanged;
        }

        if (enemyHealth != null)
        {
            enemyHealth.IsDead.OnValueChanged -= OnDeadChanged;
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

        // 1. Calculate horizontal movement speed (combines displacement, NavMeshAgent, and physics)
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        float displacementSpeed = Time.deltaTime > 0.0001f ? (delta.magnitude / Time.deltaTime) : 0f;
        lastPosition = transform.position;

        float agentSpeed = (agent != null && agent.enabled) ? agent.velocity.magnitude : 0f;
        float rbSpeed = (rb != null && !rb.isKinematic) ? new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude : 0f;

        float speed = Mathf.Max(displacementSpeed, Mathf.Max(agentSpeed, rbSpeed));

        debugSpeed = speed;
        debugHasAnimator = animator != null;
        debugHasController = animator != null && animator.runtimeAnimatorController != null;

        animator.SetFloat(SpeedParam, speed);

        // 2. Chasing / Aggro state
        if (enemyAI != null)
        {
            animator.SetBool(ChasingParam, enemyAI.IsAggroed.Value);
        }
    }

    private void OnCombatStateChanged(BasicEnemyAI.CombatState previous, BasicEnemyAI.CombatState current)
    {
        if (current == BasicEnemyAI.CombatState.Attacking)
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

    private void OnStunnedChanged(bool previous, bool current)
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            animator.SetBool(StunnedParam, current);
        }
    }

    private void OnDeadChanged(bool previous, bool current)
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
