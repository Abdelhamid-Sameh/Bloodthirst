using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 6f;
    public float turnSpeed = 180f;

    [Header("Jumping & Gravity")]
    public float jumpHeight = 2f;       // Height for a jump
    public float gravity = -19.62f;     // Heavy gravity so jump isn't floaty
    private float verticalVelocity = 0f;

    [Header("Grounding")]
    public float groundedGraceTime = 0.15f;
    private float timeSinceGrounded;

    [Header("Cape Effects")]
    public Cloth capeCloth;
    public Vector3 jumpWindForce = new Vector3(0f, 5f, -2f); // Pushes cloth up and backward on jump

    private CharacterController controller;
    private Animator animator;
    private bool wasAnimGrounded = true;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
    }

    void Update()
    {
        HandleMovement();
    }

    void HandleMovement()
    {
        if (Keyboard.current == null) return;

        // 1. Read Inputs
        bool upPressed = Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed;
        bool downPressed = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;
        bool rightPressed = Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed;
        bool leftPressed = Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed;
        bool jumpPressed = Keyboard.current.spaceKey.wasPressedThisFrame;

        // 2. Turning Rotation
        float turnInput = 0f;
        if (rightPressed) turnInput += 1f;
        if (leftPressed) turnInput -= 1f;
        transform.Rotate(Vector3.up, turnInput * turnSpeed * Time.deltaTime, Space.World);

        // 3. Forward / Backward Motion
        float moveInput = 0f;
        if (upPressed) moveInput += 1f;
        if (downPressed) moveInput -= 1f;

        bool isMoving = Mathf.Abs(moveInput) > 0.01f;
        Vector3 moveDirection = transform.forward * moveInput;

        // 4. Grounded Check & Ground Snapping
        bool isGrounded = controller.isGrounded;
        if (isGrounded) timeSinceGrounded = 0f;
        else timeSinceGrounded += Time.deltaTime;

        if (isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f;

            // Reset cape wind force when landed
            if (capeCloth != null)
            {
                capeCloth.externalAcceleration = Vector3.zero;
            }
        }

        // 5. Jump Input
        bool jumped = false;
        bool canJump = timeSinceGrounded < groundedGraceTime && verticalVelocity <= 0.1f;
        if (jumpPressed && canJump)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumped = true;

            // Apply upward/backward wind to cape when jumping
            if (capeCloth != null)
            {
                capeCloth.externalAcceleration = jumpWindForce;
            }
        }

        // 6. Apply Gravity over time
        verticalVelocity += gravity * Time.deltaTime;

        // 7. Execute Total Movement
        Vector3 motion = (moveDirection * moveSpeed) + (Vector3.up * verticalVelocity);
        controller.Move(motion * Time.deltaTime);

        // 8. Update Animator Parameters
        if (animator != null)
        {
            bool animGrounded = timeSinceGrounded < groundedGraceTime && verticalVelocity <= 0.1f;

            animator.SetFloat("Speed", isMoving ? 1f : 0f);
            float turnTarget = isMoving ? 0f : turnInput;
            animator.SetFloat("Turn", turnTarget, 0.05f, Time.deltaTime);

            // Snap tiny leftovers to exactly 0
            if (turnTarget == 0f && Mathf.Abs(animator.GetFloat("Turn")) < 0.01f)
                animator.SetFloat("Turn", 0f);

            animator.SetBool("IsGrounded", animGrounded);

            if (jumped || (wasAnimGrounded && !animGrounded))
            {
                animator.SetTrigger("Jump");
            }
            wasAnimGrounded = animGrounded;
        }
    }
}