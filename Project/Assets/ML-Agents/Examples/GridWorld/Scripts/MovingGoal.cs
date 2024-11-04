using UnityEngine;
using Unity.MLAgents;

public class MovingGoal : MonoBehaviour
{
    public float moveSpeed = 1.5f;
    public float detectionRadius = 2.0f;
    public float bounceForce = 0.8f;
    
    private const float BOUNDARY_BUFFER = 0.5f;
    private const float GOAL_SCALE = 0.3f;
    private const float COLLISION_RADIUS = 0.25f;
    private const float DIRECTION_CHANGE_COOLDOWN = 1.0f;
    private const float MIN_SPEED = 0.1f;
    
    private Transform agentTransform;
    private Vector3 moveDirection;
    private float directionChangeTimer;
    private int gridSize;
    private LayerMask goalLayer;
    private LayerMask wallLayer;
    private bool isEscaping = false;
    private Vector3 lastSafePosition;

    public void Initialize(int size)
    {
        gridSize = size;
        transform.localScale = Vector3.one * GOAL_SCALE;
        
        gameObject.layer = LayerMask.NameToLayer("Goal");
        goalLayer = LayerMask.GetMask("Goal");
        wallLayer = LayerMask.GetMask("Wall");
        
        FindAgent();
        SetRandomDirection();
        lastSafePosition = transform.position;
    }

    void FindAgent()
    {
        var agentObj = GameObject.FindGameObjectWithTag("agent");
        if (agentObj != null)
        {
            agentTransform = agentObj.transform;
        }
    }

    void SetRandomDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        moveDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        directionChangeTimer = DIRECTION_CHANGE_COOLDOWN;
    }

    void Update()
    {
        if (agentTransform == null)
        {
            FindAgent();
            return;
        }

        // Ensure current position is valid, if not, reset to last safe position
        if (!IsValidPosition(transform.position))
        {
            transform.position = lastSafePosition;
            moveDirection = -moveDirection; // Reverse direction
            return;
        }

        // Store current position as last safe position
        lastSafePosition = transform.position;

        UpdateMovement();
        EnforceBoundaries();
        
        directionChangeTimer -= Time.deltaTime;
    }

    void UpdateMovement()
    {
        float distanceToAgent = Vector3.Distance(transform.position, agentTransform.position);
        
        if (distanceToAgent < detectionRadius)
        {
            if (!isEscaping || directionChangeTimer <= 0)
            {
                isEscaping = true;
                Vector3 escapeDir = (transform.position - agentTransform.position).normalized;
                // Add slight randomness to escape direction
                escapeDir += new Vector3(Random.Range(-0.1f, 0.1f), 0, Random.Range(-0.1f, 0.1f));
                moveDirection = escapeDir.normalized;
                directionChangeTimer = DIRECTION_CHANGE_COOLDOWN;
            }
        }
        else
        {
            isEscaping = false;
        }

        // Calculate proposed movement
        Vector3 proposedPosition = transform.position + moveDirection * moveSpeed * Time.deltaTime;
        
        // Check if proposed position is valid
        if (IsValidPosition(proposedPosition))
        {
            transform.position = proposedPosition;
        }
        else
        {
            // Handle collision by trying different directions
            HandleCollision();
        }
    }

    void HandleCollision()
    {
        // Try several different angles to find a valid direction
        float[] angles = { 45f, 90f, 135f, 180f, -135f, -90f, -45f };
        
        foreach (float angle in angles)
        {
            Quaternion rotation = Quaternion.Euler(0, angle, 0);
            Vector3 newDirection = rotation * -moveDirection;
            Vector3 proposedPosition = transform.position + newDirection * moveSpeed * Time.deltaTime;
            
            if (IsValidPosition(proposedPosition))
            {
                moveDirection = newDirection;
                transform.position = proposedPosition;
                return;
            }
        }
        
        // If no valid direction is found, stop movement temporarily
        moveDirection = Vector3.zero;
    }

    void EnforceBoundaries()
    {
        Vector3 currentPos = transform.position;
        Vector3 clampedPos = new Vector3(
            Mathf.Clamp(currentPos.x, BOUNDARY_BUFFER, gridSize - BOUNDARY_BUFFER),
            currentPos.y,
            Mathf.Clamp(currentPos.z, BOUNDARY_BUFFER, gridSize - BOUNDARY_BUFFER)
        );

        // If position was clamped, we hit a boundary
        if (currentPos != clampedPos)
        {
            transform.position = clampedPos;
            // Reflect movement direction based on which boundary was hit
            if (currentPos.x != clampedPos.x) moveDirection.x = -moveDirection.x;
            if (currentPos.z != clampedPos.z) moveDirection.z = -moveDirection.z;
            moveDirection = moveDirection.normalized;
        }
    }

    bool IsValidPosition(Vector3 position)
    {
        // First check boundaries
        if (position.x < BOUNDARY_BUFFER || position.x > gridSize - BOUNDARY_BUFFER ||
            position.z < BOUNDARY_BUFFER || position.z > gridSize - BOUNDARY_BUFFER)
        {
            return false;
        }

        // Check for collisions with other goals using overlap sphere
        Collider[] hitColliders = Physics.OverlapSphere(position, COLLISION_RADIUS, goalLayer);
        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.gameObject != gameObject)
            {
                return false;
            }
        }

        // Check for wall collisions
        if (Physics.OverlapSphere(position, COLLISION_RADIUS, wallLayer).Length > 0)
        {
            return false;
        }

        return true;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // Draw detection radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        
        // Draw collision radius
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, COLLISION_RADIUS);
        
        // Draw movement direction
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, moveDirection * 1.5f);
        
        // Draw boundaries
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(new Vector3((gridSize-1)/2f, 0, (gridSize-1)/2f), 
            new Vector3(gridSize - 2*BOUNDARY_BUFFER, 0.1f, gridSize - 2*BOUNDARY_BUFFER));
    }

    void OnCollisionEnter(Collision collision)
    {
        // Additional collision handling
        if (collision.gameObject.CompareTag("wall") || collision.gameObject.layer == LayerMask.NameToLayer("Goal"))
        {
            transform.position = lastSafePosition;
            HandleCollision();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Additional trigger handling
        if (other.CompareTag("wall") || other.gameObject.layer == LayerMask.NameToLayer("Goal"))
        {
            transform.position = lastSafePosition;
            HandleCollision();
        }
    }
}